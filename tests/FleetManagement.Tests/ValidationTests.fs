module FleetManagement.Tests.ValidationTests

open Xunit
open FsUnit.Xunit
open FleetManagement.Core.Domain
open FleetManagement.Core.Validation
open System

// ============================================================
//  Validate module primitive tests
// ============================================================

[<Fact>]
let ``notEmpty returns Valid for non-empty string`` () =
    match Validate.notEmpty "field" "hello" with
    | Valid v -> v |> should equal "hello"
    | Invalid _ -> failwith "Should be Valid"

[<Fact>]
let ``notEmpty returns Invalid for empty string`` () =
    match Validate.notEmpty "field" "" with
    | Invalid [e] -> e.Field |> should equal "field"
    | _ -> failwith "Should be Invalid"

[<Fact>]
let ``notEmpty returns Invalid for whitespace-only string`` () =
    match Validate.notEmpty "field" "   " with
    | Invalid _ -> ()
    | Valid _   -> failwith "Should be Invalid"

[<Fact>]
let ``inRange returns Valid when value is within bounds`` () =
    match Validate.inRange "f" 0.0 100.0 50.0 with
    | Valid 50.0 -> ()
    | _ -> failwith "Should be Valid"

[<Fact>]
let ``inRange returns Invalid when value exceeds max`` () =
    match Validate.inRange "f" 0.0 100.0 101.0 with
    | Invalid _ -> ()
    | Valid _   -> failwith "Should be Invalid"

[<Fact>]
let ``inRange returns Invalid when value is below min`` () =
    match Validate.inRange "f" 0.0 100.0 -1.0 with
    | Invalid _ -> ()
    | Valid _   -> failwith "Should be Invalid"

[<Fact>]
let ``isPositive returns Valid for positive number`` () =
    match Validate.isPositive "w" 0.001 with
    | Valid _ -> ()
    | Invalid _ -> failwith "Should be Valid"

[<Fact>]
let ``isPositive returns Invalid for zero`` () =
    match Validate.isPositive "w" 0.0 with
    | Invalid _ -> ()
    | Valid _   -> failwith "Zero should be Invalid"

[<Fact>]
let ``isPositive returns Invalid for negative number`` () =
    match Validate.isPositive "w" -5.0 with
    | Invalid _ -> ()
    | Valid _   -> failwith "Negative should be Invalid"

[<Fact>]
let ``maxLength returns Valid when within limit`` () =
    match Validate.maxLength "f" 10 "hello" with
    | Valid "hello" -> ()
    | _ -> failwith "Should be Valid"

[<Fact>]
let ``maxLength returns Invalid when exceeding limit`` () =
    match Validate.maxLength "f" 3 "toolong" with
    | Invalid _ -> ()
    | Valid _ -> failwith "Should be Invalid"

// ============================================================
//  License plate validation
// ============================================================

[<Fact>]
let ``licensePlate normalises to uppercase and trims`` () =
    match Validate.licensePlate "plate" "  ab-123  " with
    | Valid v -> v |> should equal "AB-123"
    | Invalid _ -> failwith "Should be Valid"

[<Fact>]
let ``licensePlate returns Invalid for single-char plate`` () =
    match Validate.licensePlate "plate" "X" with
    | Invalid _ -> ()
    | Valid _   -> failwith "Should be Invalid"

[<Fact>]
let ``licensePlate returns Invalid for plate over 15 chars`` () =
    match Validate.licensePlate "plate" (String.replicate 16 "A") with
    | Invalid _ -> ()
    | Valid _   -> failwith "Should be Invalid"

// ============================================================
//  GeoCoordinate validation
// ============================================================

[<Fact>]
let ``coordinate returns Valid for Bucharest`` () =
    let coord = { Latitude = 44.4268; Longitude = 26.1025 }
    match Validate.coordinate "loc" coord with
    | Valid _ -> ()
    | Invalid e -> failwithf "Bucharest should be valid: %A" e

[<Fact>]
let ``coordinate returns Invalid for latitude > 90`` () =
    let coord = { Latitude = 91.0; Longitude = 0.0 }
    match Validate.coordinate "loc" coord with
    | Invalid _ -> ()
    | Valid _   -> failwith "lat > 90 should be Invalid"

[<Fact>]
let ``coordinate returns Invalid for latitude < -90`` () =
    let coord = { Latitude = -91.0; Longitude = 0.0 }
    match Validate.coordinate "loc" coord with
    | Invalid _ -> ()
    | Valid _   -> failwith "lat < -90 should be Invalid"

[<Fact>]
let ``coordinate returns Invalid for longitude > 180`` () =
    let coord = { Latitude = 0.0; Longitude = 181.0 }
    match Validate.coordinate "loc" coord with
    | Invalid _ -> ()
    | Valid _   -> failwith "lon > 180 should be Invalid"

[<Fact>]
let ``coordinate returns Invalid for both bad lat and lon`` () =
    let coord = { Latitude = 999.0; Longitude = -999.0 }
    match Validate.coordinate "loc" coord with
    | Invalid errors -> errors.Length |> should be (greaterThanOrEqualTo 2)
    | Valid _        -> failwith "Should be Invalid"

// ============================================================
//  Domain validators
// ============================================================

[<Fact>]
let ``validateVehicleCreate returns Valid for good input`` () =
    let coord = { Latitude = 44.43; Longitude = 26.10 }
    match validateVehicleCreate "AB-123-CD" VehicleType.Truck 5000.0 coord with
    | Valid (plate, vtype, payload, loc) ->
        plate   |> should equal "AB-123-CD"
        vtype   |> should equal VehicleType.Truck
        payload |> should equal 5000.0
        loc     |> should equal coord
    | Invalid errors -> failwithf "Should be Valid: %A" errors

[<Fact>]
let ``validateVehicleCreate returns Invalid for empty plate`` () =
    let coord = { Latitude = 44.43; Longitude = 26.10 }
    match validateVehicleCreate "" VehicleType.Van 1000.0 coord with
    | Invalid errors -> errors |> List.exists (fun e -> e.Field = "LicensePlate") |> should be True
    | Valid _ -> failwith "Empty plate should be Invalid"

[<Fact>]
let ``validateVehicleCreate returns Invalid for zero payload`` () =
    let coord = { Latitude = 44.43; Longitude = 26.10 }
    match validateVehicleCreate "XY-001" VehicleType.Car 0.0 coord with
    | Invalid errors -> errors |> List.exists (fun e -> e.Field = "MaxPayloadKg") |> should be True
    | Valid _ -> failwith "Zero payload should be Invalid"

[<Fact>]
let ``validateVehicleCreate accumulates multiple errors`` () =
    let bad = { Latitude = 999.0; Longitude = 999.0 }
    match validateVehicleCreate "" VehicleType.Car -1.0 bad with
    | Invalid errors -> errors.Length |> should be (greaterThanOrEqualTo 3)
    | Valid _ -> failwith "Should collect all errors"

[<Fact>]
let ``validateRoute returns Valid for 2 good waypoints`` () =
    let vid = VehicleId (Guid.NewGuid())
    let wps = [ { Latitude = 44.4; Longitude = 26.1 }
                { Latitude = 44.5; Longitude = 26.2 } ]
    match validateRoute vid wps PathfindingAlgorithm.AStar with
    | Valid _ -> ()
    | Invalid e -> failwithf "Should be Valid: %A" e

[<Fact>]
let ``validateRoute returns Invalid for single waypoint`` () =
    let vid = VehicleId (Guid.NewGuid())
    let wps = [ { Latitude = 44.4; Longitude = 26.1 } ]
    match validateRoute vid wps PathfindingAlgorithm.Dijkstra with
    | Invalid _ -> ()
    | Valid _   -> failwith "Single waypoint should be Invalid"

[<Fact>]
let ``validateRoute returns Invalid for empty waypoints`` () =
    let vid = VehicleId (Guid.NewGuid())
    match validateRoute vid [] PathfindingAlgorithm.Dijkstra with
    | Invalid _ -> ()
    | Valid _   -> failwith "Empty waypoints should be Invalid"

// ============================================================
//  Validate combinator tests
// ============================================================

[<Fact>]
let ``Validate.combine merges multiple invalid results`` () =
    let results = [
        Validate.fail "f1" "e1"
        Validate.fail "f2" "e2"
        Validate.ok ()
    ]
    match Validate.combine results with
    | Invalid errors -> errors.Length |> should equal 2
    | Valid _ -> failwith "Should be Invalid"

[<Fact>]
let ``Validate.combine returns Valid when all valid`` () =
    let results = [ Validate.ok (); Validate.ok () ]
    match Validate.combine results with
    | Valid () -> ()
    | Invalid _ -> failwith "Should be Valid"
