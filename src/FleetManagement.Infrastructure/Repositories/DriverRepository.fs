module FleetManagement.Infrastructure.Repositories.DriverRepository

open System
open System.Data
open FleetManagement.Core.Domain
open FleetManagement.Infrastructure.DbContext
open FleetManagement.Infrastructure.IRepositories

[<CLIMutable>]
type DriverRow = {
    id              : Guid
    first_name      : string
    last_name       : string
    license_number  : string
    phone_number    : string
    email           : string
    is_available    : bool
    hours_worked    : float
    max_hours_per_day: float
    created_at      : DateTimeOffset
}

module private Mapping =
    let toDomain (r: DriverRow) : Driver = {
        Id             = DriverId r.id
        FirstName      = r.first_name
        LastName       = r.last_name
        LicenseNumber  = r.license_number
        PhoneNumber    = r.phone_number
        Email          = r.email
        IsAvailable    = r.is_available
        HoursWorked    = r.hours_worked
        MaxHoursPerDay = r.max_hours_per_day
        CreatedAt      = r.created_at
    }

type PostgresDriverRepository(ctx: IDbContext) =

    let selectAll = """
        SELECT id, first_name, last_name, license_number,
               phone_number, email, is_available,
               hours_worked, max_hours_per_day, created_at
        FROM public.fms_drivers"""

    interface IDriverRepository with

        member _.GetById (DriverId did) = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! row = Db.queryFirst<DriverRow>
                           $"{selectAll} WHERE id = @id" {| id = did |} conn
            return row |> Option.map Mapping.toDomain
        }

        member _.GetAll () = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<DriverRow>
                            $"{selectAll} ORDER BY last_name, first_name" {| |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.GetAvailable () = async {
            let! conn = ctx.OpenConnection()
            use conn = conn :?> Npgsql.NpgsqlConnection
            let! rows = Db.query<DriverRow>
                            $"{selectAll} WHERE is_available = true ORDER BY last_name" {| |} conn
            return rows |> List.map Mapping.toDomain
        }

        member _.Upsert driver = async {
            let (DriverId did) = driver.Id
            let sql = """
                INSERT INTO public.fms_drivers (id, first_name, last_name, license_number,
                    phone_number, email, is_available, hours_worked, max_hours_per_day, created_at)
                VALUES (@id, @first_name, @last_name, @license_number,
                    @phone_number, @email, @is_available, @hours_worked, @max_hours_per_day, @created_at)
                ON CONFLICT (id) DO UPDATE SET
                    first_name       = EXCLUDED.first_name,
                    last_name        = EXCLUDED.last_name,
                    phone_number     = EXCLUDED.phone_number,
                    email            = EXCLUDED.email,
                    is_available     = EXCLUDED.is_available,
                    hours_worked     = EXCLUDED.hours_worked,
                    max_hours_per_day= EXCLUDED.max_hours_per_day"""
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute sql
                             {| id = did; first_name = driver.FirstName; last_name = driver.LastName
                                license_number = driver.LicenseNumber; phone_number = driver.PhoneNumber
                                email = driver.Email; is_available = driver.IsAvailable
                                hours_worked = driver.HoursWorked; max_hours_per_day = driver.MaxHoursPerDay
                                created_at = driver.CreatedAt |} conn tx
                return ()
            })
        }

        member _.SetAvailability (DriverId did, avail) = async {
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            "UPDATE public.fms_drivers SET is_available = @avail WHERE id = @id"
                            {| id=did; avail=avail |} conn tx
                return ()
            })
        }

        member _.LogHours (DriverId did, hours) = async {
            do! ctx.InTransaction(fun conn tx -> async {
                let! _ = Db.execute
                            "UPDATE public.fms_drivers SET hours_worked = hours_worked + @hours WHERE id = @id"
                            {| id=did; hours=hours |} conn tx
                return ()
            })
        }
