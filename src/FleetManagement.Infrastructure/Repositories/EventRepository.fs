module FleetManagement.Infrastructure.Repositories.EventRepository

open System
open System.Text.Json
open FleetManagement.Core.Domain
open FleetManagement.Core.Events
open FleetManagement.Infrastructure.DbContext
open FleetManagement.Infrastructure.IRepositories

[<CLIMutable>]
type EventRow = {
    id              : Guid
    occurred_at     : DateTimeOffset
    correlation_id  : Guid
    event_type      : string
    payload_json    : string
}

type PostgresEventRepository(ctx: IDbContext) =

    let serialize (ev: DomainEvent) =
        let (EventId eid) = ev.EventId
        let typeName, payload =
            match ev.Payload with
            | VehicleRegistered (VehicleId vid, plate, vtype) ->
                "VehicleRegistered",
                JsonSerializer.Serialize {| vehicleId=vid; licensePlate=plate; vehicleType=string vtype |}
            | VehicleLocationUpdated (VehicleId vid, loc, spd) ->
                "VehicleLocationUpdated",
                JsonSerializer.Serialize {| vehicleId=vid; lat=loc.Latitude; lon=loc.Longitude; speedKmh=spd |}
            | VehicleStatusChanged (VehicleId vid, oldS, newS) ->
                "VehicleStatusChanged",
                JsonSerializer.Serialize {| vehicleId=vid; oldStatus=string oldS; newStatus=string newS |}
            | VehicleFuelUpdated (VehicleId vid, pct) ->
                "VehicleFuelUpdated",
                JsonSerializer.Serialize {| vehicleId=vid; fuelPct=pct |}
            | RouteCreated (RouteId rid, VehicleId vid, algo) ->
                "RouteCreated",
                JsonSerializer.Serialize {| routeId=rid; vehicleId=vid; algorithm=string algo |}
            | RouteActivated (RouteId rid, ts) ->
                "RouteActivated",
                JsonSerializer.Serialize {| routeId=rid; activatedAt=ts |}
            | RouteCompleted (RouteId rid, ts, dur) ->
                "RouteCompleted",
                JsonSerializer.Serialize {| routeId=rid; completedAt=ts; durationMin=dur |}
            | RouteCancelled (RouteId rid, reason) ->
                "RouteCancelled",
                JsonSerializer.Serialize {| routeId=rid; reason=reason |}
            | FleetAlertRaised (msg, priority, vehicleId) ->
                "FleetAlertRaised",
                JsonSerializer.Serialize {| message=msg; priority=string priority
                                            vehicleId=vehicleId |> Option.map (fun (VehicleId v) -> string v) |}
            | _ ->
                "UnknownEvent", "{}"
        (eid, typeName, payload)

    interface IEventRepository with

        member _.Append ev = async {
            let (eid, typeName, payload) = serialize ev
            let sql = """
                INSERT INTO public.fms_domain_events (id, occurred_at, correlation_id, event_type, payload_json)
                VALUES (@id, @occurred_at, @correlation_id, @event_type, @payload::jsonb)
                ON CONFLICT (id) DO NOTHING"""
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute sql
                             {| id             = eid
                                occurred_at    = ev.OccurredAt
                                correlation_id = ev.CorrelationId
                                event_type     = typeName
                                payload        = payload |} conn tx
                return ()
            })
        }

        member _.GetByCorrelation corrId = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<EventRow>
                            "SELECT id, occurred_at, correlation_id, event_type, payload_json FROM public.fms_domain_events WHERE correlation_id = @id ORDER BY occurred_at"
                            {| id = corrId |} conn
            // Return raw rows as lightweight DTOs; full deserialization is optional
            return
                rows |> List.map (fun r ->
                    { EventId       = EventId r.id
                      OccurredAt    = r.occurred_at
                      CorrelationId = r.correlation_id
                      Payload       = FleetAlertRaised (r.event_type, Priority.Normal, None) }) // simplified
        }

        member _.GetRecent count = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<EventRow>
                            "SELECT id, occurred_at, correlation_id, event_type, payload_json FROM public.fms_domain_events ORDER BY occurred_at DESC LIMIT @count"
                            {| count = count |} conn
            return
                rows |> List.map (fun r ->
                    { EventId       = EventId r.id
                      OccurredAt    = r.occurred_at
                      CorrelationId = r.correlation_id
                      Payload       = FleetAlertRaised (r.event_type, Priority.Normal, None) })
        }
