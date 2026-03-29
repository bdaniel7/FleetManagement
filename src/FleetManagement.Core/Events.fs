module FleetManagement.Core.Events

open System
open Domain

// ============================================================
//  Domain Event envelope
// ============================================================

[<Struct>]
type EventId = EventId of Guid

type DomainEvent = {
    EventId       : EventId
    OccurredAt    : DateTimeOffset
    CorrelationId : Guid
    Payload       : EventPayload
}

and EventPayload =
    // Vehicle events
    | VehicleRegistered       of VehicleId * string * VehicleType
    | VehicleLocationUpdated  of VehicleId * GeoCoordinate * float // location, speed
    | VehicleStatusChanged    of VehicleId * VehicleStatus * VehicleStatus
    | VehicleFuelUpdated      of VehicleId * float
    | VehicleDriverAssigned   of VehicleId * DriverId
    | VehicleDriverUnassigned of VehicleId
    | VehicleMaintenanceAlert of VehicleId * string

    // Route events
    | RouteCreated     of RouteId * VehicleId * PathfindingAlgorithm
    | RouteActivated   of RouteId * DateTimeOffset
    | RouteCompleted   of RouteId * DateTimeOffset * float     // actual duration minutes
    | RouteCancelled   of RouteId * string                     // reason
    | RouteRerouted    of RouteId * PathfindingAlgorithm

    // Fleet events
    | FleetAlertRaised of string * Priority * VehicleId option

// ============================================================
//  Event factory helpers
// ============================================================

let private newEvent correlationId payload = {
    EventId       = EventId (Guid.NewGuid())
    OccurredAt    = DateTimeOffset.UtcNow
    CorrelationId = correlationId
    Payload       = payload
}

let vehicleRegistered cid vid plate vtype   = newEvent cid (VehicleRegistered (vid, plate, vtype))
let vehicleLocationUpdated cid vid loc spd  = newEvent cid (VehicleLocationUpdated (vid, loc, spd))
let vehicleStatusChanged cid vid old newS   = newEvent cid (VehicleStatusChanged (vid, old, newS))
let vehicleFuelUpdated cid vid level        = newEvent cid (VehicleFuelUpdated (vid, level))
let vehicleDriverAssigned cid vid driverId     = newEvent cid (VehicleDriverAssigned (vid, driverId))
let vehicleDriverUnassigned cid vid            = newEvent cid (VehicleDriverUnassigned vid)
let routeCreated cid rid vid algo           = newEvent cid (RouteCreated (rid, vid, algo))
let routeActivated cid rid                  = newEvent cid (RouteActivated (rid, DateTimeOffset.UtcNow))
let routeCompleted cid rid dur              = newEvent cid (RouteCompleted (rid, DateTimeOffset.UtcNow, dur))
let routeCancelled cid rid reason           = newEvent cid (RouteCancelled (rid, reason))
let fleetAlert cid msg priority vehicleId  = newEvent cid (FleetAlertRaised (msg, priority, vehicleId))
