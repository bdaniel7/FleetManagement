module FleetManagement.API.Endpoints.VehicleEndpoints

open System
open System.Threading.Tasks
open FleetManagement.Infrastructure.IRepositories
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Akka.Actor
open FleetManagement.Core.Domain
open FleetManagement.Actors.ActorMessages
open FleetManagement.Core.Validation
open Serilog

// ============================================================
//  DTOs
// ============================================================

[<CLIMutable>]
type RegisterVehicleRequest = {
    LicensePlate    : string
    VehicleType     : string
    MaxPayloadKg    : float
    Latitude        : float
    Longitude       : float
}

[<CLIMutable>]
type UpdateLocationRequest = {
    Latitude  : float
    Longitude : float
    SpeedKmh  : float
}

[<CLIMutable>]
type UpdateStatusRequest = {
    Status : string
}

// ============================================================
//  Endpoint mapping
// ============================================================

let mapVehicleEndpoints (app: IEndpointRouteBuilder) (repo: IVehicleRepository) (fleetSupervisor: IActorRef) =

    let tag = "Vehicles"

    // GET /api/vehicles
    app.MapGet("/api/vehicles", Func<Task<IResult>>(fun () -> task {
        let! vehicles = repo.GetAll()
        return Results.Ok vehicles
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/vehicles/{id}
    app.MapGet("/api/vehicles/{id:guid}", Func<Guid, Task<IResult>>(fun id-> task {
        let! vehicle = repo.GetById (VehicleId id)
        return
            match vehicle with
            | Some v -> Results.Ok v
            | None   -> Results.NotFound {| error = $"Vehicle {id} not found" |}
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/vehicles/status/{status}
    app.MapGet("/api/vehicles/status/{status}", Func<string, Task<IResult>>(fun status -> task {
        let parsed =
            match status with
            | "Idle"        -> Some VehicleStatus.Idle
            | "En Route"     -> Some VehicleStatus.EnRoute
            | "Maintenance" -> Some VehicleStatus.Maintenance
            | "Out Of Service"-> Some VehicleStatus.OutOfService
            | "Charging"    -> Some VehicleStatus.Charging
            | _             -> None
        match parsed with
        | None   -> return Results.BadRequest {| error = $"Unknown status: {status}" |}
        | Some s ->
            let! vehicles = repo.GetByStatus s
            return Results.Ok vehicles
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // POST /api/vehicles
    app.MapPost("/api/vehicles", Func<RegisterVehicleRequest, Task<IResult>>(fun req -> task {
        let vtype =
            match req.VehicleType with
            | "Truck"        -> Some VehicleType.Truck
            | "Van"          -> Some VehicleType.Van
            | "Car"          -> Some VehicleType.Car
            | "Motorcycle"   -> Some VehicleType.Motorcycle
            | "Electric Truck"-> Some VehicleType.ElectricTruck
            | "Electric Van"  -> Some VehicleType.ElectricVan
            | _              -> None

        match vtype with
        | None -> return Results.BadRequest {| error = $"Unknown vehicle type: {req.VehicleType}" |}
        | Some vt ->
            let location = { Latitude = req.Latitude; Longitude = req.Longitude }
            match validateVehicleCreate req.LicensePlate vt req.MaxPayloadKg location with
            | Invalid errors ->
                return Results.UnprocessableEntity {| errors = errors |> List.map (fun e -> {| field=e.Field; message=e.Message |}) |}
            | Valid _ ->
                let vehicle = {
                    Id               = VehicleId (Guid.NewGuid())
                    LicensePlate     = req.LicensePlate.Trim().ToUpperInvariant()
                    VehicleType      = vt
                    Status           = VehicleStatus.Idle
                    CurrentLocation  = location
                    AssignedDriver   = None
                    FuelLevelPct     = 100.0
                    SpeedKmh         = 0.0
                    MaxPayloadKg     = req.MaxPayloadKg
                    CurrentPayloadKg = 0.0
                    Telemetry = {
                        OdometerKm      = 0.0
                        EngineTemp      = 20.0
                        BatteryLevel    = None
                        LastHeartbeat   = DateTimeOffset.UtcNow
                        DiagnosticCodes = []
                    }
                    CreatedAt = DateTimeOffset.UtcNow
                    UpdatedAt = DateTimeOffset.UtcNow
                }
                do! repo.Upsert vehicle
                fleetSupervisor.Tell (RegisterVehicle vehicle)
                return Results.Created($"/api/vehicles/{let (VehicleId id) = vehicle.Id in id}", vehicle)
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // PATCH /api/vehicles/{id}/location
    app.MapPatch("/api/vehicles/{id:guid}/location", Func<Guid, UpdateLocationRequest, Task<IResult>>(fun (id) (req) -> task {
        let vid      = VehicleId id
        let location = { Latitude = req.Latitude; Longitude = req.Longitude }
        do! repo.UpdateLocation(vid, location, req.SpeedKmh)
        fleetSupervisor.Tell (GetVehicle(vid, ActorRefs.Nobody))
        return Results.NoContent()
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // PATCH /api/vehicles/{id}/status
    app.MapPatch("/api/vehicles/{id:guid}/status", Func<Guid, UpdateStatusRequest, Task<IResult>>(fun (id) (req) -> task {
        let parsed =
            match req.Status with
            | "Idle"          -> Some VehicleStatus.Idle
            | "En Route"      -> Some VehicleStatus.EnRoute
            | "Maintenance"   -> Some VehicleStatus.Maintenance
            | "Out Of Service"-> Some VehicleStatus.OutOfService
            | "Charging"      -> Some VehicleStatus.Charging
            | _               -> None
        match parsed with
        | None   -> return Results.BadRequest {| error = $"Unknown status: {req.Status}" |}
        | Some s ->
            do! repo.UpdateStatus(VehicleId id, s)
            return Results.NoContent()
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // DELETE /api/vehicles/{id}
    app.MapDelete("/api/vehicles/{id:guid}", Func<Guid, Task<IResult>>(fun (id: Guid) -> task {
        let! deleted = repo.Delete (VehicleId id)
        if deleted then
            fleetSupervisor.Tell(RemoveVehicle (VehicleId id))
            return Results.NoContent()
        else
            return Results.NotFound {| error = $"Vehicle {id} not found" |}
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // POST /api/vehicles/{id}/telemetry
    app.MapPost("/api/vehicles/{id:guid}/telemetry", Func<Guid, {| speedKmh: float; fuelPct: float; engineTemp: float; odometerKm: float; diagCodes: string list |}, Task<IResult>> (fun (id) (ev) -> task {
        try
            do! repo.UpdateFuel(VehicleId id, ev.fuelPct)
            // Also send to VehicleActor to trigger fuel alert checks
            fleetSupervisor.Tell(UpdateVehicleFuel(VehicleId id, ev.fuelPct))
            return Results.Accepted()
        with ex ->
            return Results.Problem($"Failed to update telemetry: {ex.Message}")
    }))
    |> fun e -> e.WithTags(tag) |> ignore
