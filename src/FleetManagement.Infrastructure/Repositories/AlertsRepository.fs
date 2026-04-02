module FleetManagement.Infrastructure.Repositories.AlertsRepository

open System
open FleetManagement.Core.Domain
open FleetManagement.Infrastructure.DbContext
open FleetManagement.Infrastructure.IRepositories

[<CLIMutable>]
type AlertRow = {
    id          : Guid
    vehicle_id  : Guid Nullable
    message     : string
    issued_at   : DateTimeOffset
}

module private Mapping =
    let toDomain (a: AlertRow) : AlertRecord = {
        Id = AlertId a.id
        VehicleId = if a.vehicle_id.HasValue then Some (VehicleId a.vehicle_id.Value) else None
        Message = a.message
        IssuedAt = a.issued_at
    }

type PostgresAlertsRepository(ctx: IDbContext) =
    interface IAlertsRepository with
        member _.Insert(alert: AlertRecord) = async {

            let (AlertId aid) = alert.Id;
            let vid = alert.VehicleId |> Option.map (fun (VehicleId v) -> v) |> Option.toNullable

            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            """INSERT INTO public.fms_alerts (id, vehicle_id, message, issued_at)
                               VALUES (@id, @vehicleId, @message, @issuedAt)"""
                            {| id           = aid
                               vehicleId    = vid
                               message      = alert.Message
                               issuedAt     = alert.IssuedAt |} conn tx
                return ()
            })
        }

        member _.GetAll() = async {
            let! conn = ctx.OpenConnection()
            let! rows = Db.query<AlertRow>
                            """SELECT id, vehicle_id, message, issued_at
                               FROM public.fms_alerts
                               ORDER BY issued_at DESC
                               LIMIT 100"""
                            {| |} conn
            return rows |> List.map Mapping.toDomain
        }