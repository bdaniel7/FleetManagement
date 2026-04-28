module FleetManagement.Core.Domain

open System

open System.Text.Json.Serialization

// ============================================================
//  Value Objects
// ============================================================

[<Struct>]
type VehicleId = VehicleId of Guid

[<Struct>]
type RouteId = RouteId of Guid

[<Struct>]
type DriverId = DriverId of Guid

[<Struct>]
type NodeId = NodeId of string   // Road/Waypoint identifier

[<Struct>]
type AlertId = AlertId of Guid

[<Struct>]
type GeoCoordinate = {
    Latitude  : float
    Longitude : float
}

module GeoCoordinate =
    /// Haversine distance in kilometers
    let distanceKm (a: GeoCoordinate) (b: GeoCoordinate) =
        let toRad d = d * Math.PI / 180.0
        let R = 6371.0
        let dLat = toRad (b.Latitude  - a.Latitude)
        let dLon = toRad (b.Longitude - a.Longitude)
        let a' =
            Math.Sin(dLat/2.0) ** 2.0 +
            Math.Cos(toRad a.Latitude) * Math.Cos(toRad b.Latitude) *
            Math.Sin(dLon/2.0) ** 2.0
        R * 2.0 * Math.Atan2(Math.Sqrt(a'), Math.Sqrt(1.0 - a'))

// ============================================================
//  Enumerations
// ============================================================

[<JsonConverter(typeof<VehicleStatusConverter>)>]
type VehicleStatus =
    | Idle
    | EnRoute
    | Maintenance
    | OutOfService
    | Charging       // For electric vehicles
and VehicleStatusConverter() =
    inherit JsonConverter<VehicleStatus>()

    override _.Read(reader, _typeToConvert, _options) =
        match reader.GetString() with
        | "Idle"         -> Idle
        | "En Route"      -> EnRoute
        | "Maintenance"  -> Maintenance
        | "Out Of Service" -> OutOfService
        | "Charging"     -> Charging
        | unknown        -> failwith $"Unknown VehicleStatus: '{unknown}'"

    override _.Write(writer, value, _options) =
        let str =
            match value with
            | Idle         -> "Idle"
            | EnRoute      -> "En Route"
            | Maintenance  -> "Maintenance"
            | OutOfService -> "Out Of Service"
            | Charging     -> "Charging"
        writer.WriteStringValue(str)

[<JsonConverter(typeof<VehicleTypeConverter>)>]
type VehicleType =
    | Truck
    | Van
    | Car
    | Motorcycle
    | ElectricTruck
    | ElectricVan
and VehicleTypeConverter() =
    inherit JsonConverter<VehicleType>()

    override _.Read(reader, _typeToConvert, _options) =
        match reader.GetString() with
        | "Truck"        -> Truck
        | "Van"          -> Van
        | "Car"          -> Car
        | "Motorcycle"   -> Motorcycle
        | "Electric Truck"-> ElectricTruck
        | "Electric Van"  -> ElectricVan
        | unknown        -> failwith $"Unknown VehicleType: '{unknown}'"

    override _.Write(writer, value, _options) =
        let str =
            match value with
            | Truck         -> "Truck"
            | Van           -> "Van"
            | Car           -> "Car"
            | Motorcycle    -> "Motorcycle"
            | ElectricTruck -> "Electric Truck"
            | ElectricVan   -> "Electric Van"
        writer.WriteStringValue(str)

[<JsonConverter(typeof<PriorityConverter>)>]
type Priority =
    | Low
    | Normal
    | High
    | Emergency
and PriorityConverter() =
    inherit JsonConverter<Priority>()

    override _.Read(reader, _typeToConvert, _options) =
        match reader.GetString() with
        | "Low"        -> Low
        | "Normal"     -> Normal
        | "High"       -> High
        | "Emergency"  -> Emergency
        | unknown      -> failwith $"Unknown Priority: '{unknown}'"

    override _.Write(writer, value, _options) =
        let str =
            match value with
            | Low -> "Low"
            | Normal -> "Normal"
            | High -> "High"
            | Emergency -> "Emergency"
        writer.WriteStringValue(str)

[<JsonConverter(typeof<RouteStatusConverter>)>]
type RouteStatus =
    | Planned
    | Active
    | Completed
    | Cancelled
    | Rerouting
and RouteStatusConverter() =
    inherit JsonConverter<RouteStatus>()

    override _.Read(reader, _typeToConvert, _options) =
        match reader.GetString() with
        | "Planned"        -> Planned
        | "Active"     -> Active
        | "Completed"       -> Completed
        | "Cancelled"  -> Cancelled
        | "Rerouting"  -> Rerouting
        | unknown      -> failwith $"Unknown RouteStatus: '{unknown}'"

    override _.Write(writer, value, _options) =
        let str =
            match value with
            | Planned -> "Planned"
            | Active -> "Active"
            | Completed -> "Completed"
            | Cancelled -> "Cancelled"
            | Rerouting -> "Rerouting"

        writer.WriteStringValue(str)


// ── Trip ─────────────────────────────────────────────────────
// A user-defined journey with ordered named stops.
// The start and end waypoint can be the same (circular trip).

[<Struct>]
type TripId = TripId of Guid

[<JsonConverter(typeof<TripStatusConverter>)>]
type TripStatus =
    | Draft      // being planned, not yet assigned
    | Scheduled  // assigned to a vehicle, not yet started
    | InProgress // vehicle is on the road
    | TripCompleted
    | TripCancelled
and TripStatusConverter() =
    inherit JsonConverter<TripStatus>()

    override _.Read(reader, _typeToConvert, _options) =
        match reader.GetString() with
        | "Draft"        -> Draft
        | "Scheduled"     -> Scheduled
        | "InProgress"       -> InProgress
        | "TripCompleted"  -> TripCompleted
        | "TripCancelled"  -> TripCancelled
        | unknown      -> failwith $"Unknown TripStatus: '{unknown}'"

    override _.Write(writer, value, _options) =
        let str =
            match value with
            | Draft          -> "Draft"
            | Scheduled      -> "Scheduled"
            | InProgress     -> "InProgress"
            | TripCompleted  -> "TripCompleted"
            | TripCancelled  -> "TripCancelled"

        writer.WriteStringValue(str)

type TripWaypoint = {
    Order       : int                 // 0-based position in the trip
    Label       : string              // e.g. "Warehouse", "Customer A"
    Coordinate  : GeoCoordinate
    Notes       : string              // optional stop notes
    DwellMin    : int                 // expected stop duration in minutes
}

type Trip = {
    Id          : TripId
    Name        : string
    Description : string
    VehicleId   : VehicleId option
    DriverId    : DriverId option
    Waypoints   : TripWaypoint list   // ordered by TripWaypoint.Order
    Status      : TripStatus
    IsCircular  : bool                // true = last waypoint returns to first
    TotalDistanceKm : float           // straight-line sum, updated on save
    CreatedAt   : DateTimeOffset
    UpdatedAt   : DateTimeOffset
    StartedAt   : DateTimeOffset option
    CompletedAt : DateTimeOffset option
}

type AlertRecord = {
    Id        : AlertId
    VehicleId : VehicleId option
    Message   : string
    IssuedAt  : DateTimeOffset
}

type PathfindingAlgorithm =
    | AStar
    | Dijkstra
    | BellmanFord
and AlgorithmConverter() =
    inherit JsonConverter<PathfindingAlgorithm>()

    override _.Read(reader, _typeToConvert, _options) =
        match reader.GetString() with
        | "AStar"        -> AStar
        | "Dijkstra"     -> Dijkstra
        | "BellmanFord"  -> BellmanFord
        | unknown      -> failwith $"Unknown PathfindingAlgorithm: '{unknown}'"

    override _.Write(writer, value, _options) =
        let str =
            match value with
            | AStar        -> "AStar"
            | Dijkstra     -> "Dijkstra"
            | BellmanFord  -> "BellmanFord"

        writer.WriteStringValue(str)

// ============================================================
//  Aggregates
// ============================================================

type Vehicle = {
    Id              : VehicleId
    LicensePlate    : string
    VehicleType     : VehicleType
    Status          : VehicleStatus
    CurrentLocation : GeoCoordinate
    AssignedDriver  : DriverId option
    FuelLevelPct    : float         // 0.0 – 100.0
    SpeedKmh        : float
    MaxPayloadKg    : float
    CurrentPayloadKg: float
    Telemetry       : VehicleTelemetry
    CreatedAt       : DateTimeOffset
    UpdatedAt       : DateTimeOffset
}

and VehicleTelemetry = {
    OdometerKm       : float
    EngineTemp       : float
    BatteryLevel     : float option  // EV-only
    LastHeartbeat    : DateTimeOffset
    DiagnosticCodes  : string list
}

type Driver = {
    Id            : DriverId
    FirstName     : string
    LastName      : string
    LicenseNumber : string
    PhoneNumber   : string
    Email         : string
    IsAvailable   : bool
    HoursWorked   : float
    MaxHoursPerDay: float
    CreatedAt     : DateTimeOffset
}

type Waypoint = {
    NodeId      : NodeId
    Coordinate  : GeoCoordinate
    Address     : string
    ArrivalTime : DateTimeOffset option
    DepartureTime: DateTimeOffset option
    StopDurationMin: int
}

type Route = {
    Id            : RouteId
    VehicleId     : VehicleId
    DriverId      : DriverId option
    Waypoints     : Waypoint list           // ordered waypoints
    OptimizedPath : GeoCoordinate list       // full path coordinates through road graph
    TotalDistanceKm: float
    EstimatedDurationMin: int
    Status        : RouteStatus
    Priority      : Priority
    Algorithm     : PathfindingAlgorithm
    CreatedAt     : DateTimeOffset
    StartedAt     : DateTimeOffset option
    CompletedAt   : DateTimeOffset option
}

type FleetSummary = {
    TotalVehicles    : int
    ActiveVehicles   : int
    IdleVehicles     : int
    MaintenanceCount : int
    TotalRoutes      : int
    ActiveRoutes     : int
    AvgFuelLevel     : float
    LastUpdated      : DateTimeOffset
}
