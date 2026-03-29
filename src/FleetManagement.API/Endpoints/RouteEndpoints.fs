module FleetManagement.API.Endpoints.RouteEndpoints

open System
open System.Threading.Tasks
open FleetManagement.API.Dtos
open FleetManagement.Actors.ActorMessages
open FleetManagement.Infrastructure.IRepositories
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Akka.Actor
open FleetManagement.Core.Domain


// ============================================================
//  Endpoint mapping
// ============================================================

let mapRouteEndpoints (app: IEndpointRouteBuilder) (repo: IRouteRepository) (routeCalculator: IActorRef) =

    let tag = "Routes"
    let timeout = TimeSpan.FromSeconds 30.0

    app.MapGet("/hello", Func<string>(fun () -> "Hello world"))
    |> fun e -> e.WithTags(tag)
    |> ignore

    // GET /api/routes
    app.MapGet("/api/routes", Func<Task<IResult>>(fun () -> task {
        let! routes = repo.GetAll()
        return Results.Ok routes
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/routes/active
    app.MapGet("/api/routes/active", Func<Task<IResult>>(fun () -> task {
        let! routes = repo.GetActive()
        return Results.Ok routes
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/routes/{id}
    app.MapGet("/api/routes/{id:guid}", Func<Guid, Task<IResult>>(fun (id) -> task {
        let! route = repo.GetById (RouteId id)
        return
            match route with
            | Some r -> Results.Ok r
            | None   -> Results.NotFound {| error = $"Route {id} not found" |}
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/routes/vehicle/{vehicleId}
    app.MapGet("/api/routes/vehicle/{vehicleId:guid}", Func<Guid, Task<IResult>>(fun (vehicleId) -> task {
        let! routes = repo.GetByVehicle (VehicleId vehicleId)
        return Results.Ok routes
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    let mapWaypointToGeoCoordinate (w: WaypointDto) : GeoCoordinate = { Latitude = w.Latitude; Longitude = w.Longitude }

    // POST /api/routes/plan  — triggers pathfinding via actor
    app.MapPost("/api/routes/plan", Func<PlanRouteRequest, HttpContext, Task<IResult>>(fun (req) (ctx) -> task {
        if req.Waypoints.Length < 2 then
            return Results.BadRequest {| error = "At least 2 waypoints are required" |}
        else

        let algo =
            match req.Algorithm with
            | "Dijkstra"    -> PathfindingAlgorithm.Dijkstra
            | "BellmanFord" -> PathfindingAlgorithm.BellmanFord
            | _             -> PathfindingAlgorithm.AStar   // default

        let priority =
            match req.Priority with
            | "Low"       -> Priority.Low
            | "High"      -> Priority.High
            | "Emergency" -> Priority.Emergency
            | _           -> Priority.Normal

        let driverId =
            if req.DriverId.HasValue then Some (DriverId req.DriverId.Value)
            else None

        //let replyRef = ctx.RequestServices.GetRequiredService<IActorRef>()

        let routeReq : RouteRequest = {
            RequestId   = Guid.NewGuid()
            VehicleId   = VehicleId req.VehicleId
            DriverId    = driverId
            Coordinates = req.Waypoints |> Array.map (mapWaypointToGeoCoordinate) |> Array.toList
            Algorithm   = algo
            Priority    = priority
            ReplyTo     = ActorRefs.Nobody   // use ask pattern instead
        }

        try
            let! response = routeCalculator.Ask<RouteResponse>((ComputeRoute routeReq), timeout)
            match response with
            | RouteComputed (routeId, route) ->
                do! repo.Insert route
                return Results.Created($"/api/routes/{let (RouteId rid) = routeId in rid}", route)
            | RouteError (_, msg) ->
                return Results.UnprocessableEntity {| error = msg |}
        with :? TimeoutException ->
            return Results.StatusCode 504
    }))
    |> fun e -> e.WithTags(tag).RequireRateLimiting("route") |> ignore

    // POST /api/routes/{id}/activate
    app.MapPost("/api/routes/{id:guid}/activate", Func<Guid, Task<IResult>>(fun (id: Guid) -> task {
        do! repo.UpdateStatus(RouteId id, RouteStatus.Active)
        return Results.NoContent()
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // POST /api/routes/{id}/complete
    app.MapPost("/api/routes/{id:guid}/complete", Func<Guid, Task<IResult>>(fun (id: Guid) -> task {
        do! repo.Complete(RouteId id)
        return Results.NoContent()
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // PATCH /api/routes/{id}/waypoints — update waypoints and re-plan
    app.MapPatch("/api/routes/{id:guid}/waypoints", Func<Guid, UpdateWaypointsRequest, Task<IResult>>(fun (id) (req) -> task {
        if req.Waypoints.Length < 2 then
            return Results.BadRequest {| error = "At least 2 waypoints are required" |}
        else

        let! existing = repo.GetById (RouteId id)
        match existing with
        | None ->
            return Results.NotFound {| error = $"Route {id} not found" |}
        | Some route ->
            match route.Status with
            | RouteStatus.Completed | RouteStatus.Cancelled ->
                return Results.Conflict {| error = $"Cannot edit a {route.Status} route" |}
            | _ ->

            let algo =
                match req.Algorithm with
                | "Dijkstra"    -> PathfindingAlgorithm.Dijkstra
                | "BellmanFord" -> PathfindingAlgorithm.BellmanFord
                | _             -> PathfindingAlgorithm.AStar

            // Build Waypoint list from the DTO coordinates
            let waypoints =
                req.Waypoints
                |> Array.mapi (fun i w ->
                    { NodeId          = NodeId $"WP-{i}"
                      Coordinate      = { Latitude = w.Latitude; Longitude = w.Longitude }
                      Address         = $"Waypoint {i + 1}"
                      ArrivalTime     = None
                      DepartureTime   = None
                      StopDurationMin = 0 })
                |> Array.toList

            do! repo.UpdateWaypoints(RouteId id, waypoints, algo)

            let! updated = repo.GetById (RouteId id)
            return
                match updated with
                | Some r -> Results.Ok r
                | None   -> Results.NotFound {| error = "Route not found after update" |}
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // POST /api/routes/{id}/cancel
    app.MapPost("/api/routes/{id:guid}/cancel", Func<Guid, {| reason: string |}, Task<IResult>>(fun (id) (body) -> task {
        do! repo.UpdateStatus(RouteId id, RouteStatus.Cancelled)
        return Results.NoContent()
    }))
    |> fun e -> e.WithTags(tag) |> ignore
