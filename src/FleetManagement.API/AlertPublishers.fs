module FleetManagement.API.AlertPublishers

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.SignalR
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open RabbitMQ.Client
open FleetManagement.Core.Domain
open FleetManagement.API.Hubs.TelemetryHub

type AlertInfo = {
    AlertId: Guid
    Message: string
    Priority: Priority
    VehicleId: Guid option
    RaisedAt: DateTimeOffset
}

type IAlertPublisher =
    abstract PublishAlert : AlertInfo -> Task

type RabbitMqAlertPublisher(config: IConfiguration, log: ILogger<RabbitMqAlertPublisher>) =
    let enabled = config.["RabbitMq:Enabled"] |> bool.TryParse |> snd
    let host = config.["RabbitMq:Host"] |> Option.ofObj |> Option.defaultValue "localhost"
    let port = config.["RabbitMq:Port"] |> Option.ofObj |> Option.map int |> Option.defaultValue 5672
    let username = config.["RabbitMq:Username"] |> Option.ofObj |> Option.defaultValue "guest"
    let password = config.["RabbitMq:Password"] |> Option.ofObj |> Option.defaultValue "guest"
    let exchange = config.["RabbitMq:Exchange"] |> Option.ofObj |> Option.defaultValue "fleet.alerts"
    let routingKey = config.["RabbitMq:RoutingKey"] |> Option.ofObj |> Option.defaultValue "fleet.alert.lowfuel"

    let mutable connection: IConnection option = None
    let mutable channel: IChannel option = None

    let ensureConnected() =
        if enabled && connection.IsNone then
            try
                let factory = ConnectionFactory()
                factory.HostName <- host
                factory.Port <- port
                factory.UserName <- username
                factory.Password <- password
                factory.AutomaticRecoveryEnabled <- true
                let conn = factory.CreateConnectionAsync().GetAwaiter().GetResult()
                connection <- Some conn
                channel <- Some (conn.CreateChannelAsync().GetAwaiter().GetResult())
                channel.Value.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable = true)
                    .GetAwaiter().GetResult()
                log.LogInformation("Connected to RabbitMQ at {Host}:{Port}", host, port)
            with ex ->
                log.LogError(ex, "Failed to connect to RabbitMQ")

    interface IAlertPublisher with
        member _.PublishAlert(alert: AlertInfo) =
            task {
                if not enabled then () else
                ensureConnected()
                match channel with
                | Some ch ->
                    try
                        let json = JsonSerializer.Serialize alert
                        let body = System.Text.Encoding.UTF8.GetBytes json
                        let props = BasicProperties()
                        props.ContentType <- "application/json"
                        props.DeliveryMode <- DeliveryModes.Persistent
                        let addr = PublicationAddress(ExchangeType.Topic, exchange, routingKey)
                        do! ch.BasicPublishAsync(addr, props, body.AsMemory())
                        log.LogDebug("Published alert to RabbitMQ: {Message}", alert.Message)
                    with ex ->
                        log.LogError(ex, "Failed to publish alert to RabbitMQ")
                | None -> ()
            }

    interface IDisposable with
        member _.Dispose() =
            channel |> Option.iter _.Dispose()
            connection |> Option.iter _.Dispose()

type SignalRAlertPublisher(hub: IHubContext<TelemetryHub>, log: ILogger<SignalRAlertPublisher>) =

    interface IAlertPublisher with
        member _.PublishAlert(alert: AlertInfo) =
            task {
                try
                    let level, priorityStr = 
                        match alert.Priority with 
                        | Priority.Emergency -> ("critical", "Emergency")
                        | Priority.High -> ("warning", "High") 
                        | Priority.Normal -> ("info", "Normal") 
                        | Priority.Low -> ("info", "Low")
                    let payload = {|
                        alertId   = alert.AlertId
                        message   = alert.Message
                        priority  = priorityStr
                        vehicleId = alert.VehicleId
                        timestamp = alert.RaisedAt
                        level     = level
                    |}
                    let clients = hub.Clients.All
                    do! clients.SendAsync(OnAlert, payload)
                    log.LogDebug("Alert published via SignalR: {Message}", alert.Message)
                with ex ->
                    log.LogError(ex, "Failed to publish alert via SignalR")
            }
