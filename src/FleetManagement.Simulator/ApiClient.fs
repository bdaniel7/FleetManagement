module FleetManagement.Simulator.ApiClient

open System
open System.Net.Http
open System.Net.Http.Json
open System.Text
open System.Text.Json
open Types

// ── JSON options matching the API's camelCase policy ─────────

let private jsonOpts =
    let o = JsonSerializerOptions()
    o.PropertyNameCaseInsensitive <- true
    o.PropertyNamingPolicy        <- JsonNamingPolicy.CamelCase
    o

// ── HTTP client factory ───────────────────────────────────────

let makeClient (baseUrl: string) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(baseUrl.TrimEnd('/') + "/")
    client.Timeout     <- TimeSpan.FromSeconds 10.0
    client

// ── Wait for API to be available ─────────────────────────────

let waitForApi (client: HttpClient) retryCount waitSeconds = async {
    let mutable success = false
    let mutable attempt = 0
    let mutable waitTime = waitSeconds

    while not success && attempt < retryCount do
        attempt <- attempt + 1
        try
            let! response = client.GetAsync("api/fleet/health") |> Async.AwaitTask
            if response.IsSuccessStatusCode then
                success <- true
            else
                eprintfn "  [Retry] Attempt %d: API returned %O" attempt response.StatusCode
                if attempt < retryCount then
                    eprintfn "  [Retry] Waiting %.1fs before retry..." waitTime
                    do! Async.Sleep(int (waitTime * 1000.0))
                    waitTime <- waitTime * 2.0  // exponential backoff
        with ex ->
            eprintfn "  [Retry] Attempt %d failed: %s" attempt ex.Message
            if attempt < retryCount then
                eprintfn "  [Retry] Waiting %.1fs before retry..." waitTime
                do! Async.Sleep(int (waitTime * 1000.0))
                waitTime <- waitTime * 2.0  // exponential backoff

    return success
}

// ── Fetch all vehicles ────────────────────────────────────────

let fetchVehicles (client: HttpClient) = async {
    try
        let! response = client.GetAsync("api/vehicles") |> Async.AwaitTask
        if response.IsSuccessStatusCode then
            let! json = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            let vehicles = JsonSerializer.Deserialize<VehicleDto[]>(json, jsonOpts)
            return if vehicles = null then [||] else vehicles
        else
            return [||]
    with ex ->
        eprintfn "  [ApiClient] fetchVehicles failed: %s" ex.Message
        return [||]
}

// ── Fetch active routes ───────────────────────────────────────

/// Parse waypoints from the JSON stored in the route (flat lat/lon objects).
let private parseWaypoints (waypointsJson: JsonElement) : (float * float) list =
    try
        waypointsJson.EnumerateArray()
        |> Seq.map (fun el ->
            let lat = el.GetProperty("lat").GetDouble()
            let lon = el.GetProperty("lon").GetDouble()
            (lat, lon))
        |> Seq.toList
    with _ -> []

let fetchActiveRoutes (client: HttpClient) = async {
    try
        let! response = client.GetAsync("api/routes/active") |> Async.AwaitTask
        if response.IsSuccessStatusCode then
            let! json = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            use doc = JsonDocument.Parse(json)
            return
                doc.RootElement.EnumerateArray()
                |> Seq.map (fun el ->
                    let waypoints =
                        try parseWaypoints (el.GetProperty("waypointsJson"))
                        with _ -> []
                    {
                        id                  = el.GetProperty("id").GetString()
                        vehicleId           = el.GetProperty("vehicleId").GetString()
                        status              = el.GetProperty("status").GetString()
                        algorithm           = el.GetProperty("algorithm").GetString()
                        priority            = el.GetProperty("priority").GetString()
                        totalDistanceKm     = el.GetProperty("totalDistanceKm").GetDouble()
                        estimatedDurationMin= el.GetProperty("estimatedDurationMin").GetInt32()
                        waypointsDtoList    = [||]
                    })
                |> Seq.toArray
        else
            return [||]
    with ex ->
        eprintfn "  [ApiClient] fetchActiveRoutes failed: %s" ex.Message
        return [||]
}

// ── POST telemetry ────────────────────────────────────────────

let postTelemetry (client: HttpClient) (vehicleId: string) (sv: SimVehicle) = async {
    try
        let payload = JsonSerializer.Serialize({|
            speedKmh   = sv.SpeedKmh
            fuelPct    = sv.FuelPct
            engineTemp = sv.EngineTemp
            odometerKm = sv.OdometerKm
            diagCodes  = ([] : string list)
        |}, jsonOpts)
        use content = new StringContent(payload, Encoding.UTF8, "application/json")
        let! _ = client.PostAsync($"api/vehicles/{vehicleId}/telemetry", content) |> Async.AwaitTask
        return ()
    with ex ->
        eprintfn "  [ApiClient] postTelemetry(%s) failed: %s" vehicleId ex.Message
}

// ── PATCH location ────────────────────────────────────────────

let patchLocation (client: HttpClient) (vehicleId: string) (lat: float) (lon: float) (speedKmh: float) = async {
    try
        let payload = JsonSerializer.Serialize({|
            latitude  = lat
            longitude = lon
            speedKmh  = speedKmh
        |}, jsonOpts)
        use content  = new StringContent(payload, Encoding.UTF8, "application/json")
        use request  = new HttpRequestMessage(HttpMethod.Patch, $"api/vehicles/{vehicleId}/location")
        request.Content <- content
        let! _ = client.SendAsync(request) |> Async.AwaitTask
        return ()
    with ex ->
        eprintfn "  [ApiClient] patchLocation(%s) failed: %s" vehicleId ex.Message
}

// ── PATCH vehicle status ──────────────────────────────────────

let patchStatus (client: HttpClient) (vehicleId: string) (status: string) = async {
    try
        let payload = JsonSerializer.Serialize({| status = status |}, jsonOpts)
        use content  = new StringContent(payload, Encoding.UTF8, "application/json")
        use request  = new HttpRequestMessage(HttpMethod.Patch, $"api/vehicles/{vehicleId}/status")
        request.Content <- content
        let! _ = client.SendAsync(request) |> Async.AwaitTask
        return ()
    with ex ->
        eprintfn "  [ApiClient] patchStatus(%s) failed: %s" vehicleId ex.Message
}

// ── POST telemetry to set initial fuel ─────────────────────────

let setFuelForAllVehicles (client: HttpClient) (fuelPct: float) (vehicleIds: Guid[]) = async {
    let tasks =
        vehicleIds
        |> Array.map (fun id ->
            async {
                try
                    let payload = JsonSerializer.Serialize({|
                        speedKmh   = 0.0
                        fuelPct    = fuelPct
                        engineTemp = 20.0
                        odometerKm = 0.0
                        diagCodes  = ([] : string list)
                    |}, jsonOpts)
                    use content = new StringContent(payload, Encoding.UTF8, "application/json")
                    let! _ = client.PostAsync($"api/vehicles/{id}/telemetry", content) |> Async.AwaitTask
                    return true
                with ex ->
                    eprintfn "  [ApiClient] setFuelForAllVehicles(%O) failed: %s" id ex.Message
                    return false
            })
    let! results = Async.Parallel tasks
    return results |> Array.filter id |> Array.length
}
