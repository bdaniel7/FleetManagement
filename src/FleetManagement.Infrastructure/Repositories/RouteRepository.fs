module FleetManagement.Infrastructure.Repositories.RouteRepository

open System
open FleetManagement.Core.Domain
open FleetManagement.Infrastructure.DbContext
open FleetManagement.Infrastructure.IRepositories

[<CLIMutable>]
type RouteRow = {
    id                      : Guid
    vehicle_id              : Guid
    driver_id               : Guid Nullable
    total_distance_km       : float
    estimated_duration_min  : int
    status                  : string
    priority                : string
    algorithm               : string
    waypoints_json          : string
    optimized_path_json     : string
    created_at              : DateTimeOffset
    started_at              : DateTimeOffset Nullable
    completed_at            : DateTimeOffset Nullable
}

module private Mapping =
    open System.Text.Json

    let parseStatus = function
        | "Planned"   -> RouteStatus.Planned
        | "Active"    -> RouteStatus.Active
        | "Completed" -> RouteStatus.Completed
        | "Cancelled" -> RouteStatus.Cancelled
        | "Rerouting" -> RouteStatus.Rerouting
        | s           -> failwithf "Unknown route status: %s" s

    let parsePriority = function
        | "Low"       -> Priority.Low
        | "Normal"    -> Priority.Normal
        | "High"      -> Priority.High
        | "Emergency" -> Priority.Emergency
        | s           -> failwithf "Unknown priority: %s" s

    let parseAlgorithm = function
        | "AStar"       -> PathfindingAlgorithm.AStar
        | "Dijkstra"    -> PathfindingAlgorithm.Dijkstra
        | "BellmanFord" -> PathfindingAlgorithm.BellmanFord
        | s             -> failwithf "Unknown algorithm: %s" s

    let parseNodeIds (json: string) : NodeId list =
        try
            JsonSerializer.Deserialize<string[]>(json)
            |> Array.map NodeId
            |> Array.toList
        with _ -> []

    let parseWaypoints (json: string) : Waypoint list =
        try
            JsonSerializer.Deserialize<{| nodeId: string; lat: float; lon: float; address: string |}[]>(json)
            |> Array.map (fun w ->
                { NodeId          = NodeId w.nodeId
                  Coordinate      = { Latitude = w.lat; Longitude = w.lon }
                  Address         = w.address
                  ArrivalTime     = None
                  DepartureTime   = None
                  StopDurationMin = 0 })
            |> Array.toList
        with _ -> []

    let toDomain (r: RouteRow) : Route = {
        Id                   = RouteId r.id
        VehicleId            = VehicleId r.vehicle_id
        DriverId             = if r.driver_id.HasValue then Some (DriverId r.driver_id.Value) else None
        Waypoints            = parseWaypoints r.waypoints_json
        OptimizedPath        = parseNodeIds r.optimized_path_json
        TotalDistanceKm      = r.total_distance_km
        EstimatedDurationMin = r.estimated_duration_min
        Status               = parseStatus r.status
        Priority             = parsePriority r.priority
        Algorithm            = parseAlgorithm r.algorithm
        CreatedAt            = r.created_at
        StartedAt            = if r.started_at.HasValue then Some r.started_at.Value else None
        CompletedAt          = if r.completed_at.HasValue then Some r.completed_at.Value else None
    }

    let waypointsJson (waypoints: Waypoint list) =
        let items =
            waypoints
            |> List.map (fun w ->
                let (NodeId nid) = w.NodeId
                $"""{{ "nodeId":"{nid}", "lat":{w.Coordinate.Latitude}, "lon":{w.Coordinate.Longitude}, "address":"{w.Address}" }}""")
            |> String.concat ","
        $"[{items}]"

    let nodeIdsJson (nodes: NodeId list) =
        nodes
        |> List.map (fun (NodeId n) -> $"\"{n}\"")
        |> String.concat ","
        |> fun s -> $"[{s}]"

type PostgresRouteRepository(ctx: IDbContext) =

    let selectAll = """
        SELECT id, vehicle_id, driver_id, total_distance_km, estimated_duration_min,
               status, priority, algorithm,
               waypoints_json, optimized_path_json,
               created_at, started_at, completed_at
        FROM public.fms_routes"""

    interface IRouteRepository with

        member _.GetById (RouteId rid) = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! row = Db.queryFirst<RouteRow>
                           $"{selectAll} WHERE id = @id" {| id = rid |} conn
            return row |> Option.map Mapping.toDomain
        }

        member _.GetByVehicle (VehicleId vid) = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<RouteRow>
                            $"{selectAll} WHERE vehicle_id = @vid ORDER BY created_at DESC" {| vid = vid |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.GetActive () = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<RouteRow>
                            $"{selectAll} WHERE status IN ('Active','Rerouting') ORDER BY created_at DESC" {| |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.GetAll () = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<RouteRow>
                            $"{selectAll} ORDER BY created_at DESC LIMIT 500" {| |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.Insert route = async {
            let (RouteId rid)   = route.Id
            let (VehicleId vid) = route.VehicleId
            let driverId        = route.DriverId |> Option.map (fun (DriverId d) -> d) |> Option.toNullable
            let sql = """
                INSERT INTO public.fms_routes (id, vehicle_id, driver_id, total_distance_km, estimated_duration_min,
                    status, priority, algorithm, waypoints_json, optimized_path_json, created_at, started_at, completed_at)
                VALUES (@id, @vid, @driver_id, @distance, @duration,
                    @status, @priority, @algorithm, @waypoints::jsonb, @path::jsonb, @created_at, @started_at, @completed_at)"""
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute sql
                             {| id          = rid
                                vid         = vid
                                driver_id   = driverId
                                distance    = route.TotalDistanceKm
                                duration    = route.EstimatedDurationMin
                                status      = string route.Status
                                priority    = string route.Priority
                                algorithm   = string route.Algorithm
                                waypoints   = Mapping.waypointsJson route.Waypoints
                                path        = Mapping.nodeIdsJson route.OptimizedPath
                                created_at  = route.CreatedAt
                                started_at  = route.StartedAt |> Option.toNullable
                                completed_at= route.CompletedAt |> Option.toNullable |} conn tx
                return ()
            })
        }

        member _.UpdateStatus (RouteId rid, status) = async {
            let startedAt =
                if status = RouteStatus.Active then Some DateTimeOffset.UtcNow |> Option.toNullable
                else Nullable()
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            "UPDATE public.fms_routes SET status=@status, started_at=COALESCE(@started_at, started_at) WHERE id=@id"
                            {| id=rid; status=string status; started_at=startedAt |} conn tx
                return ()
            })
        }

        member _.UpdateWaypoints (RouteId rid, waypoints, algo) = async {
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            """UPDATE public.fms_routes
                               SET waypoints_json    = @waypoints::jsonb,
                                   optimized_path_json = @path::jsonb,
                                   algorithm         = @algo,
                                   status            = 'Planned',
                                   updated_at        = NOW()
                               WHERE id = @id"""
                            {| id       = rid
                               waypoints = Mapping.waypointsJson waypoints
                               path      = Mapping.nodeIdsJson (waypoints |> List.map (fun w -> w.NodeId))
                               algo      = string algo |} conn tx
                return ()
            })
        }

        member _.Complete (RouteId rid) = async {
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            "UPDATE public.fms_routes SET status='Completed', completed_at=NOW() WHERE id=@id"
                            {| id=rid |} conn tx
                return ()
            })
        }
