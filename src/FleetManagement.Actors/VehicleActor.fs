module FleetManagement.Actors.VehicleActor

open System
open Akka.Event
open Akka.FSharp
open Akka.Actor
open FleetManagement.Core.Domain
open FleetManagement.Core.Events
open ActorMessages

// ============================================================
//  Vehicle State
// ============================================================

type private VehicleActorState = {
    Vehicle      : Vehicle
    ActiveRouteId: RouteId option
    EventHistory : DomainEvent list   // recent N events, ring-buffer style
}

module private VehicleActorState =
    let maxHistory = 100

    let init (vehicle: Vehicle) = {
        Vehicle       = vehicle
        ActiveRouteId = None
        EventHistory  = []
    }

    let addEvent ev (state: VehicleActorState) =
        let hist =
            if state.EventHistory.Length >= maxHistory then
                List.tail state.EventHistory
            else state.EventHistory
        { state with EventHistory = hist @ [ev] }

    let applyLocation (loc: GeoCoordinate) (spd: float) (state: VehicleActorState) =
        let updated = {
            state.Vehicle with
                CurrentLocation = loc
                SpeedKmh        = spd
                UpdatedAt       = DateTimeOffset.UtcNow
        }
        { state with Vehicle = updated }

    let applyStatus (s: VehicleStatus) (state: VehicleActorState) =
        let updated = { state.Vehicle with Status = s; UpdatedAt = DateTimeOffset.UtcNow }
        { state with Vehicle = updated }

    let applyFuel (pct: float) (state: VehicleActorState) =
        let clamped = Math.Clamp(pct, 0.0, 100.0)
        let updated = { state.Vehicle with FuelLevelPct = clamped; UpdatedAt = DateTimeOffset.UtcNow }
        { state with Vehicle = updated }

    let applyTelemetry (t: VehicleTelemetry) (state: VehicleActorState) =
        let updated = { state.Vehicle with Telemetry = t; UpdatedAt = DateTimeOffset.UtcNow }
        { state with Vehicle = updated }

// ============================================================
//  Actor Definition  (Akka.FSharp functional style)
// ============================================================

let private isElectricVehicle (v: Vehicle) =
    match v.VehicleType with
    | VehicleType.ElectricTruck -> true
    | VehicleType.ElectricVan -> true
    | _ -> false

let vehicleActor
    (initialVehicle : Vehicle)
    (lowFuelThreshold : float)
    (publishEvent   : DomainEvent -> unit)
    (supervisor     : IActorRef)
    (mailbox        : Actor<VehicleMessage>) =

    let correlationId = Guid.NewGuid()
    let log           = mailbox.Context.GetLogger()
    let isElectric    = isElectricVehicle initialVehicle

    // Extract inner strings from [<Struct>] DUs — never pass them directly
    // to the logger or string interpolation; .NET 10 crashes on reflection.
    let inline vidStr (VehicleId g) = string g
    let inline ridStr (RouteId  g) = string g
    let inline statusStr (s: VehicleStatus) = sprintf "%A" s   // safe: plain string DU, not struct

    let rec loop (state: VehicleActorState) = actor {
        let! msg = mailbox.Receive()

        match msg with
        | GetVehicleState ->
            mailbox.Sender().Tell({ Vehicle = state.Vehicle; ActiveRouteId = state.ActiveRouteId })
            return! loop state

        | UpdateLocation (loc, spd) ->
            log.Debug("Vehicle {VehicleId} location updated to {Lat},{Lon} at {Speed} km/h",
                      vidStr state.Vehicle.Id, loc.Latitude, loc.Longitude, spd)
            let ev    = vehicleLocationUpdated correlationId state.Vehicle.Id loc spd
            publishEvent ev
            let next  = state |> VehicleActorState.applyLocation loc spd |> VehicleActorState.addEvent ev
            return! loop next

        | ChangeStatus newStatus ->
            let oldStatus = state.Vehicle.Status
            if oldStatus <> newStatus then
                log.Info("Vehicle {VehicleId} status: {Old} → {New}",
                         vidStr state.Vehicle.Id, sprintf "%A" oldStatus, sprintf "%A" newStatus)
                let ev   = vehicleStatusChanged correlationId state.Vehicle.Id oldStatus newStatus
                publishEvent ev
                let next = state |> VehicleActorState.applyStatus newStatus |> VehicleActorState.addEvent ev
                return! loop next
            else
                return! loop state

        | UpdateFuel pct ->
            let ev   = vehicleFuelUpdated correlationId state.Vehicle.Id pct
            publishEvent ev
            let next = state |> VehicleActorState.applyFuel pct |> VehicleActorState.addEvent ev
            // Low battery warning (with rate limiting - only alert once until recharged)
            // For electric vehicles, use battery level; for others, use fuel level
            let currentLevel = if isElectric then state.Vehicle.Telemetry.BatteryLevel else Some pct
            let prevLevel = if isElectric then state.Vehicle.Telemetry.BatteryLevel else Some state.Vehicle.FuelLevelPct
            match currentLevel with
            | Some level when level < lowFuelThreshold ->
                // Critical if level is 0, otherwise warning
                let priority = if level <= 0.0 then Priority.Emergency else Priority.High
                let alertType = if isElectric then "battery" else "fuel"
                // Only send alert if level was previously above threshold (debounce)
                match prevLevel with
                | Some prev when prev >= lowFuelThreshold ->
                    log.Warning("Vehicle {VehicleId} {Type} LOW: {Pct}%% (threshold: {Threshold}%%)",
                                vidStr state.Vehicle.Id, alertType, level, lowFuelThreshold)
                    let levelStr = sprintf "%.1f" level
                    supervisor.Tell(RaiseFleetAlert(
                        sprintf "Low %s for %s at level %s%%" alertType state.Vehicle.LicensePlate levelStr,
                        priority, Some state.Vehicle.Id))
                | _ -> ()
            | _ -> ()
            return! loop next

        | AssignDriver driverId ->
            let ev      = vehicleDriverAssigned correlationId state.Vehicle.Id driverId
            publishEvent ev
            let updated = { state.Vehicle with AssignedDriver = Some driverId; UpdatedAt = DateTimeOffset.UtcNow }
            return! loop { state with Vehicle = updated }

        | UnassignDriver ->
            let updated = { state.Vehicle with AssignedDriver = None; UpdatedAt = DateTimeOffset.UtcNow }
            return! loop { state with Vehicle = updated }

        | UpdateTelemetry telemetry ->
            let next = state |> VehicleActorState.applyTelemetry telemetry
            // Check diagnostic codes
            if not telemetry.DiagnosticCodes.IsEmpty then
                let codes = String.concat ", " telemetry.DiagnosticCodes
                log.Warning("Vehicle {VehicleId} diagnostic codes: {Codes}", vidStr state.Vehicle.Id, codes)
                mailbox.Context.Parent.Tell(RaiseFleetAlert(
                    $"Diagnostic codes on {state.Vehicle.LicensePlate}: {codes}",
                    Priority.Normal, Some state.Vehicle.Id))
            return! loop next

        | StartRoute routeId ->
            let ev   = routeActivated correlationId routeId
            publishEvent ev
            let next = state |> VehicleActorState.applyStatus VehicleStatus.EnRoute
            return! loop { next with ActiveRouteId = Some routeId }

        | CompleteRoute ->
            match state.ActiveRouteId with
            | Some rid ->
                let ev   = routeCompleted correlationId rid 0.0
                publishEvent ev
                let next = state |> VehicleActorState.applyStatus VehicleStatus.Idle
                return! loop { next with ActiveRouteId = None }
            | None ->
                log.Warning("CompleteRoute called on vehicle with no active route")
                return! loop state

        | Shutdown ->
            log.Info("Vehicle actor {VehicleId} shutting down", vidStr state.Vehicle.Id)
            mailbox.Context.Stop(mailbox.Self)
            return! loop state
    }

    loop (VehicleActorState.init initialVehicle)

// ============================================================
//  Actor factory
// ============================================================

let spawn (system: ActorSystem) (vehicle: Vehicle) (lowFuelThreshold: float) (publishEvent: DomainEvent -> unit) (supervisor: IActorRef) : IActorRef =
    let (VehicleId vid) = vehicle.Id
    let name = $"vehicle-{vid:N}"
    spawnOpt system name
        (vehicleActor vehicle lowFuelThreshold publishEvent supervisor)
        [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun _ -> Directive.Restart)) ]
