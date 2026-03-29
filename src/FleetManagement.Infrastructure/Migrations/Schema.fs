module FleetManagement.Infrastructure.Migrations.Schema

open Npgsql

// ============================================================
//  Versioned SQL migrations run in order at startup
// ============================================================

type Migration = {
    Version  : int
    Name     : string
    UpSql    : string
}

let migrations = [
    {
        Version = 1
        Name    = "create_schema_version"
        UpSql   = """
            CREATE TABLE IF NOT EXISTS public.fms_schema_version (
                version     INT         PRIMARY KEY,
                name        TEXT        NOT NULL,
                applied_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );"""
    }
    {
        Version = 2
        Name    = "create_drivers"
        UpSql   = """
            CREATE TABLE IF NOT EXISTS public.fms_drivers (
                id                  UUID        PRIMARY KEY,
                first_name          TEXT        NOT NULL,
                last_name           TEXT        NOT NULL,
                license_number      TEXT        NOT NULL UNIQUE,
                phone_number        TEXT        NOT NULL,
                email               TEXT        NOT NULL UNIQUE,
                is_available        BOOLEAN     NOT NULL DEFAULT true,
                hours_worked        DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                max_hours_per_day   DOUBLE PRECISION NOT NULL DEFAULT 8.0,
                created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_drivers_available ON public.fms_drivers(is_available);"""
    }
    {
        Version = 3
        Name    = "create_vehicles"
        UpSql   = """
            CREATE TABLE IF NOT EXISTS public.fms_vehicles (
                id                  UUID        PRIMARY KEY,
                license_plate       TEXT        NOT NULL UNIQUE,
                vehicle_type        TEXT        NOT NULL,
                status              TEXT        NOT NULL DEFAULT 'Idle',
                lat                 DOUBLE PRECISION NOT NULL,
                lon                 DOUBLE PRECISION NOT NULL,
                assigned_driver_id  UUID        REFERENCES public.fms_drivers(id) ON DELETE SET NULL,
                fuel_level_pct      DOUBLE PRECISION NOT NULL DEFAULT 100.0,
                speed_kmh           DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                max_payload_kg      DOUBLE PRECISION NOT NULL,
                current_payload_kg  DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                odometer_km         DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                engine_temp         DOUBLE PRECISION NOT NULL DEFAULT 20.0,
                battery_level       DOUBLE PRECISION,
                last_heartbeat      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                diagnostic_codes    JSONB       NOT NULL DEFAULT '[]',
                created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_vehicles_status    ON public.fms_vehicles(status);
            CREATE INDEX IF NOT EXISTS idx_vehicles_driver_id ON public.fms_vehicles(assigned_driver_id);"""
    }
    {
        Version = 4
        Name    = "create_routes"
        UpSql   = """
            CREATE TABLE IF NOT EXISTS public.fms_routes (
                id                      UUID        PRIMARY KEY,
                vehicle_id              UUID        NOT NULL REFERENCES fms_vehicles(id) ON DELETE CASCADE,
                driver_id               UUID        REFERENCES fms_drivers(id) ON DELETE SET NULL,
                total_distance_km       DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                estimated_duration_min  INT         NOT NULL DEFAULT 0,
                status                  TEXT        NOT NULL DEFAULT 'Planned',
                priority                TEXT        NOT NULL DEFAULT 'Normal',
                algorithm               TEXT        NOT NULL DEFAULT 'AStar',
                waypoints_json          JSONB       NOT NULL DEFAULT '[]',
                optimized_path_json     JSONB       NOT NULL DEFAULT '[]',
                created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                started_at              TIMESTAMPTZ,
                completed_at            TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS idx_routes_vehicle_id ON public.fms_routes(vehicle_id);
            CREATE INDEX IF NOT EXISTS idx_routes_status     ON public.fms_routes(status);
            CREATE INDEX IF NOT EXISTS idx_routes_created_at ON public.fms_routes(created_at DESC);"""
    }
    {
        Version = 5
        Name    = "create_domain_events"
        UpSql   = """
            CREATE TABLE IF NOT EXISTS public.fms_domain_events (
                id              UUID        PRIMARY KEY,
                occurred_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                correlation_id  UUID        NOT NULL,
                event_type      TEXT        NOT NULL,
                payload_json    JSONB       NOT NULL DEFAULT '{}'
            );
            CREATE INDEX IF NOT EXISTS idx_events_correlation ON public.fms_domain_events(correlation_id);
            CREATE INDEX IF NOT EXISTS idx_events_occurred_at ON public.fms_domain_events(occurred_at DESC);
            CREATE INDEX IF NOT EXISTS idx_events_type        ON public.fms_domain_events(event_type);"""
    }
    {
        Version = 6
        Name    = "create_telemetry_archive"
        UpSql   = """
            CREATE TABLE IF NOT EXISTS public.fms_telemetry_archive (
                id              BIGSERIAL   PRIMARY KEY,
                vehicle_id      UUID        NOT NULL,
                recorded_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                lat             DOUBLE PRECISION NOT NULL,
                lon             DOUBLE PRECISION NOT NULL,
                speed_kmh       DOUBLE PRECISION NOT NULL,
                fuel_pct        DOUBLE PRECISION NOT NULL,
                engine_temp     DOUBLE PRECISION NOT NULL,
                odometer_km     DOUBLE PRECISION NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_telemetry_vehicle_time
                ON public.fms_telemetry_archive(vehicle_id, recorded_at DESC);"""

            //         PARTITION BY RANGE (recorded_at);
            //
            // -- Create monthly partition for current month
            // CREATE TABLE IF NOT EXISTS public.telemetry_archive_current
            // PARTITION OF public.fms_telemetry_archive
            // FOR VALUES FROM (DATE_TRUNC('month', NOW()))
            //             TO   (DATE_TRUNC('month', NOW()) + INTERVAL '1 month');
    }
]

// ============================================================
//  Migration runner
// ============================================================

let run (connectionString: string) = async {
    use conn = new NpgsqlConnection(connectionString)
    do! conn.OpenAsync() |> Async.AwaitTask

    // Ensure schema_version table exists first
    use cmd = conn.CreateCommand()
    cmd.CommandText <- """
        CREATE TABLE IF NOT EXISTS public.fms_schema_version (
            version     INT         PRIMARY KEY,
            name        TEXT        NOT NULL,
            applied_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );"""
    do! cmd.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore

    // Get applied versions
    cmd.CommandText <- "SELECT version FROM public.fms_schema_version ORDER BY version"
    let! reader      = cmd.ExecuteReaderAsync() |> Async.AwaitTask
    let applied      = System.Collections.Generic.HashSet<int>()
    while reader.Read() do
        applied.Add(reader.GetInt32(0)) |> ignore
    do! reader.CloseAsync() |> Async.AwaitTask

    // Apply pending migrations
    for migration in migrations do
        if not (applied.Contains migration.Version) then
            printfn "Applying migration %d: %s" migration.Version migration.Name
            let! (tx : NpgsqlTransaction) = conn.BeginTransactionAsync().AsTask() |> Async.AwaitTask
            try
                cmd.Transaction <- tx
                cmd.CommandText <- migration.UpSql
                do! cmd.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore

                cmd.CommandText <- "INSERT INTO public.fms_schema_version (version, name) VALUES (@v, @n)"
                cmd.Parameters.Clear()
                cmd.Parameters.AddWithValue("v", migration.Version) |> ignore
                cmd.Parameters.AddWithValue("n", migration.Name)    |> ignore
                do! cmd.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore

                do! tx.CommitAsync() |> Async.AwaitTask
                printfn "  ✅ Migration %d applied" migration.Version
            with ex ->
                do! tx.RollbackAsync() |> Async.AwaitTask
                failwithf "Migration %d failed: %s" migration.Version ex.Message
}
