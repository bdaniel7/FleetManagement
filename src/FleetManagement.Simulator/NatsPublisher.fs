module FleetManagement.Simulator.NatsPublisher

open System
open System.Text.Json
open System.Threading.Tasks
open NATS.Client.Core
open NATS.Client.JetStream
open Types
open FleetManagement.API.Messaging

let private jsonOpts =
    let o = JsonSerializerOptions()
    o.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    o

// ── Publisher state ────────────────────────────────────────────

type NatsPublisher(natsUrl: string) =

    let natsOpts = NatsOpts(Url = natsUrl,
                            WebSocketOpts = NatsWebSocketOpts.Default,
                            TlsOpts = NatsTlsOpts.Default,
                            AuthOpts = NatsAuthOpts.Default)
    let mutable connection: NatsConnection option = None
    let mutable js: NatsJSContext option = None

    member _.ConnectAsync() = async {
        let conn = NatsConnection(natsOpts)
        do! conn.ConnectAsync().AsTask() |> Async.AwaitTask
        let jsCtx = NatsJSContext(conn)
        connection <- Some conn
        js <- Some jsCtx
        printfn "  ✓ Connected to NATS at %s" natsUrl
    }

    member _.PublishTelemetry (sv: SimVehicle) = async {
        match js with
        | None -> ()   // not connected — silently skip
        | Some jsCtx ->
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
            let subject = $"fleet.telemetry.{sv.Id}"
            let payload = JsonSerializer.SerializeToUtf8Bytes(msg, jsonOpts)
            try
                let! _ = jsCtx.PublishAsync(subject, payload).AsTask() |> Async.AwaitTask
                ()
            with ex ->
                eprintfn "  [NATS] publish failed for %s: %s" sv.LicensePlate ex.Message
    }

    member _.DisposeAsync() = async {
        match connection with
        | Some conn ->
            do! conn.DisposeAsync().AsTask() |> Async.AwaitTask
        | None -> ()
    }

    interface IAsyncDisposable with
        member this.DisposeAsync() =
            this.DisposeAsync() |> Async.StartAsTask |> ValueTask
