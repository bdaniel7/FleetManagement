module FleetManagement.Infrastructure.IRepositories

open System
open FleetManagement.Core.Domain
open FleetManagement.Core.Events

// ============================================================
//  Repository interfaces — defined separately so they are
//  always resolved before any implementation or consumer.
// ============================================================

type IVehicleRepository =
    abstract GetById        : VehicleId -> Async<Vehicle option>
    abstract GetAll         : unit -> Async<Vehicle list>
    abstract GetByStatus    : VehicleStatus -> Async<Vehicle list>
    abstract Upsert         : Vehicle -> Async<unit>
    abstract Delete         : VehicleId -> Async<bool>
    abstract UpdateLocation : VehicleId * GeoCoordinate * float -> Async<unit>
    abstract UpdateStatus   : VehicleId * VehicleStatus -> Async<unit>
    abstract UpdateFuel     : VehicleId * float -> Async<unit>

type IDriverRepository =
    abstract GetById          : DriverId -> Async<Driver option>
    abstract GetAll           : unit -> Async<Driver list>
    abstract GetAvailable     : unit -> Async<Driver list>
    abstract Upsert           : Driver -> Async<unit>
    abstract SetAvailability  : DriverId * bool -> Async<unit>
    abstract LogHours         : DriverId * float -> Async<unit>

type IRouteRepository =
    abstract GetById       : RouteId -> Async<Route option>
    abstract GetByVehicle  : VehicleId -> Async<Route list>
    abstract GetActive     : unit -> Async<Route list>
    abstract GetAll        : unit -> Async<Route list>
    abstract Insert        : Route -> Async<unit>
    abstract UpdateStatus  : RouteId * RouteStatus -> Async<unit>
    abstract UpdateWaypoints : RouteId * Waypoint list * PathfindingAlgorithm -> Async<unit>
    abstract Complete      : RouteId -> Async<unit>

type IEventRepository =
    abstract Append            : DomainEvent -> Async<unit>
    abstract GetByCorrelation  : Guid -> Async<DomainEvent list>
    abstract GetRecent         : int -> Async<DomainEvent list>

type ITripsRepository =
    abstract GetAll        : unit -> Async<Trip list>
    abstract GetById       : TripId -> Async<Trip option>
    abstract GetByVehicle  : VehicleId -> Async<Trip list>
    abstract GetByStatus   : TripStatus -> Async<Trip list>
    abstract Upsert        : Trip -> Async<unit>
    abstract UpdateStatus  : TripId * TripStatus -> Async<unit>
    abstract Delete        : TripId -> Async<bool>
