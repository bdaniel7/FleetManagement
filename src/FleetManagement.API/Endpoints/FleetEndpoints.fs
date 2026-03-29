module FleetManagement.API.Endpoints.FleetEndpoints

open System
open System.Threading.Tasks
open FleetManagement.Actors.ActorMessages
open FleetManagement.Core.Domain
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Akka.Actor

let mapFleetEndpoints (app: IEndpointRouteBuilder)
                      (fleetSupervisor: IActorRef)
                      (routeCalculator : IActorRef) =

    let tag     = "Fleet"
    let timeout = TimeSpan.FromSeconds 10.0

    // GET /api/fleet/summary
    app.MapGet("/api/fleet/summary", Func<Task<IResult>>(fun () -> task {
        try
            let! summary = fleetSupervisor.Ask<FleetSummary>(GetFleetSummary, timeout)
            return Results.Ok summary
        with :? TimeoutException ->
            return Results.StatusCode 504
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/fleet/graph/stats
    app.MapGet("/api/fleet/graph/stats", Func<Task<IResult>>(fun () -> task {
        try
            let! stats = routeCalculator.Ask<obj>(GetGraphStats, timeout)
            return Results.Ok stats
        with :? TimeoutException ->
            return Results.StatusCode 504
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // POST /api/fleet/alert
    app.MapPost("/api/fleet/alert", Func<{| message: string; priority: string; vehicleId: Guid Nullable |}, Task<IResult>>(
        fun (body) -> task {
        let priority =
            match body.priority with
            | "Low"       -> Priority.Low
            | "High"      -> Priority.High
            | "Emergency" -> Priority.Emergency
            | _           -> Priority.Normal
        let vehicleId =
            if body.vehicleId.HasValue then Some (VehicleId body.vehicleId.Value)
            else None
        fleetSupervisor.Tell(RaiseFleetAlert(body.message, priority, vehicleId))
        return Results.Accepted()
    }))
    |> fun e -> e.WithTags(tag) |> ignore

    // GET /api/fleet/health
    app.MapGet("/api/fleet/health", Func<IResult>(fun () ->
        Results.Ok {| status = "healthy"; timestamp = DateTimeOffset.UtcNow; version = "1.0.0" |}
    ))
    |> fun e -> e.WithTags("Health") |> ignore
