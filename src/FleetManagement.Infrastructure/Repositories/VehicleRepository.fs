module FleetManagement.Infrastructure.Repositories.VehicleRepository

open System
open FleetManagement.Core.Domain
open FleetManagement.Infrastructure.DbContext
open FleetManagement.Infrastructure.IRepositories

// ============================================================
//  DTO mapping (flat Dapper row → domain type)
// ============================================================

[<CLIMutable>]
type VehicleRow = {
    id                  : Guid
    license_plate       : string
    vehicle_type        : string
    status              : string
    lat                 : float
    lon                 : float
    assigned_driver_id  : Guid Nullable
    fuel_level_pct      : float
    speed_kmh           : float
    max_payload_kg      : float
    current_payload_kg  : float
    odometer_km         : float
    engine_temp         : float
    battery_level       : float Nullable
    last_heartbeat      : DateTimeOffset
    diagnostic_codes    : string   // JSON array
    created_at          : DateTimeOffset
    updated_at          : DateTimeOffset
}

module private Mapping =
    let parseVehicleType = function
        | "Truck"        -> VehicleType.Truck
        | "Van"          -> VehicleType.Van
        | "Car"          -> VehicleType.Car
        | "Motorcycle"   -> VehicleType.Motorcycle
        | "ElectricTruck"-> VehicleType.ElectricTruck
        | "ElectricVan"  -> VehicleType.ElectricVan
        | other          -> failwithf "Unknown vehicle type: %s" other

    let parseStatus = function
        | "Idle"        -> VehicleStatus.Idle
        | "EnRoute"     -> VehicleStatus.EnRoute
        | "Maintenance" -> VehicleStatus.Maintenance
        | "OutOfService"-> VehicleStatus.OutOfService
        | "Charging"    -> VehicleStatus.Charging
        | other         -> failwithf "Unknown vehicle status: %s" other

    let parseDiagCodes (json: string) : string list =
        if String.IsNullOrWhiteSpace json || json = "[]" then []
        else
            json.Trim('[', ']').Split(',')
            |> Array.map (fun s -> s.Trim('"', ' '))
            |> Array.toList

    let toDomain (row: VehicleRow) : Vehicle = {
        Id              = VehicleId row.id
        LicensePlate    = row.license_plate
        VehicleType     = parseVehicleType row.vehicle_type
        Status          = parseStatus row.status
        CurrentLocation = { Latitude = row.lat; Longitude = row.lon }
        AssignedDriver  = if row.assigned_driver_id.HasValue then Some (DriverId row.assigned_driver_id.Value) else None
        FuelLevelPct    = row.fuel_level_pct
        SpeedKmh        = row.speed_kmh
        MaxPayloadKg    = row.max_payload_kg
        CurrentPayloadKg= row.current_payload_kg
        Telemetry = {
            OdometerKm      = row.odometer_km
            EngineTemp      = row.engine_temp
            BatteryLevel    = if row.battery_level.HasValue then Some row.battery_level.Value else None
            LastHeartbeat   = row.last_heartbeat
            DiagnosticCodes = parseDiagCodes row.diagnostic_codes
        }
        CreatedAt = row.created_at
        UpdatedAt = row.updated_at
    }

    let toParams (v: Vehicle) =
        let (VehicleId vid)   = v.Id
        let driverId          = v.AssignedDriver |> Option.map (fun (DriverId d) -> d) |> Option.toNullable
        let diagJson          =
            v.Telemetry.DiagnosticCodes
            |> List.map (fun s -> $"\"{s}\"")
            |> String.concat ", "
            |> fun s -> $"[{s}]"
        {| id                 = vid
           license_plate      = v.LicensePlate
           vehicle_type       = string v.VehicleType
           status             = string v.Status
           lat                = v.CurrentLocation.Latitude
           lon                = v.CurrentLocation.Longitude
           assigned_driver_id = driverId
           fuel_level_pct     = v.FuelLevelPct
           speed_kmh          = v.SpeedKmh
           max_payload_kg     = v.MaxPayloadKg
           current_payload_kg = v.CurrentPayloadKg
           odometer_km        = v.Telemetry.OdometerKm
           engine_temp        = v.Telemetry.EngineTemp
           battery_level      = v.Telemetry.BatteryLevel |> Option.toNullable
           last_heartbeat     = v.Telemetry.LastHeartbeat
           diagnostic_codes   = diagJson
           created_at         = v.CreatedAt
           updated_at         = v.UpdatedAt |}

// ============================================================
//  Repository interface
// ============================================================
//  PostgreSQL implementation
// ============================================================

type PostgresVehicleRepository(ctx: IDbContext) =

    let selectAll = """
        SELECT id, license_plate, vehicle_type, status,
               lat, lon, assigned_driver_id,
               fuel_level_pct, speed_kmh, max_payload_kg, current_payload_kg,
               odometer_km, engine_temp, battery_level,
               last_heartbeat, diagnostic_codes,
               created_at, updated_at
        FROM public.fms_vehicles"""

    interface IVehicleRepository with

        member _.GetById (VehicleId vid) = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! row = Db.queryFirst<VehicleRow>
                            $"{selectAll} WHERE id = @id"
                            {| id = vid |} conn
            return row |> Option.map Mapping.toDomain
        }

        member _.GetAll () = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<VehicleRow>
                            $"{selectAll} ORDER BY created_at DESC"
                            {| |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.GetByStatus status = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<VehicleRow>
                            $"{selectAll} WHERE status = @status ORDER BY updated_at DESC"
                            {| status = string status |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.Upsert vehicle = async {
            let p = Mapping.toParams vehicle
            let sql = """
                INSERT INTO public.fms_vehicles (
                    id, license_plate, vehicle_type, status,
                    lat, lon, assigned_driver_id,
                    fuel_level_pct, speed_kmh, max_payload_kg, current_payload_kg,
                    odometer_km, engine_temp, battery_level,
                    last_heartbeat, diagnostic_codes, created_at, updated_at)
                VALUES (
                    @id, @license_plate, @vehicle_type, @status,
                    @lat, @lon, @assigned_driver_id,
                    @fuel_level_pct, @speed_kmh, @max_payload_kg, @current_payload_kg,
                    @odometer_km, @engine_temp, @battery_level,
                    @last_heartbeat, @diagnostic_codes::jsonb, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    license_plate       = EXCLUDED.license_plate,
                    vehicle_type        = EXCLUDED.vehicle_type,
                    status              = EXCLUDED.status,
                    lat                 = EXCLUDED.lat,
                    lon                 = EXCLUDED.lon,
                    assigned_driver_id  = EXCLUDED.assigned_driver_id,
                    fuel_level_pct      = EXCLUDED.fuel_level_pct,
                    speed_kmh           = EXCLUDED.speed_kmh,
                    max_payload_kg      = EXCLUDED.max_payload_kg,
                    current_payload_kg  = EXCLUDED.current_payload_kg,
                    odometer_km         = EXCLUDED.odometer_km,
                    engine_temp         = EXCLUDED.engine_temp,
                    battery_level       = EXCLUDED.battery_level,
                    last_heartbeat      = EXCLUDED.last_heartbeat,
                    diagnostic_codes    = EXCLUDED.diagnostic_codes,
                    updated_at          = EXCLUDED.updated_at"""
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute sql p conn tx
                return ()
            })
        }

        member _.Delete (VehicleId vid) = async {
            let! result = ctx.InTransaction(fun conn tx -> async {
                let! rows = Db.execute "DELETE FROM public.fms_vehicles WHERE id = @id" {| id = vid |} conn tx
                return rows > 0
            })
            return result
        }

        member _.UpdateLocation (VehicleId vid, loc, speed) = async {
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            "UPDATE public.fms_vehicles SET lat=@lat, lon=@lon, speed_kmh=@speed, updated_at=NOW() WHERE id=@id"
                            {| id=vid; lat=loc.Latitude; lon=loc.Longitude; speed=speed |} conn tx
                return ()
            })
        }

        member _.UpdateStatus (VehicleId vid, status) = async {
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            "UPDATE public.fms_vehicles SET status=@status, updated_at=NOW() WHERE id=@id"
                            {| id=vid; status=string status |} conn tx
                return ()
            })
        }

        member _.UpdateFuel (VehicleId vid, pct) = async {
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            "UPDATE public.fms_vehicles SET fuel_level_pct=@pct, updated_at=NOW() WHERE id=@id"
                            {| id=vid; pct=pct |} conn tx
                return ()
            })
        }
