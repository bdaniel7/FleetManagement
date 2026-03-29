module FleetManagement.Actors.TelemetryStreamActor

open System
open System.Collections.Generic
open Akka
open Akka.Actor
open Akka.Event
open Akka.FSharp
open Akka.Streams
open Akka.Streams.Dsl
open FleetManagement.Core.Domain
open ActorMessages

// ============================================================
//  In-memory telemetry ring buffer per vehicle
// ============================================================

type private RingBuffer<'T>(capacity: int) =
    let buf = Array.zeroCreate<'T> capacity
    let mutable head  = 0
    let mutable count = 0

    member _.Push(item: 'T) =
        buf.[head % capacity] <- item
        head <- head + 1
        if count < capacity then count <- count + 1

    member _.ToArray() =
        if count = 0 then [||]
        else
            let start = if count < capacity then 0 else head % capacity
            Array.init count (fun i -> buf.[(start + i) % capacity])

    member _.Count = count

type private VehicleTelemetryBuffer = {
    Buffer   : RingBuffer<TelemetryEvent>
    Alerts   : ResizeArray<string>
}

module private VehicleTelemetryBuffer =
    let create () = {
        Buffer = RingBuffer<TelemetryEvent>(500)
        Alerts = ResizeArray<string>()
    }

    let stats (vehicleId: VehicleId) (b: VehicleTelemetryBuffer) : TelemetryStats =
        let events = b.Buffer.ToArray()
        if events.Length = 0 then
            { VehicleId   = vehicleId
              EventCount  = 0
              AvgSpeedKmh = 0.0
              AvgFuelPct  = 0.0
              LastReceived= None
              Alerts      = [] }
        else
            { VehicleId   = vehicleId
              EventCount  = events.Length
              AvgSpeedKmh = events |> Array.averageBy (fun e -> e.SpeedKmh)
              AvgFuelPct  = events |> Array.averageBy (fun e -> e.FuelPct)
              LastReceived= events |> Array.map (fun e -> e.Timestamp) |> Array.max |> Some
              Alerts      = b.Alerts |> Seq.toList }


    let push (ev: TelemetryEvent) (b: VehicleTelemetryBuffer) : VehicleTelemetryBuffer =
       b.Buffer.Push ev
       // Rule-based alerting
       if ev.SpeedKmh > 120.0 then
           b.Alerts.Add $"[{ev.Timestamp}] Speed alert: {ev.SpeedKmh:F1} km/h"
       if ev.FuelPct < 10.0 then
           b.Alerts.Add $"[{ev.Timestamp}] Critical fuel: {ev.FuelPct:F1}%%"
       if ev.EngineTemp > 110.0 then
           b.Alerts.Add $"[{ev.Timestamp}] Engine overheat: {ev.EngineTemp:F1}°C"
           // :HH:mm:ss
       b


// ============================================================
//  Akka.Streams telemetry pipeline
// ============================================================

type private StreamState = {
    Buffers       : Dictionary<VehicleId, VehicleTelemetryBuffer>
    Materializer  : ActorMaterializer option
    StreamRunning : bool
    TotalIngested : int64
}

let telemetryStreamActor
    (broadcastToHub: TelemetryEvent -> unit)
    (mailbox: Actor<TelemetryStreamMessage>) =

    let log           = mailbox.Context.GetLogger()
    let system        = mailbox.Context.System
    let mutable mat : ActorMaterializer = Unchecked.defaultof<_>

    // Build a stream pipeline: Source queue → throttle → broadcast → persist + forward
    let buildStream () =
        mat <- ActorMaterializer.Create(system)

       // SourceQueue → throttle to 1000/sec → foreach sink
        let (queue, _) =
            Source.Queue<TelemetryEvent>(1024, OverflowStrategy.DropHead)
                .Throttle(1000, TimeSpan.FromSeconds 1.0, 1000, ThrottleMode.Shaping)
                .ToMaterialized(
                    Sink.ForEach<TelemetryEvent>(fun ev ->
                        broadcastToHub ev
                        mailbox.Self.Tell(IngestTelemetry ev)),
                    fun left _ -> left)
                .Run(mat)
            |> fun q -> q, ()
        queue

    let rec loop (state: StreamState) = actor {
        let! msg = mailbox.Receive()

        match msg with
        | StartStream when not state.StreamRunning ->
            log.Info("Telemetry stream pipeline starting")
            buildStream() |> ignore
            return! loop { state with StreamRunning = true }

        | StartStream -> return! loop state

        | StopStream ->
            log.Info("Telemetry stream stopping. Total events: {Count}", state.TotalIngested)
            if not (obj.ReferenceEquals(mat, null)) then mat.Dispose()
            return! loop { state with StreamRunning = false }

        | IngestTelemetry ev ->
            let buf =
                if state.Buffers.ContainsKey ev.VehicleId then
                    state.Buffers.[ev.VehicleId]
                else
                    let b = VehicleTelemetryBuffer.create()
                    state.Buffers.[ev.VehicleId] <- b
                    b
            state.Buffers.[ev.VehicleId] <- VehicleTelemetryBuffer.push ev buf
            return! loop { state with TotalIngested = state.TotalIngested + 1L }

        | GetTelemetryStats vehicleId ->
            let stats =
                match state.Buffers.TryGetValue vehicleId with
                | true, buf -> VehicleTelemetryBuffer.stats vehicleId buf
                | _         ->
                    { VehicleId   = vehicleId
                      EventCount  = 0
                      AvgSpeedKmh = 0.0
                      AvgFuelPct  = 0.0
                      LastReceived= None
                      Alerts      = [] }
            mailbox.Sender().Tell(stats)
            return! loop state

        | PurgeTelemetry (vehicleId, _olderThan) ->
            state.Buffers.Remove vehicleId |> ignore
            log.Info("Telemetry purged for vehicle {VehicleId}", vehicleId)
            return! loop state
    }

    loop {
        Buffers       = Dictionary()
        Materializer  = None
        StreamRunning = false
        TotalIngested = 0L
    }
