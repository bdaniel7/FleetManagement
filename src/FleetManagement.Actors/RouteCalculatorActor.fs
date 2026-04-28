module FleetManagement.Actors.RouteCalculatorActor

open System
open Akka.Event
open Akka.FSharp
open Akka.Actor
open FleetManagement.Core.Domain
open FleetManagement.Core.Graph
open FleetManagement.Core.Pathfinding
open ActorMessages

// ============================================================
//  State
// ============================================================

type private CalcState = {
    RoadGraph   : RoadGraph
    Processed   : int
    TotalCostMs : int64
}

// ============================================================
//  Coordinate → NodeId resolution
// ============================================================

/// Finds the nearest node in the graph to a geo-coordinate.
let private nearestNode (coord: GeoCoordinate) (graph: RoadGraph) : NodeId option =
    if graph.Nodes.Count = 0 then None
    else
        graph.Nodes
        |> Seq.minBy (fun kvp -> GeoCoordinate.distanceKm coord kvp.Value.Coordinate)
        |> fun kvp -> Some kvp.Key

// ============================================================
//  Helpers — safely extract inner string from struct DUs
//  DO NOT use .ToString() or $"{nodeId}" on [<Struct>] DUs —
//  F#'s reflection pretty-printer crashes on .NET 10 for structs.
// ============================================================

let inline private nodeIdStr (NodeId s) = s

// ============================================================
//  Route builder helpers
// ============================================================

let private nodeToCoordinate (nodeId: NodeId) (graph: RoadGraph) : GeoCoordinate =
    match graph.Nodes.TryGetValue(nodeId) with
    | true, node -> node.Coordinate
    | false, _ -> { Latitude = 0.0; Longitude = 0.0 }

let private buildRoute (req: RouteRequest) (pathResult: PathResult) (graph: RoadGraph) : Route =
    let pathCoords = pathResult.Path |> List.map (fun nid -> nodeToCoordinate nid graph)
    let waypoints =
        pathResult.Path
        |> List.mapi (fun i nodeId ->
            { NodeId         = nodeId
              Coordinate     = pathCoords.[i]
              Address        = nodeIdStr nodeId
              ArrivalTime    = None
              DepartureTime  = None
              StopDurationMin= 0 })
    {
        Id                   = RouteId (Guid.NewGuid())
        VehicleId            = req.VehicleId
        DriverId             = req.DriverId
        Waypoints            = waypoints
        OptimizedPath        = pathCoords
        TotalDistanceKm      = pathResult.TotalDistanceKm
        EstimatedDurationMin = int (pathResult.TotalCost)
        Status               = RouteStatus.Planned
        Priority             = req.Priority
        Algorithm            = req.Algorithm
        CreatedAt            = DateTimeOffset.UtcNow
        StartedAt            = None
        CompletedAt          = None
    }

// ============================================================
//  Actor
// ============================================================

let routeCalculatorActor
    (mailbox: Actor<RouteCalculatorMessage>) =

    let log = mailbox.Context.GetLogger()

    let rec loop (state: CalcState) = actor {
        let! msg = mailbox.Receive()

        match msg with
        | UpdateRoadGraph graph ->
            log.Info("Road graph updated: {Nodes} nodes", graph.Nodes.Count)
            return! loop { state with RoadGraph = graph }

        | GetGraphStats ->
            mailbox.Sender().Tell({|
                NodeCount = state.RoadGraph.Nodes.Count
                EdgeCount = state.RoadGraph.EdgeCount
                ProcessedRequests = state.Processed
                AvgComputeMs = if state.Processed > 0 then state.TotalCostMs / int64 state.Processed else 0L
            |})
            return! loop state

        | ComputeRoute req ->
            let graph = state.RoadGraph
            if graph.Nodes.Count = 0 then
                req.ReplyTo.Tell(RouteError (req.RequestId, "Road graph is not loaded"))
                return! loop state
            else
                // Resolve geo-coordinates to node IDs
                match req.Coordinates with
                | [] | [_] ->
                    req.ReplyTo.Tell(RouteError (req.RequestId, "At least 2 waypoints required"))
                    return! loop state
                | waypoints ->
                    let nodeIds =
                        waypoints
                        |> List.choose (fun coord -> nearestNode coord graph)

                    if nodeIds.Length < 2 then
                        req.ReplyTo.Tell(RouteError (req.RequestId, "Could not resolve waypoints to graph nodes"))
                        return! loop state
                    else
                        let planResult =
                            PathPlanner.planMultiStop req.Algorithm graph nodeIds

                        match planResult with
                        | Ok pathResult ->
                            log.Info("Route computed: {Nodes} nodes, {Dist}km, {Time}min via {Algo} in {Ms}ms",
                                     pathResult.Path.Length, pathResult.TotalDistanceKm,
                                     int pathResult.TotalCost, req.Algorithm, pathResult.ComputedInMs)
                            let routeId = RouteId (Guid.NewGuid())
                            let route   = buildRoute req pathResult graph
                            let route'  = { route with Id = routeId }
                            req.ReplyTo.Tell(RouteComputed (routeId, route'))
                            let next = {
                                state with
                                    Processed   = state.Processed + 1
                                    TotalCostMs = state.TotalCostMs + pathResult.ComputedInMs
                            }
                            return! loop next

                        | Error (PathError.NoPathExists (s, t)) ->
                            let sStr = nodeIdStr s
                            let tStr = nodeIdStr t
                            req.ReplyTo.Tell(RouteError (req.RequestId, $"No path from {sStr} to {tStr}"))
                            return! loop state

                        | Error (PathError.NegativeCycle msg) ->
                            req.ReplyTo.Tell(RouteError (req.RequestId, $"Negative cycle detected: {msg}"))
                            return! loop state

                        | Error (PathError.NodeNotFound nid) ->
                            let nidStr = nodeIdStr nid
                            req.ReplyTo.Tell(RouteError (req.RequestId, $"Node not found: {nidStr}"))
                            return! loop state

                        | Error PathError.EmptyGraph ->
                            req.ReplyTo.Tell(RouteError (req.RequestId, "Graph is empty"))
                            return! loop state
    }

    loop { RoadGraph = RoadGraph.buildSampleGraph(); Processed = 0; TotalCostMs = 0L }

// ============================================================
//  Pool of route calculator actors (for parallel pathfinding)
// ============================================================

let spawnPool (system: ActorSystem) (poolSize: int) : IActorRef =
    // let routerProps =
    //     Props.Create(fun () ->
    //         { new UntypedActor() with
    //             override _.OnReceive _ = () })
    // // Use a round-robin pool router
    // let props =
    //     Props.Create<UntypedActor>()
    //     |> fun _ ->
    //         Props.Create(routeCalculatorActor)

    // Spawn pool of workers
    let workers =
        [1..poolSize]
        |> List.map (fun i ->
            spawn system $"route-calculator-{i}" routeCalculatorActor)
            // system.ActorOf(
            //     Props.Create(routeCalculatorActor),
            //     $"route-calculator-{i}"))

    // Return first worker (in production: use cluster sharding / router)
    workers.[0]
