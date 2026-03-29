-- ============================================================
--  PostgreSQL initialisation — runs once on first container start
-- ============================================================

-- Extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pg_trgm";       -- fuzzy text search on plates
CREATE EXTENSION IF NOT EXISTS "btree_gin";      -- GIN indexes for JSONB

-- ── Application role (least-privilege) ────────────────────────
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'fleet_app') THEN
    CREATE ROLE fleet_app LOGIN PASSWORD 'fleet_app_secret_CHANGE_ME';
  END IF;
END
$$;

GRANT CONNECT ON DATABASE fleetdb TO fleet_app;
GRANT USAGE   ON SCHEMA  public  TO fleet_app;

-- Grant only DML — DDL stays with the migration runner (fleet user)
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fleet_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO fleet_app;

-- ── PostgreSQL performance tuning ─────────────────────────────
-- These are advisory — real tuning lives in postgresql.conf.
-- Included here as documentation of recommended settings:
--
--   shared_buffers         = 256MB
--   effective_cache_size   = 1GB
--   work_mem               = 16MB
--   maintenance_work_mem   = 128MB
--   max_connections        = 100
--   checkpoint_completion_target = 0.9
--   wal_buffers            = 16MB
--   default_statistics_target = 100
--   random_page_cost       = 1.1   # SSD
--   effective_io_concurrency = 200 # SSD

-- ── Indexes that benefit the application queries ───────────────
-- (Most are created by migration 2-6, but extras for GIN/TRGM)

-- Trigram index for license plate fuzzy search
-- (created after the vehicles table exists — safe to fail on first run)
DO $$
BEGIN
  IF EXISTS (SELECT FROM information_schema.tables
             WHERE table_schema = 'public' AND table_name = 'fms_vehicles') THEN
    EXECUTE 'CREATE INDEX IF NOT EXISTS idx_vehicles_plate_trgm
             ON fms_vehicles USING gin (license_plate gin_trgm_ops)';
  END IF;
END
$$;

-- ── Useful monitoring views ────────────────────────────────────
CREATE OR REPLACE VIEW fleet_overview AS
SELECT
    v.status,
    COUNT(*)                          AS vehicle_count,
    ROUND(AVG(v.fuel_level_pct)::numeric, 1) AS avg_fuel_pct,
    COUNT(r.id) FILTER (WHERE r.status = 'Active') AS active_routes
FROM fms_vehicles v
LEFT JOIN fms_routes r ON r.vehicle_id = v.id
GROUP BY v.status;

COMMENT ON VIEW fleet_overview IS 'Live fleet status breakdown — refreshes on each query';

-- ── Row-level security (optional, enable per deployment) ──────
-- ALTER TABLE vehicles ENABLE ROW LEVEL SECURITY;
-- CREATE POLICY vehicle_isolation ON vehicles
--     USING (true);  -- replace with tenant_id check for multi-tenancy
