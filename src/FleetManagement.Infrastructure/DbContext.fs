module FleetManagement.Infrastructure.DbContext

open System
open System.Data
open Npgsql
open Microsoft.Extensions.Logging

// ============================================================
//  Connection pool configuration
// ============================================================

type DbConfig = {
    ConnectionString : string
    MaxPoolSize      : int
    CommandTimeout   : int   // seconds
}

module DbConfig =
    let fromConnectionString (cs: string) = {
        ConnectionString = cs
        MaxPoolSize      = 50
        CommandTimeout   = 30
    }

// ============================================================
//  Database context
// ============================================================

type IDbContext =
    abstract OpenConnection : unit -> Async<IDbConnection>
    abstract InTransaction  : (IDbConnection -> IDbTransaction -> Async<'T>) -> Async<'T>

type PostgresDbContext(config: DbConfig, log: ILogger<PostgresDbContext>) =

    let buildDataSource () =
        let builder = NpgsqlDataSourceBuilder(config.ConnectionString)
        builder.EnableDynamicJson() |> ignore
        builder.Build()

    let dataSource = lazy (buildDataSource())

    interface IDbContext with
        member _.OpenConnection() = async {
            let conn = dataSource.Value.CreateConnection()
            do! conn.OpenAsync() |> Async.AwaitTask
            return conn :> IDbConnection
        }

        member this.InTransaction<'T>(work: IDbConnection -> IDbTransaction -> Async<'T>) = async {
            let! conn = (this :> IDbContext).OpenConnection()
            use conn = conn :?> NpgsqlConnection
            let! (tx : NpgsqlTransaction) = conn.BeginTransactionAsync().AsTask() |> Async.AwaitTask
            try
                let! result = work conn tx
                do! tx.CommitAsync() |> Async.AwaitTask
                return result
            with ex ->
                log.LogError(ex, "Transaction failed, rolling back")
                do! tx.RollbackAsync() |> Async.AwaitTask
                return raise ex
        }

// ============================================================
//  Dapper helpers for F#
// ============================================================

module Db =
    open Dapper

    let query<'T> (sql: string) (param: obj) (conn: IDbConnection) = async {
        let! result = conn.QueryAsync<'T>(sql, param) |> Async.AwaitTask
        return result |> Seq.toList
    }

    let queryFirst<'T> (sql: string) (param: obj) (conn: IDbConnection) = async {
        let! result = conn.QueryFirstOrDefaultAsync<'T>(sql, param) |> Async.AwaitTask
        return if obj.ReferenceEquals(result, null) then None else Some result
    }

    let execute (sql: string) (param: obj) (conn: IDbConnection) (tx: IDbTransaction) = async {
        let! rows = conn.ExecuteAsync(sql, param, tx) |> Async.AwaitTask
        return rows
    }

    let scalar<'T> (sql: string) (param: obj) (conn: IDbConnection) = async {
        let! result = conn.ExecuteScalarAsync<'T>(sql, param) |> Async.AwaitTask
        return result
    }
