module FleetManagement.Simulator.RabbitMqPublisher

open System
open System.Text
open System.Text.Json
open System.Threading.Tasks
open RabbitMQ.Client
open Types
open FleetManagement.API.Messaging

let private jsonOpts =
    let o = JsonSerializerOptions()
    o.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    o

// ── Publisher ──────────────────────────────────────────────────

type RabbitMqPublisher(host: string, port: int, vhost: string, user: string, pass: string) =

    let exchange = "fleet.telemetry"
    let mutable channel: IChannel option = None
    let mutable connection: IConnection option = None

    member _.ConnectAsync() = async {
        let factory = ConnectionFactory()
        factory.HostName    <- host
        factory.Port        <- port
        factory.VirtualHost <- vhost
        factory.UserName    <- user
        factory.Password    <- pass

        let! conn = factory.CreateConnectionAsync() |> Async.AwaitTask
        let! ch   = conn.CreateChannelAsync() |> Async.AwaitTask

        // Declare durable topic exchange — idempotent, safe to call on every startup
        do! ch.ExchangeDeclareAsync(
                exchange,
                ExchangeType.Topic,
                durable    = true,
                autoDelete = false) |> Async.AwaitTask

        connection <- Some conn
        channel    <- Some ch
        printfn "  ✓ Connected to RabbitMQ at %s:%d/%s" host port vhost
    }

    member _.PublishTelemetry(sv: SimVehicle) = async {
        match channel with
        | None -> ()   // not connected — skip silently
        | Some ch ->
            let msg = {
                VehicleId  = sv.Id
                Lat        = sv.Lat
                Lon        = sv.Lon
                SpeedKmh   = sv.SpeedKmh
                FuelPct    = sv.FuelPct
                EngineTemp = sv.EngineTemp
                OdometerKm = sv.OdometerKm
                DiagCodes  = [||]
                Timestamp  = DateTimeOffset.UtcNow
            }
            let routingKey = $"fleet.telemetry.{sv.Id}"
            let body       = ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg, jsonOpts)))

            // BasicPublishAsync — persistent delivery mode so messages survive broker restart
            let props = BasicProperties()
            props.DeliveryMode  <- DeliveryModes.Persistent
            props.ContentType   <- "application/json"
            props.Timestamp     <- AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())

            try
                do! ch.BasicPublishAsync(exchange, routingKey, mandatory = false, basicProperties = props, body = body)
                     .AsTask()
                    |> Async.AwaitTask
            with ex ->
                eprintfn "  [RabbitMQ] publish failed for %s: %s" sv.LicensePlate ex.Message
    }

    member _.DisposeAsync() = async {
        match channel with
        | Some ch ->
            do! ch.CloseAsync() |> Async.AwaitTask
            ch.Dispose()
        | None -> ()
        match connection with
        | Some conn ->
            do! conn.CloseAsync() |> Async.AwaitTask
            conn.Dispose()
        | None -> ()
    }

    interface IAsyncDisposable with
        member this.DisposeAsync() =
            this.DisposeAsync() |> Async.StartAsTask |> ValueTask
