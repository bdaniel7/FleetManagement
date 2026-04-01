module FleetManagement.API.Endpoints.TripEndpoints

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open FleetManagement.Core.Domain
open FleetManagement.Infrastructure.IRepositories
open System.Text.Json.Serialization
open FleetManagement.Core.Tracing
open FleetManagement.API.Tracing

// ── DTOs ──────────────────────────────────────────────────────

[<CLIMutable>]
type TripWaypointDto = {
    [<JsonPropertyName("order")>]         Order    : int
    [<JsonPropertyName("label")>]         Label    : string
    [<JsonPropertyName("coordinate")>]    Coordinate    : GeoCoordinate
    [<JsonPropertyName("notes")>]         Notes    : string
    [<JsonPropertyName("dwellMin")>]      DwellMin : int
}

[<CLIMutable>]
type CreateTripRequest = {
    [<JsonPropertyName("name")>]        Name        : string
    [<JsonPropertyName("description")>] Description : string
    [<JsonPropertyName("vehicleId")>]   VehicleId   : Guid Nullable
    [<JsonPropertyName("driverId")>]    DriverId    : Guid Nullable
    [<JsonPropertyName("isCircular")>]  IsCircular  : bool
    [<JsonPropertyName("waypoints")>]   Waypoints   : TripWaypointDto[]
}

[<CLIMutable>]
type UpdateTripRequest = {
    [<JsonPropertyName("name")>]        Name        : string
    [<JsonPropertyName("description")>] Description : string
    [<JsonPropertyName("vehicleId")>]   VehicleId   : Guid Nullable
    [<JsonPropertyName("driverId")>]    DriverId    : Guid Nullable
    [<JsonPropertyName("isCircular")>]  IsCircular  : bool
    [<JsonPropertyName("waypoints")>]   Waypoints   : TripWaypointDto[]
}

// ── Helpers ───────────────────────────────────────────────────

let private toWaypoint (d: TripWaypointDto) : TripWaypoint = {
    Order      = d.Order
    Label      = if String.IsNullOrWhiteSpace d.Label then $"Stop {d.Order + 1}" else d.Label
    Coordinate = { Latitude = d.Coordinate.Latitude; Longitude = d.Coordinate.Longitude }
    Notes      = d.Notes
    DwellMin   = d.DwellMin
}

let private validateWaypoints (dtos: TripWaypointDto[]) =
    if dtos.Length < 2 then
        Error "A trip requires at least 2 waypoints"
    elif dtos |> Array.exists (fun w -> w.Coordinate.Latitude < -90.0 || w.Coordinate.Latitude > 90.0
                                                        || w.Coordinate.Longitude < -180.0 || w.Coordinate.Longitude > 180.0) then
        Error "One or more waypoints have invalid coordinates"
    else
        Ok (dtos |> Array.mapi (fun i w -> toWaypoint { w with Order = i }) |> Array.toList)

// ── Endpoint mapping ──────────────────────────────────────────

let mapTripsEndpoints (app: IEndpointRouteBuilder) (repo: ITripsRepository) =

    let tag = "Trips"

    // GET /api/trips
    app.MapGet("/api/trips", Func<Task<IResult>>(fun () -> task {
        let! trips = repo.GetAll()

        startActivity "GetAllTrips"
            |> setTagInt "trip.count" trips.Length
            |> dispose

        return Results.Ok(trips)
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/trips/{id}
    app.MapGet("/api/trips/{id:guid}", Func<Guid, Task<IResult>>(fun id -> task {
        let activity = startActivity "GetOneTrip"
        activity |> setTagGuid "trip.id" id |> ignore
        //setTag activity "trace.id" (getTraceId activity)
        let! trip = repo.GetById (TripId id)
        return
            match trip with
            | Some t ->
                activity |> setTagGuid "trip.id" id |> dispose
                Results.Ok t

            | None   ->
                activity |> setError $"Trip {id} not found"
                activity |> dispose
                Results.NotFound {| error = $"Trip {id} not found" |}
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/trips/vehicle/{vehicleId}
    app.MapGet("/api/trips/vehicle/{vehicleId:guid}", Func<Guid, Task<IResult>>(fun vehicleId -> task {
        let activity = startActivity "GetTripsPerVehicle"
        activity |> setTagGuid "trip.vehicleId" vehicleId |> ignore

        let! trips = repo.GetByVehicle (VehicleId vehicleId)

        activity |> setTagInt "trip.Count" trips.Length |> dispose

        return Results.Ok trips
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/trips/status/{status}
    app.MapGet("/api/trips/status/{status}", Func<string, Task<IResult>>(fun status -> task {
        let parsed =
            match status with
            | "Draft"       -> Some TripStatus.Draft
            | "Scheduled"   -> Some TripStatus.Scheduled
            | "InProgress"  -> Some TripStatus.InProgress
            | "Completed"   -> Some TripStatus.TripCompleted
            | "Cancelled"   -> Some TripStatus.TripCancelled
            | _             -> None
        match parsed with
        | None   -> return Results.BadRequest {| error = $"Unknown trip status: {status}" |}
        | Some s ->
            let! trips = repo.GetByStatus s
            return Results.Ok trips
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // POST /api/trips
    app.MapPost("/api/trips", Func<CreateTripRequest, Task<IResult>>(fun req -> task {
        let activity = startActivity "CreateTrip"

        if String.IsNullOrWhiteSpace req.Name then
            setError "Trip name is required" activity
            return Results.BadRequest {| error = "Trip name is required" |}
        else
            activity |> setTag  "trip.name" req.Name
                     |> setTagInt "trip.waypoints_count" req.Waypoints.Length
                     |> ignore

            match validateWaypoints req.Waypoints with
            | Error msg ->
                setError msg activity
                return Results.UnprocessableEntity {| error = msg |}
            | Ok waypoints ->

            let trip : Trip = {
                Id          = TripId (Guid.NewGuid())
                Name        = req.Name.Trim()
                Description = req.Description.Trim()
                VehicleId   = if req.VehicleId.HasValue then Some (VehicleId req.VehicleId.Value) else None
                DriverId    = if req.DriverId.HasValue  then Some (DriverId  req.DriverId.Value)  else None
                Status      = TripStatus.Draft
                IsCircular  = req.IsCircular
                Waypoints   = waypoints
                TotalDistanceKm = 0.0   // computed by repository on upsert
                CreatedAt   = DateTimeOffset.UtcNow
                UpdatedAt   = DateTimeOffset.UtcNow
                StartedAt   = None
                CompletedAt = None
            }

            do! repo.Upsert trip
            let (TripId tid) = trip.Id
            setTag "trip.id" (string tid) activity |> dispose
            return Results.Created($"/api/trips/{tid}", trip)
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // PUT /api/trips/{id}
    app.MapPut("/api/trips/{id:guid}", Func<Guid, UpdateTripRequest, Task<IResult>>(fun id req -> task {

        let activity = startActivity "UpdateTrip"

        let! existing = repo.GetById (TripId id)
        match existing with
        | None ->
            setError $"Trip {id} not found" activity
            return Results.NotFound {| error = $"Trip {id} not found" |}
        | Some trip ->

        match trip.Status with
        | TripStatus.InProgress | TripStatus.TripCompleted | TripStatus.TripCancelled ->
            setError $"Cannot edit a trip with status {trip.Status}" activity
            return Results.Conflict {| error = $"Cannot edit a trip with status {trip.Status}" |}
        | _ ->

        match validateWaypoints req.Waypoints with
        | Error msg ->
            setError msg activity
            return Results.UnprocessableEntity {| error = msg |}
        | Ok waypoints ->

        let updated = { trip with
                            Name        = if String.IsNullOrWhiteSpace req.Name then trip.Name else req.Name.Trim()
                            Description = if req.Description = null then trip.Description else req.Description.Trim()
                            VehicleId   = if req.VehicleId.HasValue then Some (VehicleId req.VehicleId.Value) else None
                            DriverId    = if req.DriverId.HasValue  then Some (DriverId  req.DriverId.Value)  else None
                            IsCircular  = req.IsCircular
                            Waypoints   = waypoints
                            UpdatedAt   = DateTimeOffset.UtcNow }
        do! repo.Upsert updated

        setTag "trip.saved" "Ok" activity |> dispose

        return Results.Ok updated
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // POST /api/trips/{id}/start
    app.MapPost("/api/trips/{id:guid}/start", Func<Guid, Task<IResult>>(fun id -> task {
        let! trip = repo.GetById (TripId id)
        match trip with
        | None -> return Results.NotFound {| error = $"Trip {id} not found" |}
        | Some t when t.Status <> TripStatus.Draft && t.Status <> TripStatus.Scheduled ->
            return Results.Conflict {| error = $"Trip cannot be started from status {t.Status}" |}
        | _ ->
            do! repo.UpdateStatus(TripId id, TripStatus.InProgress)
            return Results.NoContent()
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // POST /api/trips/{id}/complete
    app.MapPost("/api/trips/{id:guid}/complete", Func<Guid, Task<IResult>>(fun id -> task {
        do! repo.UpdateStatus(TripId id, TripStatus.TripCompleted)
        return Results.NoContent()
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // POST /api/trips/{id}/cancel
    app.MapPost("/api/trips/{id:guid}/cancel", Func<Guid, Task<IResult>>(fun id -> task {
        do! repo.UpdateStatus(TripId id, TripStatus.TripCancelled)
        return Results.NoContent()
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // DELETE /api/trips/{id}
    app.MapDelete("/api/trips/{id:guid}", Func<Guid, Task<IResult>>(fun id -> task {
        let! deleted = repo.Delete (TripId id)
        if deleted then return Results.NoContent()
        else return Results.NotFound {| error = $"Trip {id} not found" |}
    }))
    |> fun e -> e.WithTags(tag) |> ignore
