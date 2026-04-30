namespace FleetManagement.API.Messaging
//module FleetManagement.API.Messaging.TelemetryMessage

open System
open System.Text.Json.Serialization

/// Wire-format telemetry message — identical JSON schema published by
/// the simulator to both NATS JetStream and RabbitMQ.
[<CLIMutable>]
type TelemetryMessage = {
    [<JsonPropertyName("vehicleId")>]  VehicleId  : string
    [<JsonPropertyName("lat")>]        Lat        : float
    [<JsonPropertyName("lon")>]        Lon        : float
    [<JsonPropertyName("speedKmh")>]   SpeedKmh   : float
    [<JsonPropertyName("fuelPct")>]    FuelPct    : float
    [<JsonPropertyName("engineTemp")>] EngineTemp : float
    [<JsonPropertyName("odometerKm")>] OdometerKm : float
    [<JsonPropertyName("diagCodes")>]  DiagCodes  : string[]
    [<JsonPropertyName("timestamp")>]  Timestamp  : DateTimeOffset
}