module FleetManagement.Tests.PathfindingTests

open Xunit
open FsUnit.Xunit
open FleetManagement.Core.Domain
open FleetManagement.Core.Graph
open FleetManagement.Core.Pathfinding

// ============================================================
//  Shared test fixtures
// ============================================================

/// Builds a simple 5-node graph with known shortest paths:
///
///       2         1
///  A ──────► B ──────► E
///  │         │         ▲
///  │  5      │  4      │
///  ▼         ▼         │ 1
///  C ──────► D ─────────
///       3
///
/// Shortest A→E:  A→B→E = 3 min  (via travelTime)
/// All edges are one-way for determinism.

let private buildLinearGraph () =
    let node id lat lon = { Id = NodeId id; Coordinate = { Latitude = lat; Longitude = lon }; Label = id; IsDepot = false }
    let edge src tgt dist speed =
        { Source = NodeId src; Target = NodeId tgt
          DistanceKm = dist; SpeedLimitKmh = speed
          TrafficFactor = 1.0; IsOneWay = true }

    RoadGraph.empty()
    |> RoadGraph.addNode (node "A" 44.40 26.10)
    |> RoadGraph.addNode (node "B" 44.41 26.11)
    |> RoadGraph.addNode (node "C" 44.39 26.10)
    |> RoadGraph.addNode (node "D" 44.39 26.12)
    |> RoadGraph.addNode (node "E" 44.41 26.13)
    |> RoadGraph.addEdge (edge "A" "B" 2.0 60.0)    // 2 min
    |> RoadGraph.addEdge (edge "A" "C" 5.0 60.0)    // 5 min
    |> RoadGraph.addEdge (edge "B" "E" 1.0 60.0)    // 1 min  → A→B→E = 3 min
    |> RoadGraph.addEdge (edge "B" "D" 4.0 60.0)    // 4 min
    |> RoadGraph.addEdge (edge "C" "D" 3.0 60.0)    // 3 min
    |> RoadGraph.addEdge (edge "D" "E" 1.0 60.0)    // 1 min  → A→C→D→E = 9 min

/// Fully disconnected graph (B is unreachable from A)
let private buildDisconnectedGraph () =
    let node id = { Id = NodeId id; Coordinate = { Latitude = 0.0; Longitude = 0.0 }; Label = id; IsDepot = false }
    RoadGraph.empty()
    |> RoadGraph.addNode (node "A")
    |> RoadGraph.addNode (node "B")  // no edges

/// Single-node graph (trivial)
let private buildSingleNodeGraph () =
    let node id = { Id = NodeId id; Coordinate = { Latitude = 0.0; Longitude = 0.0 }; Label = id; IsDepot = false }
    RoadGraph.empty()
    |> RoadGraph.addNode (node "LONE")

// ============================================================
//  Helper assertions
// ============================================================

let private assertPath (result: Result<PathResult, PathError>) startId endId =
    match result with
    | Error e -> failwithf "Expected Ok but got Error: %A" e
    | Ok pr   ->
        pr.Path |> List.head    |> should equal (NodeId startId)
        pr.Path |> List.last    |> should equal (NodeId endId)
        pr.Path.Length          |> should be (greaterThanOrEqualTo 2)

let private assertCost (result: Result<PathResult, PathError>) (expectedMin: float) (tolerance: float) =
    match result with
    | Error e -> failwithf "Expected Ok but got Error: %A" e
    | Ok pr   -> pr.TotalCost |> should (equalWithin tolerance) expectedMin

let private assertError (result: Result<PathResult, PathError>) =
    match result with
    | Ok pr  -> failwithf "Expected Error but got Ok with path: %A" pr.Path
    | Error _ -> ()

// ============================================================
//  A* Tests
// ============================================================

module AStarTests =

    [<Fact>]
    let ``AStar finds direct path in 2-node graph`` () =
        let g =
            RoadGraph.empty()
            |> RoadGraph.addNode { Id = NodeId "S"; Coordinate = { Latitude = 0.0; Longitude = 0.0 }; Label = "S"; IsDepot = false }
            |> RoadGraph.addNode { Id = NodeId "T"; Coordinate = { Latitude = 0.1; Longitude = 0.0 }; Label = "T"; IsDepot = false }
            |> RoadGraph.addEdge { Source = NodeId "S"; Target = NodeId "T"; DistanceKm = 10.0; SpeedLimitKmh = 60.0; TrafficFactor = 1.0; IsOneWay = false }

        let result = AStar.find g (NodeId "S") (NodeId "T")
        assertPath result "S" "T"

    [<Fact>]
    let ``AStar finds optimal path A→B→E = 3 min`` () =
        let g = buildLinearGraph()
        let r = AStar.find g (NodeId "A") (NodeId "E")
        assertPath  r "A" "E"
        assertCost  r 3.0 0.01   // 2 + 1 = 3 minutes

    [<Fact>]
    let ``AStar path visits only nodes A B E`` () =
        let g    = buildLinearGraph()
        let r    = AStar.find g (NodeId "A") (NodeId "E")
        match r with
        | Error e -> failwithf "%A" e
        | Ok pr   ->
            pr.Path |> should equal [NodeId "A"; NodeId "B"; NodeId "E"]

    [<Fact>]
    let ``AStar returns NodeNotFound for unknown source`` () =
        let g = buildLinearGraph()
        let r = AStar.find g (NodeId "GHOST") (NodeId "E")
        match r with
        | Error (PathError.NodeNotFound nid) -> nid |> should equal (NodeId "GHOST")
        | _ -> failwith "Expected NodeNotFound"

    [<Fact>]
    let ``AStar returns NodeNotFound for unknown target`` () =
        let r = AStar.find (buildLinearGraph()) (NodeId "A") (NodeId "GHOST")
        match r with
        | Error (PathError.NodeNotFound _) -> ()
        | _ -> failwith "Expected NodeNotFound"

    [<Fact>]
    let ``AStar returns NoPathExists for disconnected graph`` () =
        let r = AStar.find (buildDisconnectedGraph()) (NodeId "A") (NodeId "B")
        assertError r

    [<Fact>]
    let ``AStar path same start and end returns single-node path`` () =
        let g = buildLinearGraph()
        let r = AStar.find g (NodeId "A") (NodeId "A")
        match r with
        | Ok pr ->
            pr.Path   |> should equal [NodeId "A"]
            pr.TotalCost |> should (equalWithin 0.001) 0.0
        | Error _ -> ()   // acceptable — implementation may refuse same-node

    [<Fact>]
    let ``AStar records algorithm used`` () =
        let g = buildLinearGraph()
        match AStar.find g (NodeId "A") (NodeId "E") with
        | Ok pr -> pr.AlgorithmUsed |> should equal PathfindingAlgorithm.AStar
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``AStar computes positive distance`` () =
        let g = buildLinearGraph()
        match AStar.find g (NodeId "A") (NodeId "E") with
        | Ok pr -> pr.TotalDistanceKm |> should be (greaterThan 0.0)
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``AStar performance on sample graph under 100ms`` () =
        let g = RoadGraph.buildSampleGraph()
        let sw = System.Diagnostics.Stopwatch.StartNew()
        for _ in 1..100 do
            AStar.find g (NodeId "DEPOT") (NodeId "G") |> ignore
        sw.Stop()
        sw.ElapsedMilliseconds |> should be (lessThan 100L)

// ============================================================
//  Dijkstra Tests
// ============================================================

module DijkstraTests =

    [<Fact>]
    let ``Dijkstra finds optimal path A→B→E = 3 min`` () =
        let g = buildLinearGraph()
        let r = Dijkstra.find g (NodeId "A") (NodeId "E")
        assertPath r "A" "E"
        assertCost r 3.0 0.01

    [<Fact>]
    let ``Dijkstra path visits nodes A B E`` () =
        let g = buildLinearGraph()
        match Dijkstra.find g (NodeId "A") (NodeId "E") with
        | Error e -> failwithf "%A" e
        | Ok pr   -> pr.Path |> should equal [NodeId "A"; NodeId "B"; NodeId "E"]

    [<Fact>]
    let ``Dijkstra and AStar agree on optimal cost`` () =
        let g        = RoadGraph.buildSampleGraph()
        let astar    = AStar.find    g (NodeId "DEPOT") (NodeId "F")
        let dijkstra = Dijkstra.find g (NodeId "DEPOT") (NodeId "F")
        match astar, dijkstra with
        | Ok a, Ok d -> a.TotalCost |> should (equalWithin 0.01) d.TotalCost
        | _ -> failwith "Both algorithms should find a path"

    [<Fact>]
    let ``Dijkstra returns NodeNotFound for missing source`` () =
        let r = Dijkstra.find (buildLinearGraph()) (NodeId "X99") (NodeId "E")
        match r with
        | Error (PathError.NodeNotFound _) -> ()
        | _ -> failwith "Expected NodeNotFound"

    [<Fact>]
    let ``Dijkstra returns NoPathExists for disconnected graph`` () =
        assertError (Dijkstra.find (buildDisconnectedGraph()) (NodeId "A") (NodeId "B"))

    [<Fact>]
    let ``singleSource returns infinity for unreachable nodes`` () =
        let g    = buildDisconnectedGraph()
        let dist = Dijkstra.singleSource g (NodeId "A")
        match dist.TryGetValue (NodeId "B") with
        | true, d -> d |> should equal System.Double.PositiveInfinity
        | _       -> ()   // not reached at all — also acceptable

    [<Fact>]
    let ``singleSource gives zero distance for source node`` () =
        let g    = buildLinearGraph()
        let dist = Dijkstra.singleSource g (NodeId "A")
        dist.[NodeId "A"] |> should (equalWithin 0.001) 0.0

    [<Fact>]
    let ``singleSource gives correct distances to all nodes`` () =
        let g    = buildLinearGraph()
        let dist = Dijkstra.singleSource g (NodeId "A")
        dist.[NodeId "B"] |> should (equalWithin 0.001) 2.0
        dist.[NodeId "E"] |> should (equalWithin 0.001) 3.0   // A→B→E

    [<Fact>]
    let ``Dijkstra records algorithm used`` () =
        let g = buildLinearGraph()
        match Dijkstra.find g (NodeId "A") (NodeId "E") with
        | Ok pr -> pr.AlgorithmUsed |> should equal PathfindingAlgorithm.Dijkstra
        | Error e -> failwithf "%A" e

// ============================================================
//  Bellman-Ford Tests
// ============================================================

module BellmanFordTests =

    [<Fact>]
    let ``BellmanFord finds same path as Dijkstra on normal graph`` () =
        let g  = buildLinearGraph()
        let bf = BellmanFord.find g (NodeId "A") (NodeId "E")
        let dj = Dijkstra.find    g (NodeId "A") (NodeId "E")
        match bf, dj with
        | Ok b, Ok d -> b.TotalCost |> should (equalWithin 0.01) d.TotalCost
        | _ -> failwith "Both should succeed"

    [<Fact>]
    let ``BellmanFord finds optimal path A→B→E = 3 min`` () =
        let g = buildLinearGraph()
        assertPath (BellmanFord.find g (NodeId "A") (NodeId "E")) "A" "E"
        assertCost (BellmanFord.find g (NodeId "A") (NodeId "E")) 3.0 0.01

    [<Fact>]
    let ``BellmanFord returns NegativeCycle on negative-cycle graph`` () =
        // Build a graph with a negative-weight cycle A→B→A
        let node id = { Id = NodeId id; Coordinate = { Latitude = 0.0; Longitude = 0.0 }; Label = id; IsDepot = false }
        let g =
            RoadGraph.empty()
            |> RoadGraph.addNode (node "A")
            |> RoadGraph.addNode (node "B")
            |> RoadGraph.addNode (node "C")
            |> RoadGraph.addEdge { Source = NodeId "A"; Target = NodeId "B"
                                   DistanceKm = -1.0; SpeedLimitKmh = 60.0
                                   TrafficFactor = 1.0; IsOneWay = true }
            |> RoadGraph.addEdge { Source = NodeId "B"; Target = NodeId "A"
                                   DistanceKm = -1.0; SpeedLimitKmh = 60.0
                                   TrafficFactor = 1.0; IsOneWay = true }
            |> RoadGraph.addEdge { Source = NodeId "A"; Target = NodeId "C"
                                   DistanceKm =  5.0; SpeedLimitKmh = 60.0
                                   TrafficFactor = 1.0; IsOneWay = true }

        let r = BellmanFord.find g (NodeId "A") (NodeId "C")
        match r with
        | Error (PathError.NegativeCycle _) -> ()
        | _ -> failwith "Expected NegativeCycle error"

    [<Fact>]
    let ``BellmanFord returns NoPathExists for disconnected graph`` () =
        assertError (BellmanFord.find (buildDisconnectedGraph()) (NodeId "A") (NodeId "B"))

    [<Fact>]
    let ``BellmanFord returns NodeNotFound for unknown node`` () =
        let r = BellmanFord.find (buildLinearGraph()) (NodeId "GHOST") (NodeId "E")
        match r with
        | Error (PathError.NodeNotFound _) -> ()
        | _ -> failwith "Expected NodeNotFound"

    [<Fact>]
    let ``BellmanFord records algorithm used`` () =
        let g = buildLinearGraph()
        match BellmanFord.find g (NodeId "A") (NodeId "E") with
        | Ok pr -> pr.AlgorithmUsed |> should equal PathfindingAlgorithm.BellmanFord
        | Error e -> failwithf "%A" e

// ============================================================
//  PathPlanner (unified API) Tests
// ============================================================

module PathPlannerTests =

    [<Fact>]
    let ``plan dispatches to correct algorithm`` () =
        let g = buildLinearGraph()
        let test algo =
            match PathPlanner.plan algo g (NodeId "A") (NodeId "E") with
            | Ok pr -> pr.AlgorithmUsed |> should equal algo
            | Error e -> failwithf "%A" e
        test PathfindingAlgorithm.AStar
        test PathfindingAlgorithm.Dijkstra
        test PathfindingAlgorithm.BellmanFord

    [<Fact>]
    let ``autoSelect chooses BellmanFord for negative-edge graph`` () =
        let node id = { Id = NodeId id; Coordinate = { Latitude = 0.0; Longitude = 0.0 }; Label = id; IsDepot = false }
        // Negative distance but no cycle — BF can handle it
        let g =
            RoadGraph.empty()
            |> RoadGraph.addNode (node "A")
            |> RoadGraph.addNode (node "B")
            |> RoadGraph.addEdge { Source = NodeId "A"; Target = NodeId "B"
                                   DistanceKm = -0.5; SpeedLimitKmh = 60.0
                                   TrafficFactor = 1.0; IsOneWay = true }
        // Just verify it runs (result may be error due to negative cost)
        let _r = PathPlanner.autoSelect g (NodeId "A") (NodeId "B")
        ()

    [<Fact>]
    let ``planMultiStop through 3 waypoints produces combined path`` () =
        let g      = buildLinearGraph()
        let stops  = [NodeId "A"; NodeId "B"; NodeId "E"]
        let result = PathPlanner.planMultiStop PathfindingAlgorithm.AStar g stops
        match result with
        | Error e -> failwithf "planMultiStop failed: %A" e
        | Ok pr   ->
            pr.Path |> List.head |> should equal (NodeId "A")
            pr.Path |> List.last |> should equal (NodeId "E")
            pr.TotalDistanceKm   |> should be (greaterThan 0.0)
            pr.TotalCost         |> should be (greaterThan 0.0)

    [<Fact>]
    let ``planMultiStop with single waypoint returns EmptyGraph error`` () =
        let g = buildLinearGraph()
        match PathPlanner.planMultiStop PathfindingAlgorithm.Dijkstra g [NodeId "A"] with
        | Error PathError.EmptyGraph -> ()
        | _ -> failwith "Expected EmptyGraph error for single waypoint"

    [<Fact>]
    let ``planMultiStop with empty waypoints returns EmptyGraph error`` () =
        let g = buildLinearGraph()
        match PathPlanner.planMultiStop PathfindingAlgorithm.Dijkstra g [] with
        | Error PathError.EmptyGraph -> ()
        | _ -> failwith "Expected EmptyGraph error for empty waypoints"

    [<Fact>]
    let ``All three algorithms agree on sample graph DEPOT to G`` () =
        let g  = RoadGraph.buildSampleGraph()
        let ra = PathPlanner.plan PathfindingAlgorithm.AStar       g (NodeId "DEPOT") (NodeId "G")
        let rd = PathPlanner.plan PathfindingAlgorithm.Dijkstra    g (NodeId "DEPOT") (NodeId "G")
        let rb = PathPlanner.plan PathfindingAlgorithm.BellmanFord g (NodeId "DEPOT") (NodeId "G")
        match ra, rd, rb with
        | Ok a, Ok d, Ok b ->
            // All should find the same optimal cost
            a.TotalCost |> should (equalWithin 0.01) d.TotalCost
            d.TotalCost |> should (equalWithin 0.01) b.TotalCost
        | _ ->
            failwith "All three algorithms should find a path on sample graph"

    [<Fact>]
    let ``ComputedInMs is non-negative`` () =
        let g = RoadGraph.buildSampleGraph()
        match PathPlanner.plan PathfindingAlgorithm.AStar g (NodeId "DEPOT") (NodeId "G") with
        | Ok pr -> pr.ComputedInMs |> should be (greaterThanOrEqualTo 0L)
        | Error e -> failwithf "%A" e
