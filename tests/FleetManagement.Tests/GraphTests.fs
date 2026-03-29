module FleetManagement.Tests.GraphTests

open Xunit
open FsUnit.Xunit
open FleetManagement.Core.Domain
open FleetManagement.Core.Graph

// ============================================================
//  Helpers
// ============================================================

let makeNode id lat lon =
    { Id         = NodeId id
      Coordinate = { Latitude = lat; Longitude = lon }
      Label      = id
      IsDepot    = false }

let makeEdge src tgt dist speed =
    { Source        = NodeId src
      Target        = NodeId tgt
      DistanceKm    = dist
      SpeedLimitKmh = speed
      TrafficFactor = 1.0
      IsOneWay      = false }

let makeOneWay src tgt dist speed =
    { makeEdge src tgt dist speed with IsOneWay = true }

// ============================================================
//  Graph construction
// ============================================================

[<Fact>]
let ``Empty graph has zero nodes and edges`` () =
    let g = RoadGraph.empty()
    RoadGraph.nodeCount g |> should equal 0
    g.EdgeCount          |> should equal 0

[<Fact>]
let ``addNode increases node count`` () =
    let g = RoadGraph.empty() |> RoadGraph.addNode (makeNode "A" 0.0 0.0)
    RoadGraph.nodeCount g |> should equal 1

[<Fact>]
let ``addEdge creates bidirectional edges for two-way roads`` () =
    let g =
        RoadGraph.empty()
        |> RoadGraph.addNode (makeNode "A" 44.0 26.0)
        |> RoadGraph.addNode (makeNode "B" 44.1 26.1)
        |> RoadGraph.addEdge (makeEdge "A" "B" 5.0 60.0)

    // Both directions should be reachable
    RoadGraph.neighbors (NodeId "A") g |> Seq.length |> should equal 1
    RoadGraph.neighbors (NodeId "B") g |> Seq.length |> should equal 1
    g.EdgeCount |> should equal 2   // forward + reverse

[<Fact>]
let ``addEdge one-way creates only one direction`` () =
    let g =
        RoadGraph.empty()
        |> RoadGraph.addNode (makeNode "A" 44.0 26.0)
        |> RoadGraph.addNode (makeNode "B" 44.1 26.1)
        |> RoadGraph.addEdge (makeOneWay "A" "B" 5.0 60.0)

    RoadGraph.neighbors (NodeId "A") g |> Seq.length |> should equal 1
    RoadGraph.neighbors (NodeId "B") g |> Seq.length |> should equal 0
    g.EdgeCount |> should equal 1

[<Fact>]
let ``tryGetNode returns None for unknown node`` () =
    let g = RoadGraph.empty()
    RoadGraph.tryGetNode (NodeId "GHOST") g |> should equal None

[<Fact>]
let ``tryGetNode returns Some for known node`` () =
    let node = makeNode "X" 1.0 2.0
    let g    = RoadGraph.empty() |> RoadGraph.addNode node
    RoadGraph.tryGetNode (NodeId "X") g |> should equal (Some node)

[<Fact>]
let ``hasNegativeEdges detects negative distances`` () =
    let g =
        RoadGraph.empty()
        |> RoadGraph.addNode (makeNode "A" 0.0 0.0)
        |> RoadGraph.addNode (makeNode "B" 0.0 1.0)
        |> RoadGraph.addEdge { makeEdge "A" "B" 5.0 60.0 with DistanceKm = -1.0 }

    RoadGraph.hasNegativeEdges g |> should be True

[<Fact>]
let ``hasNegativeEdges returns false for normal graph`` () =
    let g = RoadGraph.buildSampleGraph()
    RoadGraph.hasNegativeEdges g |> should be False

[<Fact>]
let ``buildSampleGraph produces a non-trivial graph`` () =
    let g = RoadGraph.buildSampleGraph()
    RoadGraph.nodeCount g |> should be (greaterThan 5)
    g.EdgeCount           |> should be (greaterThan 10)

// ============================================================
//  Edge cost model
// ============================================================

[<Fact>]
let ``travelTimeMin increases with traffic factor`` () =
    let edge = makeEdge "A" "B" 10.0 60.0
    let freeFlow    = Edge.travelTimeMin edge
    let congested   = Edge.travelTimeMin { edge with TrafficFactor = 2.0 }
    congested |> should be (greaterThan freeFlow)

[<Fact>]
let ``travelTimeMin is correct for 60 km over 60 kph`` () =
    let edge = makeEdge "A" "B" 60.0 60.0
    let t    = Edge.travelTimeMin edge
    t |> should (equalWithin 0.001) 60.0   // 60 minutes

[<Fact>]
let ``withTraffic multiplier scales cost correctly`` () =
    let edge     = makeEdge "A" "B" 10.0 100.0
    let scaled   = Edge.withTraffic 4.0 edge
    let baseTime = Edge.travelTimeMin edge
    let scaledT  = Edge.travelTimeMin scaled
    scaledT |> should (equalWithin 0.001) (baseTime * 4.0)

// ============================================================
//  Haversine heuristic
// ============================================================

[<Fact>]
let ``heuristic returns zero for same node`` () =
    let g = RoadGraph.buildSampleGraph()
    let h = RoadGraph.heuristic (NodeId "DEPOT") (NodeId "DEPOT") g
    h |> should (equalWithin 0.01) 0.0

[<Fact>]
let ``heuristic returns positive value for different nodes`` () =
    let g = RoadGraph.buildSampleGraph()
    let h = RoadGraph.heuristic (NodeId "DEPOT") (NodeId "G") g
    h |> should be (greaterThan 0.0)

[<Fact>]
let ``GeoCoordinate distanceKm approximates 111 km per degree latitude`` () =
    let a = { Latitude = 0.0; Longitude = 0.0 }
    let b = { Latitude = 1.0; Longitude = 0.0 }
    let d = GeoCoordinate.distanceKm a b
    d |> should (equalWithin 1.0) 111.19   // ≈ 111 km per degree
