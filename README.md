# ⬡ FlitOS — Fleet Management System

A **highly scalable, high-performance, highly reliable, and highly secure** fleet management platform built with **F# 10 / .NET 10**, **Akka.NET 1.5 (Streams, Remoting, Cluster)**, **PostgreSQL**, and **Svelte 5**.  
Cloud-provider agnostic — runs anywhere Docker runs.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  Clients (browser)                                                  │
│  Svelte 5 SPA · Leaflet Live Map · Chart.js · SignalR WS client     │
└────────────────────────┬────────────────────────────────────────────┘
                         │ HTTP / WebSocket
┌────────────────────────▼────────────────────────────────────────────┐
│  Nginx  (reverse proxy, load balancer, static file server)          │
│  Round-robin upstream · Rate limiting · Security headers            │
└───────────┬────────────────────────────────────┬────────────────────┘
            │                                    │
┌───────────▼────────────┐          ┌────────────▼────────────────────┐
│  API Node 1            │          │  API Node 2                     │
│  F# / ASP.NET Core 10  │          │  F# / ASP.NET Core 10           │
│  Minimal API endpoints │◄────────►│  Minimal API endpoints          │
│  SignalR TelemetryHub  │ Akka TCP │  SignalR TelemetryHub           │
│  JWT auth · Rate limit │ Cluster  │  JWT auth · Rate limit          │
└───────────┬────────────┘          └──────────┬──────────────────────┘
            │                                  │
            └──────────────┬───────────────────┘
                           │  Akka.NET Cluster (gossip protocol)
┌──────────────────────────▼──────────────────────────────────────────┐
│  Actor Hierarchy                                                    │
│                                                                     │
│  FleetSupervisorActor           (guardian, health checks)           │
│  ├── VehicleActor×N             (one per vehicle, stateful)         │
│  ├── RouteCalculatorActor×pool  (parallel pathfinding)              │
│  └── TelemetryStreamActor       (Akka.Streams pipeline)             │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│  PostgreSQL 17                                                      │
│  vehicles · drivers · routes · domain_events · telemetry_archive   │
│  JSONB · GIN indexes · pg_trgm · partitioned telemetry table        │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Pathfinding Algorithms

All three algorithms are implemented from scratch in pure F# with no external graph libraries.

| Algorithm | Complexity | Best for | Handles negative weights |
|-----------|-----------|----------|--------------------------|
| **A\*** | O((V+E) log V) | Large sparse road networks | ✗ |
| **Dijkstra** | O((V+E) log V) | General purpose, moderate graphs | ✗ |
| **Bellman-Ford** | O(V·E) | Graphs with negative-weight edges | ✅ (detects cycles) |

`PathPlanner.autoSelect` automatically chooses:
- **Bellman-Ford** if the graph has negative-weight edges
- **A\*** for graphs with >10 000 nodes (sparse heuristic guidance helps most)
- **Dijkstra** for smaller, denser graphs

Multi-stop routing is supported via `PathPlanner.planMultiStop`, which stitches segment paths together into a single `PathResult`.

---

## Project Structure

```
FleetManagement/
├── src/
│   ├── FleetManagement.Core/          # Domain model, graph, algorithms, events, commands
│   │   ├── Domain.fs                  # All domain types (Vehicle, Route, Driver …)
│   │   ├── Graph.fs                   # RoadGraph, Edge, adjacency list, Haversine
│   │   ├── Pathfinding.fs             # A*, Dijkstra, Bellman-Ford, PathPlanner
│   │   ├── Validation.fs              # Railway-oriented validation DSL
│   │   ├── Events.fs                  # Domain event sourcing types
│   │   └── Commands.fs                # CQRS command types
│   │
│   ├── FleetManagement.Actors/        # Akka.NET actor system
│   │   ├── ActorMessages.fs           # All discriminated union message types
│   │   ├── VehicleActor.fs            # Stateful per-vehicle actor
│   │   ├── RouteCalculatorActor.fs    # Async pathfinding, coord→node resolution
│   │   ├── TelemetryStreamActor.fs    # Akka.Streams pipeline, ring buffers
│   │   ├── FleetSupervisorActor.fs    # Guardian, lifecycle management, alerts
│   │   └── ClusterBootstrap.fs        # HOCON config, cluster startup, wiring
│   │
│   ├── FleetManagement.Infrastructure/ # PostgreSQL persistence
│   │   ├── DbContext.fs               # Npgsql connection pool, transaction helper
│   │   ├── Repositories/
│   │   │   ├── VehicleRepository.fs
│   │   │   ├── DriverRepository.fs
│   │   │   ├── RouteRepository.fs
│   │   │   └── EventRepository.fs
│   │   └── Migrations/Schema.fs       # Versioned migration runner
│   │
│   ├── FleetManagement.API/           # ASP.NET Core 10 Minimal API
│   │   ├── Configuration.fs           # Strongly-typed app config
│   │   ├── Middleware/
│   │   │   ├── SecurityMiddleware.fs  # Security headers, correlation ID
│   │   │   └── RateLimitMiddleware.fs # Fixed-window rate limiting (3 policies)
│   │   ├── Hubs/TelemetryHub.fs       # SignalR real-time hub + broadcaster
│   │   ├── Endpoints/
│   │   │   ├── VehicleEndpoints.fs
│   │   │   ├── RouteEndpoints.fs
│   │   │   └── FleetEndpoints.fs
│   │   ├── Program.fs                 # DI wiring, middleware pipeline
│   │   └── appsettings.json
│   │
│   └── FleetManagement.Frontend/      # Svelte 5 SPA
│       └── src/
│           ├── lib/api.ts             # Typed API client + SignalR hub factory
│           ├── stores/fleet.ts        # Reactive Svelte stores
│           └── components/
│               ├── Dashboard.svelte   # KPI cards, Chart.js histograms
│               ├── FleetMap.svelte    # Leaflet dark map, live vehicle markers
│               ├── VehicleTable.svelte# Filterable, sortable vehicle table
│               ├── RoutePlanner.svelte# Algorithm picker + waypoint builder
│               └── AlertPanel.svelte  # Real-time alert feed
│
├── tests/
│   └── FleetManagement.Tests/
│       ├── GraphTests.fs              # 18 graph construction / cost tests
│       ├── PathfindingTests.fs        # 36 algorithm tests (A*, Dijkstra, BF, Planner)
│       ├── ValidationTests.fs         # 26 validation DSL tests
│       └── ActorTests.fs              # Akka.TestKit integration tests
│
├── docker/
│   ├── Dockerfile.api                 # Multi-stage .NET 10 build
│   └── Dockerfile.frontend            # Node 22 build → Nginx serve
│
├── infra/
│   ├── nginx/nginx.conf               # Reverse proxy, WS upgrade, rate limiting
│   └── postgres/init.sql              # Extensions, roles, indexes, monitoring views
│
└── docker-compose.yml                 # Full stack: PG + API×2 + frontend + nginx
```

---

## Quick Start

### Prerequisites

- Docker 26+ and Docker Compose v2
- (For local dev) .NET 10 SDK, Node 22+

### 1. Clone and configure

```bash
git clone <repo>
cd FleetManagement

# Copy and edit secrets
cp .env.example .env
# Set POSTGRES_PASSWORD and JWT_SECRET in .env
```

### 2. Run with Docker Compose

```bash
docker-compose up --build -d
```

Services start in dependency order:
1. **postgres** — waits until `pg_isready` passes
2. **api-node1** — runs DB migrations, joins Akka cluster seed
3. **api-node2** — joins cluster after node1 is healthy
4. **frontend** — builds Svelte app
5. **nginx** — exposes port 80

Open: **http://localhost**  
API docs: **http://localhost/swagger**

### 3. Local development

**Backend:**
```bash
cd src/FleetManagement.API
dotnet restore ../../FleetManagement.sln
dotnet run
# API at http://localhost:5000
# Swagger at http://localhost:5000/swagger
```

**Frontend:**
```bash
cd src/FleetManagement.Frontend
npm install
npm run dev
# UI at http://localhost:5173
```

**Tests:**
```bash
dotnet test tests/FleetManagement.Tests/
# Runs 80+ tests covering graph, pathfinding, validation, actors
```

---

## Configuration Reference

All configuration is environment-variable driven (12-factor):

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__Postgres` | `Host=postgres;...` | Full Npgsql connection string |
| `Jwt__SecretKey` | *(must set)* | Min 32-char HMAC secret |
| `Jwt__Issuer` | `fleet-api` | JWT issuer claim |
| `Jwt__Audience` | `fleet-client` | JWT audience claim |
| `Akka__Hostname` | `127.0.0.1` | This node's routable hostname |
| `Akka__Port` | `2551` | Akka remoting TCP port |
| `Akka__SeedNodes` | `127.0.0.1:2551` | Comma-separated seed node addresses |
| `AllowedOrigins` | `http://localhost` | Comma-separated CORS origins |
| `POSTGRES_PASSWORD` | `fleet` | PostgreSQL superuser password |

---

## REST API Reference

### Vehicles

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/vehicles` | List all vehicles |
| `GET` | `/api/vehicles/{id}` | Get single vehicle |
| `GET` | `/api/vehicles/status/{status}` | Filter by status |
| `POST` | `/api/vehicles` | Register new vehicle |
| `PATCH` | `/api/vehicles/{id}/location` | Update GPS location |
| `PATCH` | `/api/vehicles/{id}/status` | Change status |
| `POST` | `/api/vehicles/{id}/telemetry` | Ingest telemetry event |
| `DELETE` | `/api/vehicles/{id}` | Deregister vehicle |

### Routes

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/routes` | List all routes |
| `GET` | `/api/routes/active` | Active routes only |
| `GET` | `/api/routes/{id}` | Get single route |
| `GET` | `/api/routes/vehicle/{vehicleId}` | Routes for a vehicle |
| `POST` | `/api/routes/plan` | **Plan route** (runs pathfinding) |
| `POST` | `/api/routes/{id}/activate` | Start a planned route |
| `POST` | `/api/routes/{id}/complete` | Mark route complete |
| `POST` | `/api/routes/{id}/cancel` | Cancel route |

### Fleet

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/fleet/summary` | Aggregate fleet KPIs |
| `GET` | `/api/fleet/health` | Health check (used by Docker) |
| `POST` | `/api/fleet/alert` | Raise a manual fleet alert |

### WebSocket Hub

`ws://host/hubs/telemetry`

**Server → Client events:**

| Event | Payload |
|-------|---------|
| `OnTelemetry` | Vehicle location, speed, fuel, engine temp |
| `OnFleetSummary` | Aggregate KPI snapshot |
| `OnRouteUpdate` | Route status change |
| `OnAlert` | Fleet alert with priority |

**Client → Server:**

| Method | Args |
|--------|------|
| `SubscribeVehicle` | `vehicleId: string` |
| `UnsubscribeVehicle` | `vehicleId: string` |

---

## Security

- **JWT Bearer** authentication on all API routes
- **HTTPS** enforced via HSTS header (enable TLS termination at nginx in production)
- **Rate limiting**: 200 req/min global, 500 req/min telemetry, 30 req/min pathfinding
- **Security headers**: CSP, X-Frame-Options, HSTS, Referrer-Policy
- **Correlation IDs** on every request for distributed tracing
- **Least-privilege DB role** (`fleet_app`) — DML only, no DDL
- **Non-root Docker user** in API container
- **Internal Docker network** — PostgreSQL and API nodes are not reachable from outside

---

## Scaling

**Horizontal scaling** — add more API nodes to `docker-compose.yml`:

```yaml
api-node3:
  <<: *api-node1  # inherit config
  environment:
    Akka__Hostname: api-node3
    Akka__Port: 2553
    Akka__SeedNodes: api-node1:2551,api-node2:2551
```

Nginx automatically load-balances across all healthy upstream nodes. The Akka cluster self-heals; unreachable nodes are auto-downed after 30 seconds.

**Database scaling** — the telemetry table is range-partitioned by month. Add read replicas and point the Dapper queries at the replica connection string for read-heavy workloads.

---

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Language | F# 10 on .NET 10 |
| Actor system | Akka.NET 1.5 (Actors, Streams, Remoting, Cluster) |
| API | ASP.NET Core 10 Minimal API |
| Real-time | SignalR (WebSocket) |
| Database | PostgreSQL 17 + Npgsql 9 + Dapper 2 |
| Frontend | Svelte 5 + TypeScript |
| Maps | Leaflet.js (OpenStreetMap / CartoDB dark tiles) |
| Charts | Chart.js 4 |
| Serialization | Hyperion (Akka cluster), System.Text.Json (API) |
| Logging | Serilog (console + rolling file) |
| Auth | JWT Bearer |
| Proxy | Nginx 1.27 |
| Containers | Docker + Docker Compose |
| Testing | xUnit + FsUnit + Akka.TestKit |
