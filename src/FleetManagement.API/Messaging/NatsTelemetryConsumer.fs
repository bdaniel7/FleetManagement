module FleetManagement.API.Messaging.NatsTelemetryConsumer

open System
open System.Linq
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open FleetManagement.Core.Domain
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open NATS.Client.Core
open NATS.Client.JetStream
open NATS.Client.JetStream.Models
open FleetManagement.Infrastructure.IRepositories
open FleetManagement.Infrastructure.DbContext
open FleetManagement.API.Hubs.TelemetryHub

// ── Wire-format message (matches what the simulator publishes) ─

[<CLIMutable>]
type TelemetryMessage = {
    [<JsonPropertyName("vehicleId")>]  VehicleId  : string
    [<JsonPropertyName("lat")>]        Lat        : float
    [<JsonPropertyName("lon")>]        Lon        : float
    [<JsonPropertyName("speedKmh")>]   SpeedKmh   : float
    [<JsonPropertyName("fuelPct")>]    FuelPct    : float
    [<JsonPropertyName("engineTemp")>] EngineTemp : float
    [<JsonPropertyName("odometerKm")>] OdometerKm : float
    [<JsonPropertyName("diagCodes")>]  DiagCodes  : string[]
    [<JsonPropertyName("timestamp")>]  Timestamp  : DateTimeOffset
}

// ── Configuration ──────────────────────────────────────────────

type NatsOptions = {
    Url:            string
    StreamName:     string
    ConsumerName:   string
    Subject:        string
    /// Default = 500
    BatchSize:      int
    PollIntervalMs: int
}

module NatsOptions =
    let fromConfig (cfg: IConfiguration) =
        let s = cfg.GetSection("Nats")
        { Url            = s["Url"]            |> Option.ofObj |> Option.defaultValue "nats://localhost:4222"
          StreamName     = s["StreamName"]     |> Option.ofObj |> Option.defaultValue "FLEET_TELEMETRY"
          ConsumerName   = s["ConsumerName"]   |> Option.ofObj |> Option.defaultValue "fleet-api-consumer"
          Subject        = s["Subject"]        |> Option.ofObj |> Option.defaultValue "fleet.telemetry.>"
          BatchSize      = s["BatchSize"]      |> Option.ofObj |> Option.map int |> Option.defaultValue 500
          PollIntervalMs = s["PollIntervalMs"] |> Option.ofObj |> Option.map int |> Option.defaultValue 5000 }

// ── Hosted service ─────────────────────────────────────────────

type NatsTelemetryConsumer(
    cfg         : IConfiguration,
    db          : IDbContext,
    vehicleRepo : IVehicleRepository,
    broadcaster : IFleetHubBroadcaster,
    logger      : ILogger<NatsTelemetryConsumer>) =

    let opts = NatsOptions.fromConfig cfg

    let jsonOpts =
        let o = JsonSerializerOptions()
        o.PropertyNameCaseInsensitive <- true
        o

    // ── Ensure JetStream stream exists ─────────────────────────

    let ensureStream (js: NatsJSContext) = task {
        try
            let! _ = js.GetStreamAsync(opts.StreamName)
            logger.LogInformation("JetStream stream '{Stream}' already exists", opts.StreamName)
        with _ ->
            let config = StreamConfig(opts.StreamName, [ opts.Subject ].ToList() )
            config.MaxAge           <- TimeSpan.FromDays 7.0   // retain 7 days of telemetry
            config.MaxBytes         <- 2L * 1024L * 1024L * 1024L  // 2 GB cap
            config.Storage          <- StreamConfigStorage.File
            config.Discard          <- StreamConfigDiscard.Old
            config.NumReplicas      <- 1
            let! _ = js.CreateStreamAsync(config)
            logger.LogInformation("JetStream stream '{Stream}' created on subject '{Subject}'",
                opts.StreamName, opts.Subject)
    }

    // ── Ensure durable pull consumer exists ───────────────────

    let ensureConsumer (js: NatsJSContext) = task {
        let! stream = js.GetStreamAsync(opts.StreamName)
        try
            let! _ = stream.GetConsumerAsync(opts.ConsumerName)
            logger.LogInformation("JetStream consumer '{Consumer}' already exists", opts.ConsumerName)
        with _ ->
            let config = ConsumerConfig(opts.ConsumerName)
            config.DurableName    <- opts.ConsumerName
            config.AckPolicy      <- ConsumerConfigAckPolicy.Explicit
            config.MaxAckPending  <- Convert.ToInt64(opts.BatchSize * 2)
            config.AckWait        <- TimeSpan.FromSeconds 30.0
            let! _ = stream.CreateOrUpdateConsumerAsync(config)
            logger.LogInformation("JetStream consumer '{Consumer}' created", opts.ConsumerName)
        return! stream.GetConsumerAsync(opts.ConsumerName)
    }

    // ── Process one batch of messages ──────────────────────────

    let persistTelemetryToDb (msgs: ResizeArray<TelemetryMessage>) = task {
            do! db.InTransaction(fun conn tx -> async {
                for t in msgs do
                    let! _ =
                        Db.execute
                            """INSERT INTO public.fms_telemetry_archive
                               (vehicle_id, recorded_at, lat, lon, speed_kmh, fuel_pct, engine_temp, odometer_km)
                               VALUES (@vid, @ts, @lat, @lon, @speed, @fuel, @temp, @odo)"""
                            {| vid   = Guid.Parse(t.VehicleId)
                               ts    = t.Timestamp
                               lat   = t.Lat
                               lon   = t.Lon
                               speed = t.SpeedKmh
                               fuel  = t.FuelPct
                               temp  = t.EngineTemp
                               odo   = t.OdometerKm |} conn tx
                    ()
                return ()
            })
    }

    let broadcastToSignalR (msgs: ResizeArray<TelemetryMessage>) =
        let perVehicle =
            msgs
            |> Seq.groupBy (fun t -> t.VehicleId)
            |> Seq.map (fun (vid, readings) -> readings |> Seq.maxBy (fun r -> r.Timestamp))

        for t in perVehicle do
            let loc = {Latitude = t.Lat; Longitude = t.Lon}
            broadcaster.BroadcastTelemetry {
                VehicleId  = VehicleId (Guid.Parse(t.VehicleId))
                Location   = loc
                DiagCodes  = []
                SpeedKmh   = t.SpeedKmh
                FuelPct    = t.FuelPct
                EngineTemp = t.EngineTemp
                OdometerKm = t.OdometerKm
                Timestamp  = t.Timestamp
            } |> ignore

    let processBatch (consumer: INatsJSConsumer) = task {
        let msgs = ResizeArray<TelemetryMessage>()

        // Fetch up to BatchSize messages, time out after 2s if fewer arrive
        let fetchOpts = NatsJSFetchOpts(MaxMsgs = opts.BatchSize, Expires = TimeSpan.FromSeconds 2.0)

        let batch = consumer.FetchAsync<byte[]>(fetchOpts).GetAsyncEnumerator()
        let mutable hasMore = true

        while hasMore do
            let! moved = batch.MoveNextAsync().AsTask()
            if moved then
                let msg = batch.Current
                try
                    let telemetry = JsonSerializer.Deserialize<TelemetryMessage>(msg.Data.AsSpan(), jsonOpts)
                    if telemetry <> Unchecked.defaultof<_> then
                        msgs.Add(telemetry)
                    do! msg.AckAsync().AsTask()
                with ex ->
                    logger.LogWarning(ex, "Failed to deserialize telemetry message — NAK'd")
                    do! msg.NakAsync().AsTask()
            else
                hasMore <- false

        if msgs.Count > 0 then
            logger.LogDebug("Processing batch of {Count} telemetry messages", msgs.Count)

            persistTelemetryToDb msgs |> ignore

            // Broadcast latest reading per vehicle via SignalR
            broadcastToSignalR msgs

        return msgs.Count
    }

    // ── IHostedService ─────────────────────────────────────────

    let mutable loopTask: Task = Task.CompletedTask

    interface IHostedService with

        member _.StartAsync(ct: CancellationToken) =
            logger.LogInformation("NATS telemetry consumer starting — URL: {Url}", opts.Url)

            let natsOpts = NatsOpts(Url = opts.Url,
                                        WebSocketOpts = NatsWebSocketOpts.Default,
                                        TlsOpts = NatsTlsOpts.Default,
                                        AuthOpts = NatsAuthOpts.Default)

            // Build the poll loop as a plain Task and store it so StopAsync can observe it
            loopTask <- (task {
                let conn = NatsConnection(natsOpts)
                try
                    do! conn.ConnectAsync()
                    //let nats = NatsJSOpts(natsOpts)
                    let jsContext = NatsJSContext(conn)

                    do! ensureStream jsContext
                    let! consumer = ensureConsumer jsContext

                    logger.LogInformation(
                        "NATS consumer ready — stream: {Stream}, consumer: {Consumer}, " +
                        "batch: {Batch}, interval: {Interval}ms",
                        opts.StreamName, opts.ConsumerName, opts.BatchSize, opts.PollIntervalMs)

                    while not ct.IsCancellationRequested do
                        try
                            let! count = processBatch consumer
                            if count > 0 then
                                logger.LogInformation("Processed {Count} telemetry events from NATS", count)
                            do! Task.Delay(opts.PollIntervalMs, ct)
                        with
                        | :? OperationCanceledException -> ()   // clean shutdown
                        | ex ->
                            logger.LogWarning(ex, "Error in NATS telemetry poll loop — retrying in {Ms}ms", opts.PollIntervalMs)
                            do! Task.Delay(opts.PollIntervalMs, ct)
                finally
                    ()

                do! conn.DisposeAsync().AsTask()
            } :> Task )

            Task.CompletedTask

        member _.StopAsync(ct: CancellationToken) = task {
            logger.LogInformation("NATS telemetry consumer stopping…")
            try
                let! _ = Task.WhenAny(loopTask, Task.Delay(Timeout.Infinite, ct))
                return ()
            with _ -> ()
        }
