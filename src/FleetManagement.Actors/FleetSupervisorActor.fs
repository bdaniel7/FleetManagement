module FleetManagement.Actors.FleetSupervisorActor

open System
open System.Collections.Generic
open Akka.Event
open Akka.FSharp
open Akka.Actor
open FleetManagement.Core.Domain
open FleetManagement.Core.Events
open ActorMessages

// ============================================================
//  State
// ============================================================

type private SupervisorState = {
    VehicleActors : Dictionary<VehicleId, IActorRef>
    Alerts        : ResizeArray<FleetAlert>
    AlertCount    : int
}

// ============================================================
//  Fleet Supervisor
// ============================================================

let fleetSupervisorActor
    (routeCalculator  : IActorRef)
    (telemetryActor   : IActorRef)
    (publishEvent     : DomainEvent -> unit)
    (mailbox          : Actor<FleetSupervisorMessage>) =

    let log = mailbox.Context.GetLogger()

    // --------------------------------------------------------
    // Supervision strategy: restart crashed vehicle actors
    // --------------------------------------------------------
    mailbox.Context.System.Settings.Config |> ignore

    let strategy =
        Strategy.OneForOne(fun ex ->
            log.Error(ex, "Vehicle actor failure, restarting")
            Directive.Restart)

    let rec loop (state: SupervisorState) = actor {
        let! msg = mailbox.Receive()

        match msg with
        // -------------------------------------------------------
        | RegisterVehicle vehicle ->
            if state.VehicleActors.ContainsKey vehicle.Id then
                log.Warning("Vehicle {Id} already registered", vehicle.Id)
                return! loop state
            else
                let actorRef =
                    VehicleActor.spawn mailbox.Context.System vehicle publishEvent
                state.VehicleActors.[vehicle.Id] <- actorRef
                log.Info("Vehicle {Plate} ({Id}) registered", vehicle.LicensePlate, vehicle.Id)
                let ev = vehicleRegistered (Guid.NewGuid()) vehicle.Id vehicle.LicensePlate vehicle.VehicleType
                publishEvent ev
                return! loop state

        // -------------------------------------------------------
        | RemoveVehicle vehicleId ->
            match state.VehicleActors.TryGetValue vehicleId with
            | true, ref ->
                ref.Tell(Shutdown)
                state.VehicleActors.Remove vehicleId |> ignore
                log.Info("Vehicle {Id} removed from fleet", vehicleId)
            | _ ->
                log.Warning("RemoveVehicle: {Id} not found", vehicleId)
            return! loop state

        // -------------------------------------------------------
        | GetFleetSummary ->
            // Ask all vehicle actors for their state concurrently
            let snapshots =
                state.VehicleActors.Values
                |> Seq.map (fun actorRef ->
                        actorRef.Ask<VehicleStateSnapshot>(GetVehicleState, TimeSpan.FromSeconds 3.0)
                    )
                |> Async.Parallel
                |> Async.RunSynchronously

            let vehicles = snapshots |> Array.map (fun s -> s.Vehicle)

            let summary = {
                TotalVehicles    = vehicles.Length
                ActiveVehicles   = vehicles |> Array.filter (fun v -> v.Status = VehicleStatus.EnRoute) |> Array.length
                IdleVehicles     = vehicles |> Array.filter (fun v -> v.Status = VehicleStatus.Idle) |> Array.length
                MaintenanceCount = vehicles |> Array.filter (fun v -> v.Status = VehicleStatus.Maintenance) |> Array.length
                TotalRoutes      = 0    // populated by query layer
                ActiveRoutes     = snapshots |> Array.filter (fun s -> s.ActiveRouteId.IsSome) |> Array.length
                AvgFuelLevel     = if vehicles.Length > 0 then vehicles |> Array.averageBy (fun v -> v.FuelLevelPct) else 0.0
                LastUpdated      = DateTimeOffset.UtcNow
            }
            mailbox.Sender().Tell(summary)
            return! loop state

        // -------------------------------------------------------
        | GetVehicle (vehicleId, replyTo) ->
            match state.VehicleActors.TryGetValue vehicleId with
            | true, ref ->
                async {
                    let! snapshot = ref.Ask<VehicleStateSnapshot>(GetVehicleState, TimeSpan.FromSeconds 3.0)
                    replyTo.Tell(Some snapshot)
                } |> Async.Start
            | _ ->
                replyTo.Tell(None)
            return! loop state

        // -------------------------------------------------------
        | BroadcastToFleet msg ->
            state.VehicleActors.Values |> Seq.iter (fun ref -> ref.Tell(msg))
            return! loop state

        // -------------------------------------------------------
        | RaiseFleetAlert (message, priority, vehicleId) ->
            let alert = {
                AlertId      = Guid.NewGuid()
                Message      = message
                Priority     = priority
                VehicleId    = vehicleId
                RaisedAt     = DateTimeOffset.UtcNow
                Acknowledged = false
            }
            state.Alerts.Add alert
            // Keep only last 1000 alerts
            if state.Alerts.Count > 1000 then
                state.Alerts.RemoveAt 0
            let ev = fleetAlert (Guid.NewGuid()) message priority vehicleId
            publishEvent ev
            log.Warning("Fleet alert [{Priority}]: {Message}", priority, message)
            return! loop { state with AlertCount = state.AlertCount + 1 }

        // -------------------------------------------------------
        | ScheduleHealthCheck ->
            log.Debug("Fleet health check: {Count} vehicles tracked", state.VehicleActors.Count)
            // Ping all actors; crashed ones trigger supervision restart
            state.VehicleActors.Values |> Seq.iter (fun ref -> ref.Tell(GetVehicleState))
            return! loop state
    }

    // Schedule periodic health check every 30 seconds
    mailbox.Context.System.Scheduler.ScheduleTellRepeatedly(
        TimeSpan.FromSeconds 30.0,
        TimeSpan.FromSeconds 30.0,
        mailbox.Self,
        ScheduleHealthCheck,
        mailbox.Self)

    loop {
        VehicleActors = Dictionary()
        Alerts        = ResizeArray()
        AlertCount    = 0
    }

// ============================================================
//  Factory
// ============================================================

let spawn
    (system         : ActorSystem)
    (routeCalculator: IActorRef)
    (telemetryActor : IActorRef)
    (publishEvent   : DomainEvent -> unit) : IActorRef =
    spawn system "fleet-supervisor"
        (fleetSupervisorActor routeCalculator telemetryActor publishEvent)

    // system.ActorOf(
    //     Props.Create(fleetSupervisorActor routeCalculator telemetryActor publishEvent),
    //     "fleet-supervisor")
