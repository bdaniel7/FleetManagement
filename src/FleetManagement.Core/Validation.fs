module FleetManagement.Core.Validation

open Domain
open System

// ============================================================
//  Validation DSL
// ============================================================

type ValidationError = {
    Field   : string
    Message : string
}

type ValidationResult<'T> =
    | Valid   of 'T
    | Invalid of ValidationError list

module Validate =
    let ok x = Valid x

    let fail field msg = Invalid [{ Field = field; Message = msg }]

    let bind f = function
        | Valid x   -> f x
        | Invalid e -> Invalid e

    let map f = function
        | Valid x   -> Valid (f x)
        | Invalid e -> Invalid e

    let apply fv xv =
        match fv, xv with
        | Valid f,   Valid x   -> Valid (f x)
        | Invalid e, Valid _   -> Invalid e
        | Valid _,   Invalid e -> Invalid e
        | Invalid e1, Invalid e2 -> Invalid (e1 @ e2)

    let combine vs =
        let errors = vs |> List.collect (function Invalid e -> e | _ -> [])
        if errors.IsEmpty then Valid () else Invalid errors

    let notEmpty field (s: string) =
        if String.IsNullOrWhiteSpace s then fail field "Value cannot be empty"
        else Valid s

    let maxLength field maxLen (s: string) =
        if s.Length > maxLen then fail field $"Value exceeds maximum length of {maxLen}"
        else Valid s

    let inRange field min max (v: float) =
        if v < min || v > max then fail field $"Value must be between {min} and {max}"
        else Valid v

    let isPositive field (v: float) =
        if v <= 0.0 then fail field "Value must be positive"
        else Valid v

    let licensePlate field (plate: string) =
        let clean = plate.Trim().ToUpperInvariant()
        if clean.Length < 2 || clean.Length > 15 then
            fail field "License plate must be between 2 and 15 characters"
        else Valid clean

    let coordinate field (coord: GeoCoordinate) =
        let latCheck = inRange (field + ".Latitude")  -90.0  90.0  coord.Latitude
        let lonCheck = inRange (field + ".Longitude") -180.0 180.0 coord.Longitude
        match latCheck, lonCheck with
        | Valid _, Valid _ -> Valid coord
        | Invalid e1, Invalid e2 -> Invalid (e1 @ e2)
        | Invalid e, _ | _, Invalid e -> Invalid e

// ============================================================
//  Domain-specific validators
// ============================================================

let validateVehicleCreate (plate: string) (vtype: VehicleType) (payload: float) (location: GeoCoordinate) =
    let plateV   = Validate.licensePlate "LicensePlate" plate
    let payloadV = Validate.isPositive "MaxPayloadKg" payload
    let coordV   = Validate.coordinate "CurrentLocation" location
    match plateV, payloadV, coordV with
    | Valid p, Valid w, Valid c -> Valid (p, vtype, w, c)
    | _ ->
        let errors =
            [ match plateV   with Invalid e -> yield! e | _ -> ()
              match payloadV with Invalid e -> yield! e | _ -> ()
              match coordV   with Invalid e -> yield! e | _ -> () ]
        Invalid errors

let validateRoute (vehicleId: VehicleId) (waypoints: GeoCoordinate list) (algo: PathfindingAlgorithm) =
    if waypoints.Length < 2 then
        Validate.fail "Waypoints" "A route requires at least 2 waypoints"
    else
        let coordErrors =
            waypoints
            |> List.mapi (fun i c -> Validate.coordinate $"Waypoints[{i}]" c)
            |> List.collect (function Invalid e -> e | _ -> [])
        if coordErrors.IsEmpty then Valid (vehicleId, waypoints, algo)
        else Invalid coordErrors
