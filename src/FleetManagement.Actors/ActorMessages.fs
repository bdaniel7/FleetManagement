module FleetManagement.Actors.ActorMessages

open System
open FleetManagement.Core
open FleetManagement.Core.Domain

// ============================================================
//  Vehicle Actor Messages
// ============================================================

[<Interface>]
type IVehicleMessage = interface end

type VehicleMessage =
    | GetVehicleState
    | UpdateLocation    of GeoCoordinate * speedKmh: float
    | ChangeStatus      of VehicleStatus
    | UpdateFuel        of fuelPct: float
    | AssignDriver      of DriverId
    | UnassignDriver
    | UpdateTelemetry   of VehicleTelemetry
    | StartRoute        of RouteId
    | CompleteRoute
    | Shutdown
    interface IVehicleMessage

type VehicleStateSnapshot = {
    Vehicle      : Vehicle
    ActiveRouteId: RouteId option
}

// ============================================================
//  Route Calculator Messages
// ============================================================

type RouteRequest = {
    RequestId   : Guid
    VehicleId   : VehicleId
    DriverId    : DriverId option
    Coordinates : GeoCoordinate list
    Algorithm   : PathfindingAlgorithm
    Priority    : Priority
    ReplyTo     : Akka.Actor.IActorRef
}

type RouteResponse =
    | RouteComputed  of RouteId * Route
    | RouteError     of Guid * string

type RouteCalculatorMessage =
    | ComputeRoute      of RouteRequest
    | UpdateRoadGraph   of Graph.RoadGraph
    | GetGraphStats

// ============================================================
//  Telemetry Stream Messages
// ============================================================

type TelemetryEvent = {
    VehicleId   : VehicleId
    Timestamp   : DateTimeOffset
    Location    : GeoCoordinate
    SpeedKmh    : float
    FuelPct     : float
    EngineTemp  : float
    OdometerKm  : float
    DiagCodes   : string list
}

type TelemetryStreamMessage =
    | IngestTelemetry   of TelemetryEvent
    | GetTelemetryStats of VehicleId
    | PurgeTelemetry    of VehicleId * olderThan: DateTimeOffset
    | StartStream
    | StopStream

type TelemetryStats = {
    VehicleId    : VehicleId
    EventCount   : int
    AvgSpeedKmh  : float
    AvgFuelPct   : float
    LastReceived : DateTimeOffset option
    Alerts       : string list
}

// ============================================================
//  Fleet Supervisor Messages
// ============================================================

type FleetSupervisorMessage =
    | RegisterVehicle       of Vehicle
    | RemoveVehicle         of VehicleId
    | GetFleetSummary
    | GetVehicle            of VehicleId * Akka.Actor.IActorRef
    | BroadcastToFleet      of VehicleMessage
    | RaiseFleetAlert       of string * Priority * VehicleId option
    | ScheduleHealthCheck
    | UpdateVehicleFuel     of VehicleId * float

type FleetAlert = {
    AlertId    : Guid
    Message    : string
    Priority   : Priority
    VehicleId  : VehicleId option
    RaisedAt   : DateTimeOffset
    Acknowledged: bool
}
