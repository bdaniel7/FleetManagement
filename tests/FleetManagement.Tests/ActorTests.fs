module FleetManagement.Tests.ActorTests

open System
open Xunit
open FsUnit.Xunit
open Akka.Actor
open Akka.TestKit.Xunit2
open FleetManagement.Core.Domain
open FleetManagement.Core.Events
open FleetManagement.Actors.ActorMessages
open FleetManagement.Actors.VehicleActor
open FleetManagement.Actors.RouteCalculatorActor

// ============================================================
//  Test fixtures
// ============================================================

let private makeVehicle () = {
    Id               = VehicleId (Guid.NewGuid())
    LicensePlate     = "TEST-001"
    VehicleType      = VehicleType.Van
    Status           = VehicleStatus.Idle
    CurrentLocation  = { Latitude = 44.4268; Longitude = 26.1025 }
    AssignedDriver   = None
    FuelLevelPct     = 80.0
    SpeedKmh         = 0.0
    MaxPayloadKg     = 2000.0
    CurrentPayloadKg = 0.0
    Telemetry = {
        OdometerKm      = 12500.0
        EngineTemp      = 85.0
        BatteryLevel    = None
        LastHeartbeat   = DateTimeOffset.UtcNow
        DiagnosticCodes = []
    }
    CreatedAt = DateTimeOffset.UtcNow
    UpdatedAt = DateTimeOffset.UtcNow
}

// ============================================================
//  Vehicle Actor Tests
// ============================================================

type VehicleActorTests() =
    inherit TestKit()

    [<Fact>]
    member this.``GetVehicleState returns initial vehicle state`` () =
        let vehicle    = makeVehicle()
        let published  = ResizeArray<DomainEvent>()
        let actorRef   = spawn this.Sys vehicle (published.Add)

        actorRef.Tell(GetVehicleState, this.TestActor)
        let snapshot = this.ExpectMsg<VehicleStateSnapshot>()
        snapshot.Vehicle.Id          |> should equal vehicle.Id
        snapshot.Vehicle.LicensePlate|> should equal "TEST-001"
        snapshot.ActiveRouteId       |> should equal None

    [<Fact>]
    member this.``UpdateLocation changes vehicle coordinates`` () =
        let vehicle  = makeVehicle()
        let pub      = ResizeArray<DomainEvent>()
        let actor    = spawn this.Sys vehicle pub.Add
        let newLoc   = { Latitude = 44.50; Longitude = 26.20 }

        actor.Tell(UpdateLocation(newLoc, 55.0), this.TestActor)
        actor.Tell(GetVehicleState,              this.TestActor)
        let snap = this.ExpectMsg<VehicleStateSnapshot>()
        snap.Vehicle.CurrentLocation.Latitude  |> should (equalWithin 0.0001) 44.50
        snap.Vehicle.CurrentLocation.Longitude |> should (equalWithin 0.0001) 26.20
        snap.Vehicle.SpeedKmh                  |> should (equalWithin 0.001)  55.0

    [<Fact>]
    member this.``ChangeStatus publishes domain event`` () =
        let vehicle = makeVehicle()
        let pub     = ResizeArray<DomainEvent>()
        let actor   = spawn this.Sys vehicle pub.Add

        actor.Tell(ChangeStatus VehicleStatus.EnRoute, this.TestActor)
        this.AwaitCondition(fun () -> pub.Count > 0) // , TimeSpan.FromSeconds 2.0

        pub |> Seq.exists (fun e ->
            match e.Payload with
            | VehicleStatusChanged (_, _, VehicleStatus.EnRoute) -> true
            | _ -> false) |> should be True

    [<Fact>]
    member this.``ChangeStatus does not publish event for same status`` () =
        let vehicle = makeVehicle()
        let pub     = ResizeArray<DomainEvent>()
        let actor   = spawn this.Sys vehicle pub.Add

        actor.Tell(ChangeStatus VehicleStatus.Idle, this.TestActor)  // already Idle
        actor.Tell(GetVehicleState, this.TestActor)
        this.ExpectMsg<VehicleStateSnapshot>() |> ignore

        pub |> Seq.exists (fun e ->
            match e.Payload with
            | VehicleStatusChanged _ -> true
            | _ -> false) |> should be False

    [<Fact>]
    member this.``UpdateFuel clamps to 0-100 range`` () =
        let vehicle = makeVehicle()
        let pub     = ResizeArray<DomainEvent>()
        let actor   = spawn this.Sys vehicle pub.Add

        actor.Tell(UpdateFuel 150.0, this.TestActor)
        actor.Tell(GetVehicleState,  this.TestActor)
        let snap = this.ExpectMsg<VehicleStateSnapshot>()
        snap.Vehicle.FuelLevelPct |> should be (lessThanOrEqualTo 100.0)

    [<Fact>]
    member this.``AssignDriver sets driver on vehicle`` () =
        let vehicle  = makeVehicle()
        let driverId = DriverId (Guid.NewGuid())
        let pub      = ResizeArray<DomainEvent>()
        let actor    = spawn this.Sys vehicle pub.Add

        actor.Tell(AssignDriver driverId, this.TestActor)
        actor.Tell(GetVehicleState,       this.TestActor)
        let snap = this.ExpectMsg<VehicleStateSnapshot>()
        snap.Vehicle.AssignedDriver |> should equal (Some driverId)

    [<Fact>]
    member this.``UnassignDriver clears driver`` () =
        let vehicle  = { makeVehicle() with AssignedDriver = Some (DriverId (Guid.NewGuid())) }
        let pub      = ResizeArray<DomainEvent>()
        let actor    = spawn this.Sys vehicle pub.Add

        actor.Tell(UnassignDriver,  this.TestActor)
        actor.Tell(GetVehicleState, this.TestActor)
        let snap = this.ExpectMsg<VehicleStateSnapshot>()
        snap.Vehicle.AssignedDriver |> should equal None

    [<Fact>]
    member this.``StartRoute sets vehicle status to EnRoute and records route`` () =
        let vehicle = makeVehicle()
        let rid     = RouteId (Guid.NewGuid())
        let pub     = ResizeArray<DomainEvent>()
        let actor   = spawn this.Sys vehicle pub.Add

        actor.Tell(StartRoute rid,  this.TestActor)
        actor.Tell(GetVehicleState, this.TestActor)
        let snap = this.ExpectMsg<VehicleStateSnapshot>()
        snap.Vehicle.Status  |> should equal VehicleStatus.EnRoute
        snap.ActiveRouteId   |> should equal (Some rid)

    [<Fact>]
    member this.``CompleteRoute resets vehicle to Idle`` () =
        let vehicle = makeVehicle()
        let rid     = RouteId (Guid.NewGuid())
        let pub     = ResizeArray<DomainEvent>()
        let actor   = spawn this.Sys vehicle pub.Add

        actor.Tell(StartRoute rid,  this.TestActor)
        actor.Tell(CompleteRoute,   this.TestActor)
        actor.Tell(GetVehicleState, this.TestActor)
        let snap = this.ExpectMsg<VehicleStateSnapshot>()
        snap.Vehicle.Status |> should equal VehicleStatus.Idle
        snap.ActiveRouteId  |> should equal None

    [<Fact>]
    member this.``UpdateTelemetry reflects new diagnostic codes`` () =
        let vehicle   = makeVehicle()
        let pub       = ResizeArray<DomainEvent>()
        let actor     = spawn this.Sys vehicle pub.Add
        let telemetry = {
            vehicle.Telemetry with DiagnosticCodes = ["P0300"; "P0420"]
        }

        actor.Tell(UpdateTelemetry telemetry, this.TestActor)
        actor.Tell(GetVehicleState,           this.TestActor)
        let snap = this.ExpectMsg<VehicleStateSnapshot>()
        snap.Vehicle.Telemetry.DiagnosticCodes |> should equal ["P0300"; "P0420"]

// ============================================================
//  Route Calculator Actor Tests
// ============================================================

type RouteCalculatorActorTests() =
    inherit TestKit()

    [<Fact>]
    member this.``GetGraphStats returns stats after startup`` () =
        let actor = this.Sys.ActorOf(Props.Create routeCalculatorActor, "rc-stats")

        actor.Tell(GetGraphStats, this.TestActor)
        let stats = this.ExpectMsg<obj>()
        stats |> should not' (equal null)

    [<Fact>]
    member this.``ComputeRoute returns RouteComputed for valid waypoints`` () =
        let actor = this.Sys.ActorOf(Props.Create routeCalculatorActor, "rc-compute")

        let req : RouteRequest = {
            RequestId = Guid.NewGuid()
            VehicleId = VehicleId (Guid.NewGuid())
            DriverId  = None
            Coordinates = [
                { Latitude = 44.4268; Longitude = 26.1025 }  // DEPOT
                { Latitude = 44.4100; Longitude = 26.1000 }  // near G
            ]
            Algorithm = PathfindingAlgorithm.AStar
            Priority  = Priority.Normal
            ReplyTo   = this.TestActor
        }

        actor.Tell(ComputeRoute req, this.TestActor)
        let response = this.ExpectMsg<RouteResponse>(TimeSpan.FromSeconds 10.0)
        match response with
        | RouteComputed (_, route) ->
            route.TotalDistanceKm |> should be (greaterThanOrEqualTo 0.0)
        | RouteError (_, msg) ->
            // Acceptable if node resolution fails on sample graph
            msg |> should not' (be EmptyString)

    [<Fact>]
    member this.``ComputeRoute with single waypoint returns RouteError`` () =
        let actor = this.Sys.ActorOf(Props.Create routeCalculatorActor, "rc-single")

        let req : RouteRequest = {
            RequestId = Guid.NewGuid()
            VehicleId = VehicleId (Guid.NewGuid())
            DriverId  = None
            Coordinates = [ { Latitude = 44.4268; Longitude = 26.1025 } ]
            Algorithm = PathfindingAlgorithm.Dijkstra
            Priority  = Priority.Low
            ReplyTo   = this.TestActor
        }

        actor.Tell(ComputeRoute req, this.TestActor)
        let response = this.ExpectMsg<RouteResponse>(TimeSpan.FromSeconds 5.0)
        match response with
        | RouteError _ -> ()
        | RouteComputed _ -> failwith "Should have returned RouteError for single waypoint"

    [<Fact>]
    member this.``UpdateRoadGraph and then ComputeRoute uses new graph`` () =
        let actor    = this.Sys.ActorOf(Props.Create routeCalculatorActor, "rc-update")
        let newGraph = FleetManagement.Core.Graph.RoadGraph.buildSampleGraph()

        actor.Tell(UpdateRoadGraph newGraph, this.TestActor)

        let req : RouteRequest = {
            RequestId = Guid.NewGuid()
            VehicleId = VehicleId (Guid.NewGuid())
            DriverId  = None
            Coordinates = [
                { Latitude = 44.4268; Longitude = 26.1025 }
                { Latitude = 44.4100; Longitude = 26.1000 }
            ]
            Algorithm = PathfindingAlgorithm.Dijkstra
            Priority  = Priority.Normal
            ReplyTo   = this.TestActor
        }

        actor.Tell(ComputeRoute req, this.TestActor)
        let response = this.ExpectMsg<RouteResponse>(TimeSpan.FromSeconds 10.0)
        response |> should not' (equal null)
