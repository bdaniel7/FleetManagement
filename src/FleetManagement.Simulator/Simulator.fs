module FleetManagement.Simulator.Simulator

open System
open System.Threading
open Types
open ApiClient
open Movement

// ── Build SimVehicle from API DTO ─────────────────────────────

let private rng = Random()

let private makeSimVehicle (v: VehicleDto) (waypoints: (float * float) list) : SimVehicle = {
    Id              = v.id
    LicensePlate    = v.licensePlate
    VehicleType     = v.vehicleType
    Lat             = v.currentLocation.latitude
    Lon             = v.currentLocation.longitude
    SpeedKmh        = 0.0
    FuelPct         = v.fuelLevelPct
    EngineTemp      = 20.0 + rng.NextDouble() * 10.0
    OdometerKm      = 0.0
    Waypoints       = waypoints
    WaypointIndex   = 0
    SegmentFraction = 0.0
    IsMoving        = false
}

// ── Generate a random city-to-city route (Germany bounding box) ─

let private germanCities = [|
    (52.5200, 13.4050)   // Berlin
    (48.1351, 11.5820)   // Munich
    (53.5511,  9.9937)   // Hamburg
    (50.1109,  8.6821)   // Frankfurt
    (50.9333,  6.9600)   // Cologne
    (48.7758,  9.1829)   // Stuttgart
    (51.3397, 12.3731)   // Leipzig
    (51.5136,  7.4653)   // Dortmund
    (49.4521, 11.0767)   // Nuremberg
    (52.3759,  9.7320)   // Hannover
    (51.0504, 13.7373)   // Dresden
    (53.0793,  8.8017)   // Bremen
    (54.3233, 10.1228)   // Kiel
    (48.3705, 10.8978)   // Augsburg
    (50.8757,  8.0243)   // Siegen
|]

let private randomRoute () : (float * float) list =
    let count = 2 + rng.Next(0, 3)  // 2–4 waypoints
    let indices = Array.init count (fun _ -> rng.Next(0, germanCities.Length))
    indices |> Array.map (fun i -> germanCities.[i]) |> Array.toList

// ── Check if we've lost connectivity ─────────────────────────

let private checkHealth (client: System.Net.Http.HttpClient) = async {
    try
        let! response = client.GetAsync("api/fleet/health") |> Async.AwaitTask
        return response.IsSuccessStatusCode
    with _ -> return false
}

// ── Single tick for all vehicles ─────────────────────────────

let private runTick
    (client  : System.Net.Http.HttpClient)
    (opts    : SimOptions)
    (tickSec : float)
    (vehicles: SimVehicle[]) : Async<SimVehicle[]> = async {

    let updated = Array.copy vehicles
    let tasks =
        vehicles
        |> Array.mapi (fun i sv ->
            async {
                let sv' = Movement.tick opts tickSec sv

                // Always post telemetry
                do! postTelemetry client sv'.Id sv'

                // Patch location if moved
                if sv'.IsMoving || sv'.Lat <> sv.Lat || sv'.Lon <> sv.Lon then
                    do! patchLocation client sv'.Id sv'.Lat sv'.Lon sv'.SpeedKmh

                // If just started moving, set status to EnRoute
                if sv'.IsMoving && not sv.IsMoving then
                    do! patchStatus client sv'.Id "EnRoute"

                // If just stopped (reached last waypoint), set back to Idle
                if not sv'.IsMoving && sv.IsMoving then
                    do! patchStatus client sv'.Id "Idle"

                updated.[i] <- sv'
            })

    // Run all vehicle updates concurrently
    do! Async.Parallel tasks |> Async.Ignore
    return updated
}

// ── Main simulation loop ──────────────────────────────────────

let run (opts: SimOptions) (cancelToken: CancellationToken) = async {
    use client = makeClient opts.ApiBaseUrl
    let tickSec = float opts.TickMs / 1000.0

    printfn "  Connecting to %s…" opts.ApiBaseUrl
    let! alive = checkHealth client
    if not alive then
        printfn "  ✗ Cannot reach API at %s — is the server running?" opts.ApiBaseUrl
        return ()

    printfn "  ✓ API reachable"
    printfn "  Loading vehicles…"

    let! apivehicles = fetchVehicles client
    if apivehicles.Length = 0 then
        printfn "  No vehicles found — run the seed SQL first."
        return ()

    let take =
        if opts.VehicleCount <= 0 then apivehicles.Length
        else min opts.VehicleCount apivehicles.Length

    printfn "  Found %d vehicles — simulating %d" apivehicles.Length take

    // Build SimVehicle array with random routes
    let mutable simVehicles =
        apivehicles
        |> Array.take take
        |> Array.map (fun v ->
            let waypoints = randomRoute ()
            makeSimVehicle v waypoints)

    printfn ""
    printfn "  ╔═══════════════════════════════════════════════════╗"
    printfn "  ║  FleetOS Vehicle Simulator                        ║"
    printfn "  ║  %d vehicles  |  tick %dms  |  Ctrl+C to stop   ║" take opts.TickMs
    printfn "  ╚═══════════════════════════════════════════════════╝"
    printfn ""

    let mutable tick = 0
    while not cancelToken.IsCancellationRequested &&
          (opts.TotalTicks <= 0 || tick < opts.TotalTicks) do

        let! updated = runTick client opts tickSec simVehicles
        simVehicles <- updated
        tick <- tick + 1

        if opts.Verbose || tick % 10 = 0 then
            let moving  = simVehicles |> Array.filter (fun sv -> sv.IsMoving) |> Array.length
            let avgFuel = simVehicles |> Array.averageBy (fun sv -> sv.FuelPct)
            let avgSpd  = simVehicles |> Array.filter (fun sv -> sv.IsMoving) |> Array.averageBy (fun sv -> sv.SpeedKmh)
            printfn "  [Tick %4d]  Moving: %2d/%d  AvgFuel: %.1f%%  AvgSpeed: %.0f km/h"
                    tick moving take avgFuel (if moving > 0 then avgSpd else 0.0)

            if opts.Verbose then
                simVehicles |> Array.iter (fun sv ->
                    let icon = if sv.IsMoving then "▶" else "■"
                    printfn "    %s %-12s  %5.2f,%5.2f  Fuel:%.0f%%  Temp:%.0f°C  Odo:%.0fkm"
                            icon sv.LicensePlate sv.Lat sv.Lon sv.FuelPct sv.EngineTemp sv.OdometerKm)
                printfn ""

        // Respawn vehicle route when it reaches the end
        simVehicles <-
            simVehicles |> Array.map (fun sv ->
                if not sv.IsMoving && sv.WaypointIndex >= List.length sv.Waypoints - 1 then
                    // Start a new random route after a short pause
                    { sv with
                        Waypoints       = randomRoute()
                        WaypointIndex   = 0
                        SegmentFraction = 0.0
                        IsMoving        = false }
                else sv)

        if not cancelToken.IsCancellationRequested then
            do! Async.Sleep opts.TickMs

    printfn ""
    printfn "  Simulation stopped after %d ticks." tick
}
