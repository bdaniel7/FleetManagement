module FleetManagement.Core.Commands

open Domain

// ============================================================
//  Commands — intent to mutate state
// ============================================================

type VehicleCommand =
    | RegisterVehicle of {|
        LicensePlate    : string
        VehicleType     : VehicleType
        MaxPayloadKg    : float
        InitialLocation : GeoCoordinate |}
    | UpdateVehicleLocation of VehicleId * GeoCoordinate * float   // location, speed km/h
    | UpdateVehicleStatus   of VehicleId * VehicleStatus
    | UpdateFuelLevel       of VehicleId * float                   // 0.0 – 100.0
    | AssignDriver          of VehicleId * DriverId
    | UnassignDriver        of VehicleId
    | UpdateTelemetry       of VehicleId * VehicleTelemetry

type RouteCommand =
    | PlanRoute of {|
        VehicleId   : VehicleId
        DriverId    : DriverId option
        Waypoints   : GeoCoordinate list
        Algorithm   : PathfindingAlgorithm
        Priority    : Priority |}
    | ActivateRoute   of RouteId
    | CompleteRoute   of RouteId
    | CancelRoute     of RouteId * string   // reason
    | RerouteVehicle  of RouteId * PathfindingAlgorithm

type DriverCommand =
    | RegisterDriver of {|
        FirstName     : string
        LastName      : string
        LicenseNumber : string
        PhoneNumber   : string
        Email         : string |}
    | UpdateDriverAvailability of DriverId * bool
    | LogDriverHours           of DriverId * float

// ============================================================
//  Command result
// ============================================================

type CommandError =
    | ValidationFailure  of string list
    | NotFound           of string
    | Conflict           of string
    | InternalError      of string

type CommandResult<'T> = Result<'T, CommandError>
