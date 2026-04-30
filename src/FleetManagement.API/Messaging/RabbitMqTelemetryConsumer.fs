namespace FleetManagement.API.Messaging
//module FleetManagement.API.Messaging.RabbitMqTelemetryConsumer

open System
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open RabbitMQ.Client
open FleetManagement.Core.Domain
open FleetManagement.Infrastructure.DbContext
open FleetManagement.API.Hubs.TelemetryHub

// ── Configuration ──────────────────────────────────────────────

type RabbitMqOptions = {
    Host:          string
    Port:          int
    VirtualHost:   string
    Username:      string
    Password:      string
    Exchange:      string
    Queue:         string
    RoutingKey:    string
    BatchSize:     int
    PollIntervalMs:int
}

module RabbitMqOptions =
    let fromConfig (cfg: IConfiguration) =
        let s = cfg.GetSection("RabbitMq")
        { Host           = s["Host"]           |> Option.ofObj |> Option.defaultValue "localhost"
          Port           = s["Port"]           |> Option.ofObj |> Option.map int |> Option.defaultValue 5672
          VirtualHost    = s["VirtualHost"]    |> Option.ofObj |> Option.defaultValue "fleet"
          Username       = s["Username"]       |> Option.ofObj |> Option.defaultValue "fleet_api"
          Password       = s["Password"]       |> Option.ofObj |> Option.defaultValue "api_dev_only_change_me"
          Exchange       = s["Exchange"]       |> Option.ofObj |> Option.defaultValue "fleet.telemetry"
          Queue          = s["Queue"]          |> Option.ofObj |> Option.defaultValue "fleet-api-telemetry"
          RoutingKey     = s["RoutingKey"]     |> Option.ofObj |> Option.defaultValue "fleet.telemetry.#"
          BatchSize      = s["BatchSize"]      |> Option.ofObj |> Option.map int |> Option.defaultValue 500
          PollIntervalMs = s["PollIntervalMs"] |> Option.ofObj |> Option.map int |> Option.defaultValue 5000 }

// ── Hosted service ─────────────────────────────────────────────

type RabbitMqTelemetryConsumer(
    cfg         : IConfiguration,
    db          : IDbContext,
    broadcaster : IFleetHubBroadcaster,
    logger      : ILogger<RabbitMqTelemetryConsumer>) =

    let opts = RabbitMqOptions.fromConfig cfg

    let jsonOpts =
        let o = JsonSerializerOptions()
        o.PropertyNameCaseInsensitive <- true
        o

    // ── Ensure exchange and queue exist ────────────────────────

    let declareTopology (channel: IChannel) = task {
        // Durable topic exchange — survives broker restart
        do! channel.ExchangeDeclareAsync(
                opts.Exchange,
                ExchangeType.Topic,
                durable    = true,
                autoDelete = false)

        // Durable queue — messages survive broker restart
        let! _ = channel.QueueDeclareAsync(
                     opts.Queue,
                     durable    = true,
                     exclusive  = false,
                     autoDelete = false)

        // Bind queue to exchange with routing key pattern
        do! channel.QueueBindAsync(opts.Queue, opts.Exchange, opts.RoutingKey)

        // One message at a time unacknowledged per consumer (back-pressure)
        // prefetchCount limits unacked messages — back-pressure control
        // uint16(n) is the correct F# cast syntax (not uint16 n which is a function call ambiguity)
        do! channel.BasicQosAsync(
                prefetchSize  = 0u,
                prefetchCount = uint16(opts.BatchSize),
                ``global``    = false)

        logger.LogInformation(
            "RabbitMQ topology ready — exchange: {Exchange}, queue: {Queue}, routing: {Key}",
            opts.Exchange, opts.Queue, opts.RoutingKey)
    }

    // ── Process one batch of messages ──────────────────────────

    let processBatch (channel: IChannel) = task {
        let msgs = ResizeArray<TelemetryMessage>()
        let tags = ResizeArray<uint64>()   // delivery tags for bulk ack

        // basicGet polls individual messages — loop until batch is full or queue is empty
        let mutable keepFetching = true
        while keepFetching && msgs.Count < opts.BatchSize do
            let! result = channel.BasicGetAsync(opts.Queue, autoAck = false)
            match result with
            | null ->
                keepFetching <- false   // queue empty
            | msg ->
                try
                    let body    = Encoding.UTF8.GetString(msg.Body.ToArray())
                    let telemetry = JsonSerializer.Deserialize<TelemetryMessage>(body, jsonOpts)
                    if telemetry <> Unchecked.defaultof<_> then
                        msgs.Add(telemetry)
                        tags.Add(msg.DeliveryTag)
                    else
                        // Malformed — reject without requeue
                        do! channel.BasicNackAsync(msg.DeliveryTag, multiple = false, requeue = false)
                with ex ->
                    logger.LogWarning(ex, "Failed to deserialise RabbitMQ message — rejected")
                    do! channel.BasicNackAsync(msg.DeliveryTag, multiple = false, requeue = false)

        if msgs.Count > 0 then
            logger.LogDebug("Processing RabbitMQ batch of {Count} telemetry messages", msgs.Count)

            // Bulk insert into telemetry_archive
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
            }) //|> Async.StartAsTask

            // Bulk ack all successfully processed messages (multiple = true)
            if tags.Count > 0 then
                do! channel.BasicAckAsync(tags.[tags.Count - 1], multiple = true)

            // Broadcast latest reading per vehicle via SignalR
            let perVehicle =
                msgs
                |> Seq.groupBy (fun t -> t.VehicleId)
                |> Seq.map (fun (_, readings) -> readings |> Seq.maxBy (fun r -> r.Timestamp))

            for t in perVehicle do
                let loc = { Latitude = t.Lat; Longitude = t.Lon }
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

        return msgs.Count
    }

    // ── IHostedService ─────────────────────────────────────────

    let mutable loopTask: Task<unit> = Task.FromResult(())

    interface IHostedService with

        member _.StartAsync(ct: CancellationToken) : Task =
            logger.LogInformation(
                "RabbitMQ telemetry consumer starting — {Host}:{Port}/{VHost}",
                opts.Host, opts.Port, opts.VirtualHost)

            loopTask <- (task {
                let factory = ConnectionFactory()
                factory.HostName    <- opts.Host
                factory.Port        <- opts.Port
                factory.VirtualHost <- opts.VirtualHost
                factory.UserName    <- opts.Username
                factory.Password    <- opts.Password

                // Retry connection until RabbitMQ is ready (important at startup)
                let mutable connected = false
                let mutable connection: IConnection = Unchecked.defaultof<_>
                while not connected && not ct.IsCancellationRequested do
                    try
                        let! conn = factory.CreateConnectionAsync(ct)
                        connection <- conn
                        connected  <- true
                        logger.LogInformation("RabbitMQ connection established")
                    with ex ->
                        logger.LogWarning(ex, "RabbitMQ not ready — retrying in 5s")
                        do! Task.Delay(5000, ct)

                if connected then
                    let mutable channel : IChannel = Unchecked.defaultof<_>
                    try
                        let! ch = connection.CreateChannelAsync(cancellationToken = ct)
                        channel <- ch
                        do! declareTopology channel

                        while not ct.IsCancellationRequested do
                            try
                                let! count = processBatch channel
                                if count > 0 then
                                    logger.LogInformation("Processed {Count} RabbitMQ telemetry events", count)
                                do! Task.Delay(opts.PollIntervalMs, ct)
                            with
                            | :? OperationCanceledException -> ()
                            | ex ->
                                logger.LogWarning(ex, "Error in RabbitMQ poll loop — retrying in {Ms}ms", opts.PollIntervalMs)
                                do! Task.Delay(opts.PollIntervalMs)
                        // finally
                        //     do! channel.CloseAsync()
                        //     channel.Dispose()
                    with ex ->
                        logger.LogError(ex, "RabbitMQ consumer fatal error")

                    // Disposal after try/with — task{} CE forbids do! in finally blocks
                    if not (Object.ReferenceEquals(channel, null)) then
                        do! channel.CloseAsync()
                        channel.Dispose()

                    do! connection.CloseAsync()
                    connection.Dispose()
            } : Task<unit>)

            Task.CompletedTask

        member _.StopAsync(_ct: CancellationToken) : Task =
            logger.LogInformation("RabbitMQ telemetry consumer stopped")
            loopTask :> Task
