module FleetManagement.API.Dtos

open System
// ============================================================
//  DTOs
// ============================================================

[<CLIMutable>]
type WaypointDto = {
    Latitude  : float
    Longitude : float
}

[<CLIMutable>]
type PlanRouteRequest = {
    VehicleId   : Guid
    DriverId    : Guid Nullable
    Waypoints   : WaypointDto[]
    Algorithm   : string   // "AStar" | "Dijkstra" | "BellmanFord" | "Auto"
    Priority    : string   // "Low" | "Normal" | "High" | "Emergency"
}

[<CLIMutable>]
type UpdateWaypointsRequest = {
    Waypoints : WaypointDto[]
    Algorithm : string
}