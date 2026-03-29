module FleetManagement.API.Middleware.RateLimitMiddleware

open System
open System.Threading
open System.Threading.RateLimiting
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.RateLimiting
open Microsoft.Extensions.DependencyInjection

// ============================================================
//  Named rate limit policies
// ============================================================

[<Literal>]
let GlobalPolicy   = "global"
[<Literal>]
let TelemetryPolicy= "telemetry"
[<Literal>]
let RoutePolicy    = "route"

let configure (services: IServiceCollection) =
    services.AddRateLimiter(fun opts ->
        // Global: 200 req/min per IP
        opts.AddFixedWindowLimiter(GlobalPolicy, fun o ->
            o.PermitLimit         <- 200
            o.Window              <- TimeSpan.FromMinutes 1.0
            o.QueueProcessingOrder<- QueueProcessingOrder.OldestFirst
            o.QueueLimit          <- 50) |> ignore

        // Telemetry ingest: 500 req/min (high-frequency sensors)
        opts.AddFixedWindowLimiter(TelemetryPolicy, fun o ->
            o.PermitLimit         <- 500
            o.Window              <- TimeSpan.FromMinutes 1.0
            o.QueueProcessingOrder<- QueueProcessingOrder.OldestFirst
            o.QueueLimit          <- 100) |> ignore

        // Route computation: 30 req/min (expensive pathfinding)
        opts.AddFixedWindowLimiter(RoutePolicy, fun o ->
            o.PermitLimit         <- 30
            o.Window              <- TimeSpan.FromMinutes 1.0
            o.QueueProcessingOrder<- QueueProcessingOrder.OldestFirst
            o.QueueLimit          <- 10) |> ignore

        opts.RejectionStatusCode <- StatusCodes.Status429TooManyRequests

        opts.OnRejected <- Func<OnRejectedContext, CancellationToken, ValueTask>(fun ctx ct ->
           let t= task {
                // await async operations; honor ct if appropriate
                ctx.HttpContext.Response.Headers.["Retry-After"] <- "60"
           }
           ValueTask t
        )
    ) |> ignore

    services