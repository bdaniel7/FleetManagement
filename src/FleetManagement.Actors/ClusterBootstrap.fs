module FleetManagement.Actors.ClusterBootstrap

open System
open System.Diagnostics
open Akka.Actor
open Akka.Configuration
open Akka.Cluster
open Akka.FSharp
open FleetManagement.Core.Events
open ActorMessages
open FleetManagement.Actors.Tracing

// ============================================================
//  HOCON Configuration builder
// ============================================================

let buildConfig (hostname: string) (port: int) (seedNodes: string list) =
    let seeds =
        seedNodes
        |> List.map (fun s -> $"\"akka.tcp://fleet-cluster@{s}\"")
        |> String.concat ", "

    ConfigurationFactory.ParseString $"""
akka {{
  actor {{
    provider = cluster
    serializers {{
      hyperion = "Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion"
    }}
    serialization-bindings {{
      "System.Object" = hyperion
    }}
  }}

  remote {{
    dot-netty.tcp {{
      hostname = "{hostname}"
      port     = {port}
      maximum-frame-size = 4000000b
      send-buffer-size   = 256000b
      receive-buffer-size= 256000b
    }}
  }}

  cluster {{
    name         = fleet-cluster
    seed-nodes   = [{seeds}]
    min-nr-of-members = 1

    roles = ["fleet-node"]

    downing-provider-class = "Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster"
    split-brain-resolver {{
        active-strategy = keep-majority
    }}

    failure-detector {{
      threshold              = 10.0
      heartbeat-interval     = 1s
      acceptable-heartbeat-pause = 5s
    }}

    sharding {{
      state-store-mode = ddata
      number-of-shards = 100
    }}

  }}

  persistence {{
    journal.plugin         = "akka.persistence.journal.inmem"
    snapshot-store.plugin  = "akka.persistence.snapshot-store.local"
    snapshot-store.local.dir = "snapshots"
  }}

  streams {{
    materializer {{
      dispatcher = "akka.actor.default-dispatcher"
      subscription-timeout {{
        mode    = cancel
        timeout = 5s
      }}
    }}
  }}

  loggers              = ["Akka.Event.DefaultLogger"]
  loglevel             = INFO
  log-dead-letters     = 10
  log-dead-letters-during-shutdown = off
}}
"""

// ============================================================
//  ActorSystem + all top-level actors
// ============================================================

type FleetActorSystem = {
    System            : ActorSystem
    FleetSupervisor   : IActorRef
    RouteCalculator   : IActorRef
    TelemetryActor    : IActorRef
}

let start
    (hostname       : string)
    (port           : int)
    (seedNodes      : string list)
    (broadcastEvent : DomainEvent -> unit)
    (broadcastTelemetry : TelemetryEvent -> unit) : FleetActorSystem =

    let config = buildConfig hostname port seedNodes
    let system = ActorSystem.Create("fleet-cluster", config)

    let cluster = Cluster.Get(system)
    cluster.RegisterOnMemberUp(fun () ->
        match tryStartActivity "Cluster.MemberAdded" ActivityKind.Internal with
        | Some activity ->
            let ac = activity.AddTag("cluster.address", string cluster.SelfAddress)
            ac.Dispose()
        | None -> ()
        printfn "✅ Node joined cluster. Self address: %A" cluster.SelfAddress)
    cluster.RegisterOnMemberRemoved(fun () ->
        match tryStartActivity "Cluster.MemberRemoved" ActivityKind.Internal with
        | Some activity ->
            let ac = activity.AddTag("cluster.address", string cluster.SelfAddress)
            ac.Dispose()
        | None -> ()
        printfn "⚠️  Node left cluster")


    // Use Akka.FSharp.spawn — NOT system.ActorOf(Props.props ...) — so that
    // FunActor<'Msg> is instantiated correctly without reflection on ActorBase.
    let routeCalc =
        spawn system "route-calculator"
            RouteCalculatorActor.routeCalculatorActor

    let telemetry =
        spawn system "telemetry-stream"
            (TelemetryStreamActor.telemetryStreamActor broadcastTelemetry)

    telemetry.Tell StartStream

    let supervisor =
        FleetSupervisorActor.spawn system routeCalc telemetry broadcastEvent

    {
        System          = system
        FleetSupervisor = supervisor
        RouteCalculator = routeCalc
        TelemetryActor  = telemetry
    }

let stop (fas: FleetActorSystem) =
    fas.TelemetryActor.Tell StopStream
    fas.System.Terminate().Wait(TimeSpan.FromSeconds 10.0) |> ignore
