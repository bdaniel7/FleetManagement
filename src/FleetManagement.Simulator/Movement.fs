module FleetManagement.Simulator.Movement

open System
open Types

// ── Haversine distance ────────────────────────────────────────

let distanceKm (lat1, lon1) (lat2, lon2) =
    let toRad d = d * Math.PI / 180.0
    let R = 6371.0
    let dLat = toRad (lat2 - lat1)
    let dLon = toRad (lon2 - lon1)
    let a =
        Math.Sin(dLat / 2.0) ** 2.0 +
        Math.Cos(toRad lat1) * Math.Cos(toRad lat2) *
        Math.Sin(dLon / 2.0) ** 2.0
    R * 2.0 * Math.Atan2(Math.Sqrt a, Math.Sqrt(1.0 - a))

// ── Interpolate position between two coordinates ─────────────

let interpolate (lat1, lon1) (lat2, lon2) (t: float) =
    let t' = Math.Clamp(t, 0.0, 1.0)
    (lat1 + (lat2 - lat1) * t', lon1 + (lon2 - lon1) * t')

// ── Speed variation — smooth random walk ─────────────────────

let private rng = Random()

let varySpeed (current: float) (minSpd: float) (maxSpd: float) =
    let delta = (rng.NextDouble() - 0.5) * 8.0   // ±4 km/h per tick
    Math.Clamp(current + delta, minSpd, maxSpd)

// ── Engine temperature model ──────────────────────────────────
// Warms up while moving, cools when stopped. Target ~88°C at full speed.

let updateEngineTemp (currentTemp: float) (speedKmh: float) =
    let target = if speedKmh > 5.0 then 75.0 + speedKmh * 0.15 else 35.0
    let rate   = 0.05   // degrees per tick convergence rate
    currentTemp + (target - currentTemp) * rate

// ── Fuel consumption ──────────────────────────────────────────
// Rate increases at high speed (aerodynamic drag model).

let burnFuel (fuelPct: float) (speedKmh: float) (distKm: float) (burnRate: float) =
    let speedFactor = 1.0 + Math.Max(0.0, (speedKmh - 80.0) / 80.0) * 0.5
    let consumed    = distKm * burnRate * speedFactor
    Math.Max(0.0, fuelPct - consumed)

// ── Advance vehicle one tick ──────────────────────────────────

let tick (opts: SimOptions) (tickDurationSec: float) (sv: SimVehicle) : SimVehicle =
    match sv.Waypoints with
    | [] | [_] ->
        // No route — idle with engine cooling
        { sv with
            SpeedKmh   = 0.0
            EngineTemp = updateEngineTemp sv.EngineTemp 0.0
            IsMoving   = false }
    | waypoints ->
        let segCount = List.length waypoints - 1
        if sv.WaypointIndex >= segCount then
            // Arrived at final destination — mark as idle
            { sv with
                SpeedKmh      = 0.0
                EngineTemp    = updateEngineTemp sv.EngineTemp 0.0
                IsMoving      = false
                WaypointIndex = segCount }
        else
            let origin = waypoints.[sv.WaypointIndex]
            let dest   = waypoints.[sv.WaypointIndex + 1]
            let segLenKm = distanceKm origin dest

            // How far do we travel this tick?
            let speed      = varySpeed sv.SpeedKmh opts.SpeedMin opts.SpeedMax
            let travelKm   = speed / 3600.0 * tickDurationSec
            let segProgress=
                if segLenKm > 0.001
                then travelKm / segLenKm
                else 1.0   // zero-length segment — jump to next

            let newFraction = sv.SegmentFraction + segProgress

            if newFraction >= 1.0 then
                // Crossed into next segment
                let nextIndex    = sv.WaypointIndex + 1
                let (lat2, lon2) = dest
                let overshootKm  = (newFraction - 1.0) * segLenKm
                let fuel         = burnFuel sv.FuelPct speed segLenKm opts.FuelBurnRate
                { sv with
                    Lat             = lat2
                    Lon             = lon2
                    SpeedKmh        = speed
                    FuelPct         = fuel
                    EngineTemp      = updateEngineTemp sv.EngineTemp speed
                    OdometerKm      = sv.OdometerKm + segLenKm
                    WaypointIndex   = nextIndex
                    SegmentFraction = 0.0
                    IsMoving        = true }
            else
                // Still within current segment — interpolate position
                let (lat, lon) = interpolate origin dest newFraction
                let traveledKm = segLenKm * segProgress
                let fuel       = burnFuel sv.FuelPct speed traveledKm opts.FuelBurnRate
                { sv with
                    Lat             = lat
                    Lon             = lon
                    SpeedKmh        = speed
                    FuelPct         = fuel
                    EngineTemp      = updateEngineTemp sv.EngineTemp speed
                    OdometerKm      = sv.OdometerKm + traveledKm
                    SegmentFraction = newFraction
                    IsMoving        = true }
