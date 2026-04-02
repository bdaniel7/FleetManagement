module FleetManagement.API.Endpoints.AlertsEndpoints

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open FleetManagement.Core.Domain
open FleetManagement.Infrastructure.IRepositories

let mapAlertsEndpoints (app: IEndpointRouteBuilder) (repo: IAlertsRepository) =
    let tag = "Alerts"

    app.MapGet("/api/alerts", Func<Task<IResult>>(fun () -> task {
        try
            let! dbAlerts = repo.GetAll()
            let response = dbAlerts |> List.map (fun a -> {|
                id        = match a.Id with | AlertId id -> string id
                vehicleId = a.VehicleId |> Option.map (fun (VehicleId vid) -> string vid)
                message   = a.Message
                issuedAt  = a.IssuedAt.ToString("o")
            |})
            return Results.Ok response
        with ex ->
            return Results.Problem($"Failed to get alerts: {ex.Message}")
    }))
    |> fun e -> e.WithTags(tag) |> ignore
