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

let vehicleActor
    (initialVehicle : Vehicle)
    (publishEvent   : DomainEvent -> unit)
    (mailbox        : Actor<VehicleMessage>) =

    let correlationId = Guid.NewGuid()
    let log           = mailbox.Context.GetLogger()

    let rec loop (state: VehicleActorState) = actor {
        let! msg = mailbox.Receive()

        match msg with
        | GetVehicleState ->
            mailbox.Sender().Tell({ Vehicle = state.Vehicle; ActiveRouteId = state.ActiveRouteId })
            return! loop state

        | UpdateLocation (loc, spd) ->
            log.Debug("Vehicle {VehicleId} location updated to {Lat},{Lon} at {Speed} km/h",
                      state.Vehicle.Id, loc.Latitude, loc.Longitude, spd)
            let ev    = vehicleLocationUpdated correlationId state.Vehicle.Id loc spd
            publishEvent ev
            let next  = state |> VehicleActorState.applyLocation loc spd |> VehicleActorState.addEvent ev
            return! loop next

        | ChangeStatus newStatus ->
            let oldStatus = state.Vehicle.Status
            if oldStatus <> newStatus then
                log.Info("Vehicle {VehicleId} status: {Old} → {New}", state.Vehicle.Id, oldStatus, newStatus)
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
            // Low fuel warning
            if pct < 15.0 then
                log.Warning("Vehicle {VehicleId} fuel low: {Pct}%%", state.Vehicle.Id, pct)
                mailbox.Context.Parent.Tell(RaiseFleetAlert(
                    $"Low fuel: {state.Vehicle.LicensePlate} at {pct:F1}%%",
                    Priority.High, Some state.Vehicle.Id))
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
                log.Warning("Vehicle {VehicleId} diagnostic codes: {Codes}", state.Vehicle.Id, codes)
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
            log.Info("Vehicle actor {VehicleId} shutting down", state.Vehicle.Id)
            mailbox.Context.Stop(mailbox.Self)
            return! loop state
    }

    loop (VehicleActorState.init initialVehicle)

// ============================================================
//  Actor factory
// ============================================================

let spawn (system: ActorSystem) (vehicle: Vehicle) (publishEvent: DomainEvent -> unit) : IActorRef =
    let (VehicleId vid) = vehicle.Id
    let name = $"vehicle-{vid:N}"
    spawnOpt system name
        (vehicleActor vehicle publishEvent)
        [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun _ -> Directive.Restart)) ]
