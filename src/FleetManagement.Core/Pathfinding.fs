module FleetManagement.Core.Pathfinding

open System
open System.Collections.Generic
open Domain
open Graph

// ============================================================
//  Shared Types
// ============================================================

type PathResult = {
    Path          : NodeId list
    TotalCost     : float       // minutes
    TotalDistanceKm: float
    AlgorithmUsed : PathfindingAlgorithm
    ComputedInMs  : int64
}

type PathError =
    | NodeNotFound  of NodeId
    | NoPathExists  of NodeId * NodeId
    | NegativeCycle of string
    | EmptyGraph

// ============================================================
//  Priority Queue (min-heap for Dijkstra and A*)
// ============================================================

type private MinHeap<'T when 'T : comparison>() =
    let heap = ResizeArray<float * 'T>()

    let swap i j =
        let tmp = heap.[i]
        heap.[i] <- heap.[j]
        heap.[j] <- tmp

    let rec bubbleUp i =
        if i > 0 then
            let parent = (i - 1) / 2
            if fst heap.[i] < fst heap.[parent] then
                swap i parent
                bubbleUp parent

    let rec bubbleDown i =
        let left  = 2 * i + 1
        let right = 2 * i + 2
        let mutable smallest = i
        if left  < heap.Count && fst heap.[left]  < fst heap.[smallest] then smallest <- left
        if right < heap.Count && fst heap.[right] < fst heap.[smallest] then smallest <- right
        if smallest <> i then
            swap i smallest
            bubbleDown smallest

    member _.Enqueue (priority: float, item: 'T) =
        heap.Add(priority, item)
        bubbleUp (heap.Count - 1)

    member _.Dequeue () =
        let top = heap.[0]
        let last = heap.Count - 1
        heap.[0] <- heap.[last]
        heap.RemoveAt last
        if heap.Count > 0 then bubbleDown 0
        top

    member _.IsEmpty = heap.Count = 0

// ============================================================
//  Helper: reconstruct path from predecessor map
// ============================================================

let private reconstructPath (prev: Dictionary<NodeId, NodeId option>) (start: NodeId) (goal: NodeId) =
    let rec build cur acc =
        match prev.TryGetValue cur with
        | true, Some p -> build p (cur :: acc)
        | true, None   -> start :: acc
        | _ -> []
    build goal []

// ============================================================
//  A* Algorithm  O((V + E) log V)
// ============================================================

module AStar =
    let find (graph: RoadGraph) (start: NodeId) (goal: NodeId) : Result<PathResult, PathError> =
        let sw = System.Diagnostics.Stopwatch.StartNew()

        match RoadGraph.tryGetNode start graph, RoadGraph.tryGetNode goal graph with
        | None, _ -> Error (NodeNotFound start)
        | _, None -> Error (NodeNotFound goal)
        | _ ->

        let gScore = Dictionary<NodeId, float>()
        let fScore = Dictionary<NodeId, float>()
        let prev   = Dictionary<NodeId, NodeId option>()
        let open_  = MinHeap<NodeId>()
        let closed = HashSet<NodeId>()

        gScore.[start] <- 0.0
        fScore.[start] <- RoadGraph.heuristic start goal graph
        prev.[start] <- None
        open_.Enqueue(fScore.[start], start)

        let mutable found = false
        let mutable result: Result<PathResult, PathError> = Error (NoPathExists (start, goal))

        while not open_.IsEmpty && not found do
            let (_, current) = open_.Dequeue()
            if current = goal then
                found <- true
                let path     = reconstructPath prev start goal
                let totalDist =
                    path |> List.pairwise
                    |> List.sumBy (fun (a, b) ->
                        RoadGraph.neighbors a graph
                        |> Seq.tryFind (fun e -> e.Target = b)
                        |> Option.map (fun e -> e.DistanceKm)
                        |> Option.defaultValue 0.0)
                sw.Stop()
                result <- Ok {
                    Path           = path
                    TotalCost      = gScore.[goal]
                    TotalDistanceKm= totalDist
                    AlgorithmUsed  = PathfindingAlgorithm.AStar
                    ComputedInMs   = sw.ElapsedMilliseconds
                }
            elif not (closed.Contains current) then
                closed.Add current |> ignore
                for edge in RoadGraph.neighbors current graph do
                    if not (closed.Contains edge.Target) then
                        let tentativeG = gScore.[current] + Edge.travelTimeMin edge
                        let prevG =
                            match gScore.TryGetValue edge.Target with
                            | true, v -> v
                            | _ -> Double.PositiveInfinity
                        if tentativeG < prevG then
                            gScore.[edge.Target] <- tentativeG
                            fScore.[edge.Target] <- tentativeG + RoadGraph.heuristic edge.Target goal graph
                            prev.[edge.Target]   <- Some current
                            open_.Enqueue(fScore.[edge.Target], edge.Target)

        result

// ============================================================
//  Dijkstra's Algorithm  O((V + E) log V)
// ============================================================

module Dijkstra =
    let find (graph: RoadGraph) (start: NodeId) (goal: NodeId) : Result<PathResult, PathError> =
        let sw = System.Diagnostics.Stopwatch.StartNew()

        match RoadGraph.tryGetNode start graph, RoadGraph.tryGetNode goal graph with
        | None, _ -> Error (NodeNotFound start)
        | _, None -> Error (NodeNotFound goal)
        | _ ->

        let dist   = Dictionary<NodeId, float>()
        let prev   = Dictionary<NodeId, NodeId option>()
        let pq     = MinHeap<NodeId>()
        let visited= HashSet<NodeId>()

        for kvp in graph.Nodes do
            dist.[kvp.Key] <- Double.PositiveInfinity
        dist.[start] <- 0.0
        prev.[start]  <- None
        pq.Enqueue(0.0, start)

        let mutable found = false
        let mutable result: Result<PathResult, PathError> = Error (NoPathExists (start, goal))

        while not pq.IsEmpty && not found do
            let (d, u) = pq.Dequeue()
            if u = goal then
                found <- true
                let path     = reconstructPath prev start goal
                let totalDist =
                    path |> List.pairwise
                    |> List.sumBy (fun (a, b) ->
                        RoadGraph.neighbors a graph
                        |> Seq.tryFind (fun e -> e.Target = b)
                        |> Option.map (fun e -> e.DistanceKm)
                        |> Option.defaultValue 0.0)
                sw.Stop()
                result <- Ok {
                    Path           = path
                    TotalCost      = d
                    TotalDistanceKm= totalDist
                    AlgorithmUsed  = PathfindingAlgorithm.Dijkstra
                    ComputedInMs   = sw.ElapsedMilliseconds
                }
            elif not (visited.Contains u) then
                visited.Add u |> ignore
                for edge in RoadGraph.neighbors u graph do
                    let alt = dist.[u] + Edge.travelTimeMin edge
                    let dv  =
                        match dist.TryGetValue edge.Target with
                        | true, v -> v
                        | _ -> Double.PositiveInfinity
                    if alt < dv then
                        dist.[edge.Target] <- alt
                        prev.[edge.Target] <- Some u
                        pq.Enqueue(alt, edge.Target)

        result

    /// Single-source shortest paths to ALL nodes
    let singleSource (graph: RoadGraph) (start: NodeId) : Dictionary<NodeId, float> =
        let dist   = Dictionary<NodeId, float>()
        let pq     = MinHeap<NodeId>()
        let visited= HashSet<NodeId>()

        for kvp in graph.Nodes do
            dist.[kvp.Key] <- Double.PositiveInfinity
        dist.[start] <- 0.0
        pq.Enqueue(0.0, start)

        while not pq.IsEmpty do
            let (d, u) = pq.Dequeue()
            if not (visited.Contains u) then
                visited.Add u |> ignore
                for edge in RoadGraph.neighbors u graph do
                    let alt = d + Edge.travelTimeMin edge
                    let dv  =
                        match dist.TryGetValue edge.Target with
                        | true, v -> v
                        | _ -> Double.PositiveInfinity
                    if alt < dv then
                        dist.[edge.Target] <- alt
                        pq.Enqueue(alt, edge.Target)
        dist

// ============================================================
//  Bellman-Ford Algorithm  O(V * E) — handles negative edges
// ============================================================

module BellmanFord =
    let find (graph: RoadGraph) (start: NodeId) (goal: NodeId) : Result<PathResult, PathError> =
        let sw = System.Diagnostics.Stopwatch.StartNew()

        match RoadGraph.tryGetNode start graph, RoadGraph.tryGetNode goal graph with
        | None, _ -> Error (NodeNotFound start)
        | _, None -> Error (NodeNotFound goal)
        | _ ->

        let nodes = graph.Nodes.Keys |> Seq.toArray
        let edges =
            graph.AdjList.Values
            |> Seq.concat
            |> Seq.toArray

        let dist = Dictionary<NodeId, float>()
        let prev = Dictionary<NodeId, NodeId option>()

        for n in nodes do
            dist.[n] <- Double.PositiveInfinity
            prev.[n] <- None
        dist.[start] <- 0.0

        // Relax |V| - 1 times
        for _ in 1 .. nodes.Length - 1 do
            for edge in edges do
                match dist.TryGetValue edge.Source with
                | true, du when du < Double.PositiveInfinity ->
                    let alt = du + Edge.travelTimeMin edge
                    let dv  =
                        match dist.TryGetValue edge.Target with
                        | true, v -> v
                        | _ -> Double.PositiveInfinity
                    if alt < dv then
                        dist.[edge.Target] <- alt
                        prev.[edge.Target] <- Some edge.Source
                | _ -> ()

        // Check for negative cycles
        let hasNegCycle =
            edges |> Array.exists (fun edge ->
                match dist.TryGetValue edge.Source with
                | true, du when du < Double.PositiveInfinity ->
                    let alt = du + Edge.travelTimeMin edge
                    let dv  =
                        match dist.TryGetValue edge.Target with
                        | true, v -> v
                        | _ -> Double.PositiveInfinity
                    alt < dv
                | _ -> false)

        if hasNegCycle then
            Error (NegativeCycle "Graph contains a negative-weight cycle")
        else
            match dist.TryGetValue goal with
            | true, d when d < Double.PositiveInfinity ->
                let path     = reconstructPath prev start goal
                let totalDist =
                    path |> List.pairwise
                    |> List.sumBy (fun (a, b) ->
                        RoadGraph.neighbors a graph
                        |> Seq.tryFind (fun e -> e.Target = b)
                        |> Option.map (fun e -> e.DistanceKm)
                        |> Option.defaultValue 0.0)
                sw.Stop()
                Ok {
                    Path           = path
                    TotalCost      = d
                    TotalDistanceKm= totalDist
                    AlgorithmUsed  = PathfindingAlgorithm.BellmanFord
                    ComputedInMs   = sw.ElapsedMilliseconds
                }
            | _ -> Error (NoPathExists (start, goal))

// ============================================================
//  Unified Path Planner
// ============================================================

module PathPlanner =
    let plan (algorithm: PathfindingAlgorithm) (graph: RoadGraph) (start: NodeId) (goal: NodeId) =
        match algorithm with
        | PathfindingAlgorithm.AStar       -> AStar.find graph start goal
        | PathfindingAlgorithm.Dijkstra    -> Dijkstra.find graph start goal
        | PathfindingAlgorithm.BellmanFord -> BellmanFord.find graph start goal

    /// Auto-select algorithm based on graph properties
    let autoSelect (graph: RoadGraph) (start: NodeId) (goal: NodeId) =
        if RoadGraph.hasNegativeEdges graph then
            BellmanFord.find graph start goal
        elif RoadGraph.nodeCount graph > 10_000 then
            AStar.find graph start goal         // Best for large sparse graphs
        else
            Dijkstra.find graph start goal      // Best for moderate dense graphs

    /// Plan a multi-stop route through waypoints (uses Dijkstra between pairs)
    let planMultiStop (algorithm: PathfindingAlgorithm) (graph: RoadGraph) (waypoints: NodeId list) =
        match waypoints with
        | [] | [_] -> Error EmptyGraph
        | _ ->
            waypoints
            |> List.pairwise
            |> List.fold (fun acc (src, tgt) ->
                match acc with
                | Error e -> Error e
                | Ok results ->
                    match plan algorithm graph src tgt with
                    | Ok r  -> Ok (results @ [r])
                    | Error e -> Error e
            ) (Ok [])
            |> Result.map (fun segments ->
                let fullPath =
                    segments
                    |> List.mapi (fun i s ->
                        if i = 0 then s.Path
                        else s.Path |> List.tail)
                    |> List.concat
                {
                    Path           = fullPath
                    TotalCost      = segments |> List.sumBy (fun s -> s.TotalCost)
                    TotalDistanceKm= segments |> List.sumBy (fun s -> s.TotalDistanceKm)
                    AlgorithmUsed  = algorithm
                    ComputedInMs   = segments |> List.sumBy (fun s -> s.ComputedInMs)
                })
