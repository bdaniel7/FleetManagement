module FleetManagement.Simulator.Types

open System

// ── API response DTOs (camelCase JSON) ───────────────────────

[<CLIMutable>]
type GeoCoordinateDto = {
    latitude  : float
    longitude : float
}

[<CLIMutable>]
type VehicleDto = {
    id               : string
    licensePlate     : string
    vehicleType      : string
    status           : string
    currentLocation  : GeoCoordinateDto
    fuelLevelPct     : float
    speedKmh         : float
    maxPayloadKg     : float
    currentPayloadKg : float
}

[<CLIMutable>]
type WaypointDto = {
    nodeId    : string
    lat       : float
    lon       : float
    address   : string
}

[<CLIMutable>]
type RouteDto = {
    id                  : string
    vehicleId           : string
    status              : string
    algorithm           : string
    priority            : string
    totalDistanceKm     : float
    estimatedDurationMin: int
    waypointsDtoList    : WaypointDto[] // parsed from waypoints_json
}

// ── Simulator state for a single vehicle ─────────────────────

type SimVehicle = {
    Id            : string
    LicensePlate  : string
    VehicleType   : string
    // Current simulated position
    Lat           : float
    Lon           : float
    SpeedKmh      : float
    FuelPct       : float
    EngineTemp    : float
    OdometerKm    : float
    // Route being travelled (waypoint index, list of lat/lon stops)
    Waypoints     : (float * float) list
    WaypointIndex : int
    // Increments to next waypoint
    SegmentFraction : float  // 0.0 → 1.0 progress within current segment
    IsMoving      : bool
}

// ── CLI options ───────────────────────────────────────────────

type SimOptions = {
    ApiBaseUrl    : string
    VehicleCount  : int        // max vehicles to simulate (-1 = all)
    TickMs        : int        // milliseconds between each tick
    TotalTicks    : int        // how many ticks to run (-1 = infinite)
    SpeedMin      : float      // km/h min speed
    SpeedMax      : float      // km/h max speed
    FuelBurnRate  : float      // % per km
    InitialFuelPct: float option // set initial fuel for all vehicles
    RetryCount    : int        // number of retries for API calls
    RetryWaitSecs : float      // initial wait seconds between retries
    Verbose       : bool
}

module SimOptions =
    let defaults = {
        ApiBaseUrl    = "http://localhost:5000"
        VehicleCount  = -1
        TickMs        = 2000
        TotalTicks    = -1
        SpeedMin      = 40.0
        SpeedMax      = 120.0
        FuelBurnRate  = 0.08    // 0.08% per km
        InitialFuelPct = None
        RetryCount    = 5
        RetryWaitSecs = 5.0
        Verbose       = false
    }
