module FleetManagement.Core.Graph

open System.Collections.Generic
open Domain

// ============================================================
//  Road Network Graph
// ============================================================

/// An edge in the road network with weighted cost.
[<Struct>]
type Edge = {
    Source     : NodeId
    Target     : NodeId
    DistanceKm : float
    SpeedLimitKmh: float
    TrafficFactor: float    // 1.0 = free flow, >1.0 = congestion
    IsOneWay   : bool
}

module Edge =
    /// Effective travel time in minutes (cost used by algorithms)
    let travelTimeMin (e: Edge) =
        let effectiveSpeed = e.SpeedLimitKmh / e.TrafficFactor
        (e.DistanceKm / effectiveSpeed) * 60.0

    let withTraffic factor (e: Edge) = { e with TrafficFactor = factor }

type RoadNode = {
    Id         : NodeId
    Coordinate : GeoCoordinate
    Label      : string
    IsDepot    : bool
}

/// Immutable adjacency list graph, safe to share across actors.
type RoadGraph = {
    Nodes    : Dictionary<NodeId, RoadNode>
    AdjList  : Dictionary<NodeId, ResizeArray<Edge>>
    EdgeCount: int
}

module RoadGraph =
    let empty () = {
        Nodes     = Dictionary()
        AdjList   = Dictionary()
        EdgeCount = 0
    }

    let addNode (node: RoadNode) (g: RoadGraph) =
        g.Nodes.[node.Id] <- node
        if not (g.AdjList.ContainsKey node.Id) then
            g.AdjList.[node.Id] <- ResizeArray()
        g

    let addEdge (edge: Edge) (g: RoadGraph) =
        match g.AdjList.TryGetValue edge.Source with
        | true, list ->
            list.Add edge
            if not edge.IsOneWay then
                let reverse = {
                    edge with
                        Source = edge.Target
                        Target = edge.Source
                }
                match g.AdjList.TryGetValue edge.Target with
                | true, list2 -> list2.Add reverse
                | _ ->
                    let r = ResizeArray()
                    r.Add reverse
                    g.AdjList.[edge.Target] <- r
            { g with EdgeCount = g.EdgeCount + (if edge.IsOneWay then 1 else 2) }
        | _ ->
            failwithf "Source node %A not found. Add the node before adding edges." edge.Source

    let neighbors (nodeId: NodeId) (g: RoadGraph) : Edge seq =
        match g.AdjList.TryGetValue nodeId with
        | true, edges -> edges :> seq<_>
        | _ -> Seq.empty

    let tryGetNode (nodeId: NodeId) (g: RoadGraph) =
        match g.Nodes.TryGetValue nodeId with
        | true, n -> Some n
        | _ -> None

    let nodeCount (g: RoadGraph) = g.Nodes.Count

    /// Heuristic: straight-line Haversine distance (used in A*)
    let heuristic (a: NodeId) (b: NodeId) (g: RoadGraph) : float =
        match tryGetNode a g, tryGetNode b g with
        | Some na, Some nb ->
            let distKm = GeoCoordinate.distanceKm na.Coordinate nb.Coordinate
            // Assume 60 km/h average → time in minutes
            distKm / 60.0 * 60.0
        | _ -> 0.0

    /// Check for negative edges (required before Bellman-Ford)
    let hasNegativeEdges (g: RoadGraph) =
        g.AdjList.Values
        |> Seq.concat
        |> Seq.exists (fun e -> e.DistanceKm < 0.0)

    /// Build a sample city graph for testing/demos
    let buildSampleGraph () =
        let g = empty ()
        let mkNode id lat lon label depot =
            let nid = NodeId id
            let node = {
                Id         = nid
                Coordinate = { Latitude = lat; Longitude = lon }
                Label      = label
                IsDepot    = depot
            }
            (nid, node)

        let nodes = [
            mkNode "DEPOT"  44.4268  26.1025 "Central Depot"    true
            mkNode "A"      44.4300  26.1100 "Warehouse A"      false
            mkNode "B"      44.4350  26.0950 "Distribution B"   false
            mkNode "C"      44.4200  26.0900 "Customer C"       false
            mkNode "D"      44.4150  26.1200 "Customer D"       false
            mkNode "E"      44.4400  26.1050 "Hub E"            false
            mkNode "F"      44.4250  26.1300 "Customer F"       false
            mkNode "G"      44.4100  26.1000 "Customer G"       false
        ]

        let g' = nodes |> List.fold (fun acc (_, n) -> addNode n acc) g

        let mkEdge src tgt dist speed =
            { Source = NodeId src; Target = NodeId tgt
              DistanceKm = dist; SpeedLimitKmh = speed
              TrafficFactor = 1.0; IsOneWay = false }

        let edges = [
            mkEdge "DEPOT" "A"     3.2 60.0
            mkEdge "DEPOT" "B"     2.8 50.0
            mkEdge "DEPOT" "E"     4.5 80.0
            mkEdge "A"     "E"     2.1 60.0
            mkEdge "A"     "C"     5.3 50.0
            mkEdge "B"     "C"     1.9 40.0
            mkEdge "B"     "G"     3.7 50.0
            mkEdge "C"     "D"     4.1 60.0
            mkEdge "C"     "G"     2.5 50.0
            mkEdge "D"     "F"     3.3 60.0
            mkEdge "E"     "A"     2.1 60.0
            mkEdge "E"     "F"     5.8 80.0
            mkEdge "F"     "G"     2.9 50.0
            mkEdge "G"     "DEPOT" 4.0 60.0
        ]

        edges |> List.fold (fun acc e -> addEdge e acc) g'
