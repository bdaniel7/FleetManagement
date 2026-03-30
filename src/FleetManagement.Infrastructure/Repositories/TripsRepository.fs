module FleetManagement.Infrastructure.Repositories.TripRepository

open System
open System.Text.Json
open FleetManagement.Core.Domain
open FleetManagement.Infrastructure.DbContext
open FleetManagement.Infrastructure.IRepositories

// ── DB row ────────────────────────────────────────────────────

[<CLIMutable>]
type TripRow = {
    id                : Guid
    name              : string
    description       : string
    vehicle_id        : Guid Nullable
    driver_id         : Guid Nullable
    status            : string
    is_circular       : bool
    total_distance_km : float
    waypoints_json    : string
    created_at        : DateTimeOffset
    updated_at        : DateTimeOffset
    started_at        : DateTimeOffset Nullable
    completed_at      : DateTimeOffset Nullable
}

// ── Mapping ───────────────────────────────────────────────────

module private Mapping =

    let parseStatus = function
        | "Draft"        -> TripStatus.Draft
        | "Scheduled"    -> TripStatus.Scheduled
        | "InProgress"   -> TripStatus.InProgress
        | "Completed"    -> TripStatus.TripCompleted
        | "Cancelled"    -> TripStatus.TripCancelled
        | s              -> failwithf "Unknown TripStatus: %s" s

    let statusStr = function
        | TripStatus.Draft          -> "Draft"
        | TripStatus.Scheduled      -> "Scheduled"
        | TripStatus.InProgress     -> "InProgress"
        | TripStatus.TripCompleted  -> "Completed"
        | TripStatus.TripCancelled  -> "Cancelled"

    let parseWaypoints (json: string) : TripWaypoint list =
        if String.IsNullOrWhiteSpace json || json = "[]" then []
        else
            try
                JsonSerializer.Deserialize<
                    {| order: int; label: string; lat: float; lon: float
                       notes: string; dwellMin: int |}[]>(json)
                |> Array.map (fun w ->
                    { Order      = w.order
                      Label      = w.label
                      Coordinate = { Latitude = w.lat; Longitude = w.lon }
                      Notes      = w.notes
                      DwellMin   = w.dwellMin })
                |> Array.sortBy (fun w -> w.Order)
                |> Array.toList
            with _ -> []

    let waypointsJson (waypoints: TripWaypoint list) =
        let items =
            waypoints |> List.map (fun w ->
                {| order    = w.Order
                   label    = w.Label
                   lat      = w.Coordinate.Latitude
                   lon      = w.Coordinate.Longitude
                   notes    = w.Notes
                   dwellMin = w.DwellMin |})
        JsonSerializer.Serialize(items, JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase))

    let toDomain (r: TripRow) : Trip = {
        Id              = TripId r.id
        Name            = r.name
        Description     = r.description
        VehicleId       = if r.vehicle_id.HasValue then Some (VehicleId r.vehicle_id.Value) else None
        DriverId        = if r.driver_id.HasValue  then Some (DriverId  r.driver_id.Value)  else None
        Status          = parseStatus r.status
        IsCircular      = r.is_circular
        TotalDistanceKm = r.total_distance_km
        Waypoints       = parseWaypoints r.waypoints_json
        CreatedAt       = r.created_at
        UpdatedAt       = r.updated_at
        StartedAt       = if r.started_at.HasValue  then Some r.started_at.Value  else None
        CompletedAt     = if r.completed_at.HasValue then Some r.completed_at.Value else None
    }

// ── Haversine helper for total distance ───────────────────────

let private haversineKm (lat1, lon1) (lat2, lon2) =
    let toRad d = d * Math.PI / 180.0
    let R = 6371.0
    let dLat = toRad (lat2 - lat1)
    let dLon = toRad (lon2 - lon1)
    let a =
        Math.Sin(dLat/2.0)**2.0 +
        Math.Cos(toRad lat1) * Math.Cos(toRad lat2) *
        Math.Sin(dLon/2.0)**2.0
    R * 2.0 * Math.Atan2(Math.Sqrt a, Math.Sqrt(1.0 - a))

let computeDistance (waypoints: TripWaypoint list) (isCircular: bool) =
    let coords = waypoints |> List.map (fun w -> w.Coordinate.Latitude, w.Coordinate.Longitude)
    let segments =
        if isCircular && coords.Length > 1 then coords @ [List.head coords]
        else coords
    segments
    |> List.pairwise
    |> List.sumBy (fun (a, b) -> haversineKm a b)

// ── Repository implementation ─────────────────────────────────

type PostgresTripsRepository(ctx: IDbContext) =

    let selectAll = """
        SELECT id, name, description, vehicle_id, driver_id,
               status, is_circular, total_distance_km, waypoints_json,
               created_at, updated_at, started_at, completed_at
        FROM public.fms_trips"""

    interface ITripsRepository with

        member _.GetAll () = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<TripRow>
                            $"{selectAll} ORDER BY created_at DESC" {| |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.GetById (TripId tid) = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! row = Db.queryFirst<TripRow>
                           $"{selectAll} WHERE id = @id" {| id = tid |} conn
            return row |> Option.map Mapping.toDomain
        }

        member _.GetByVehicle (VehicleId vid) = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<TripRow>
                            $"{selectAll} WHERE vehicle_id = @vid ORDER BY created_at DESC"
                            {| vid = vid |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.GetByStatus status = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<TripRow>
                            $"{selectAll} WHERE status = @status ORDER BY created_at DESC"
                            {| status = Mapping.statusStr status |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.Upsert trip = async {
            let (TripId tid)  = trip.Id
            let vid = trip.VehicleId |> Option.map (fun (VehicleId v) -> v) |> Option.toNullable
            let did = trip.DriverId  |> Option.map (fun (DriverId  d) -> d) |> Option.toNullable
            let dist = computeDistance trip.Waypoints trip.IsCircular
            let sql = """
                INSERT INTO public.fms_trips (id, name, description, vehicle_id, driver_id,
                    status, is_circular, total_distance_km, waypoints_json,
                    created_at, updated_at, started_at, completed_at)
                VALUES (@id, @name, @desc, @vid, @did,
                    @status, @circular, @dist, @waypoints::jsonb,
                    @created_at, @updated_at, @started_at, @completed_at)
                ON CONFLICT (id) DO UPDATE SET
                    name              = EXCLUDED.name,
                    description       = EXCLUDED.description,
                    vehicle_id        = EXCLUDED.vehicle_id,
                    driver_id         = EXCLUDED.driver_id,
                    status            = EXCLUDED.status,
                    is_circular       = EXCLUDED.is_circular,
                    total_distance_km = EXCLUDED.total_distance_km,
                    waypoints_json    = EXCLUDED.waypoints_json,
                    updated_at        = NOW(),
                    started_at        = EXCLUDED.started_at,
                    completed_at      = EXCLUDED.completed_at"""
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute sql
                             {| id          = tid
                                name        = trip.Name
                                desc        = trip.Description
                                vid         = vid
                                did         = did
                                status      = Mapping.statusStr trip.Status
                                circular    = trip.IsCircular
                                dist        = dist
                                waypoints   = Mapping.waypointsJson trip.Waypoints
                                created_at  = trip.CreatedAt
                                updated_at  = trip.UpdatedAt
                                started_at  = trip.StartedAt |> Option.toNullable
                                completed_at= trip.CompletedAt |> Option.toNullable |} conn tx
                return ()
            })
        }

        member _.UpdateStatus (TripId tid, status) = async {
            let startedAt =
                match status with
                | TripStatus.InProgress -> Some DateTimeOffset.UtcNow |> Option.toNullable
                | _ -> Nullable()
            let completedAt =
                match status with
                | TripStatus.TripCompleted | TripStatus.TripCancelled ->
                    Some DateTimeOffset.UtcNow |> Option.toNullable
                | _ -> Nullable()
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            """UPDATE public.fms_trips SET
                               status       = @status,
                               updated_at   = NOW(),
                               started_at   = COALESCE(@started_at,   started_at),
                               completed_at = COALESCE(@completed_at, completed_at)
                               WHERE id = @id"""
                            {| id           = tid
                               status       = Mapping.statusStr status
                               started_at   = startedAt
                               completed_at = completedAt |} conn tx
                return ()
            })
        }

        member _.Delete (TripId tid) = async {
            let! deleted = ctx.InTransaction(fun conn tx -> async {
                let! rows = Db.execute "DELETE FROM public.fms_trips WHERE id = @id" {| id = tid |} conn tx
                return rows > 0
            })
            return deleted
        }
