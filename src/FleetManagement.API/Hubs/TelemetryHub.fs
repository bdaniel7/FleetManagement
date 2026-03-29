module FleetManagement.API.Hubs.TelemetryHub

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.SignalR
open Microsoft.Extensions.Logging
open FleetManagement.Core.Domain
open FleetManagement.Actors.ActorMessages

// ============================================================
//  Hub connection groups
// ============================================================

[<Literal>]
let AllVehiclesGroup = "all-vehicles"

let vehicleGroup (VehicleId vid) = $"vehicle-{vid:N}"

// ============================================================
//  Client method names (must match Svelte client)
// ============================================================

[<Literal>]
let OnTelemetry     = "OnTelemetry"
[<Literal>]
let OnFleetSummary  = "OnFleetSummary"
[<Literal>]
let OnRouteUpdate   = "OnRouteUpdate"
[<Literal>]
let OnAlert         = "OnAlert"

// ============================================================
//  SignalR Hub
// ============================================================

type TelemetryHub(log: ILogger<TelemetryHub>) =
    inherit Hub()

    override this.OnConnectedAsync() =
        log.LogInformation("Client connected: {ConnectionId}", this.Context.ConnectionId)
        task {
            do! this.Groups.AddToGroupAsync(this.Context.ConnectionId, AllVehiclesGroup)
        } :> Task

    override this.OnDisconnectedAsync(ex) =
        log.LogInformation("Client disconnected: {ConnectionId}", this.Context.ConnectionId)
        base.OnDisconnectedAsync(ex)

    /// Client calls this to subscribe to a specific vehicle
    member this.SubscribeVehicle(vehicleId: string) =
        let group = $"vehicle-{vehicleId}"
        log.LogDebug("Client {ConnectionId} subscribing to vehicle {VehicleId}",
                     this.Context.ConnectionId, vehicleId)
        this.Groups.AddToGroupAsync(this.Context.ConnectionId, group)

    /// Client calls this to unsubscribe from a vehicle
    member this.UnsubscribeVehicle(vehicleId: string) =
        let group = $"vehicle-{vehicleId}"
        this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, group)

// ============================================================
//  Hub broadcaster service (injected into actors/endpoints)
// ============================================================

type IFleetHubBroadcaster =
    abstract BroadcastTelemetry : TelemetryEvent -> Task
    abstract BroadcastAlert     : string * Priority -> Task
    abstract BroadcastFleetSummary : FleetSummary -> Task
    abstract BroadcastRouteUpdate  : Route -> Task

type FleetHubBroadcaster(hub: IHubContext<TelemetryHub>) =

    interface IFleetHubBroadcaster with

        member _.BroadcastTelemetry (ev: TelemetryEvent) =
            let (VehicleId vid) = ev.VehicleId
            let payload = {|
                vehicleId  = vid
                timestamp  = ev.Timestamp
                lat        = ev.Location.Latitude
                lon        = ev.Location.Longitude
                speedKmh   = ev.SpeedKmh
                fuelPct    = ev.FuelPct
                engineTemp = ev.EngineTemp
                odometerKm = ev.OdometerKm
                diagCodes  = ev.DiagCodes
            |}
            // Send to vehicle-specific group AND to all-subscribers
            task {
                do! hub.Clients.Group(vehicleGroup ev.VehicleId).SendAsync(OnTelemetry, payload)
                do! hub.Clients.Group(AllVehiclesGroup).SendAsync(OnTelemetry, payload)
            }

        member _.BroadcastAlert (message, priority) =
            hub.Clients.All.SendAsync(OnAlert, {| message = message; priority = string priority; timestamp = DateTimeOffset.UtcNow |})

        member _.BroadcastFleetSummary (summary: FleetSummary) =
            hub.Clients.All.SendAsync(OnFleetSummary, summary)

        member _.BroadcastRouteUpdate (route: Route) =
            let (RouteId rid)   = route.Id
            let (VehicleId vid) = route.VehicleId
            hub.Clients.All.SendAsync(OnRouteUpdate, {|
                routeId    = rid
                vehicleId  = vid
                status     = string route.Status
                algorithm  = string route.Algorithm
                distanceKm = route.TotalDistanceKm
                durationMin= route.EstimatedDurationMin
                priority   = string route.Priority
            |})
