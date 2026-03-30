module FleetManagement.API.Program

open System
open System.Text.Json
open System.Text.Json.Serialization
open FleetManagement.API.Endpoints
open FleetManagement.API.Endpoints.TripEndpoints
open FleetManagement.Core.Domain
open FleetManagement.Infrastructure.IRepositories
open FleetManagement.Infrastructure.Repositories.TripRepository
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http.Json
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.OpenApi
open Microsoft.Extensions.Logging
open Serilog
open Serilog.Events
open FleetManagement.Core.Events
open FleetManagement.Actors.ActorMessages
open FleetManagement.Actors.ClusterBootstrap
open FleetManagement.Infrastructure.DbContext
open FleetManagement.Infrastructure.Migrations
open FleetManagement.Infrastructure.Repositories.VehicleRepository
open FleetManagement.Infrastructure.Repositories.DriverRepository
open FleetManagement.Infrastructure.Repositories.RouteRepository
open FleetManagement.Infrastructure.Repositories.EventRepository
open FleetManagement.API.Middleware.RateLimitMiddleware
open FleetManagement.API.Hubs.TelemetryHub
open FleetManagement.API.Endpoints.VehicleEndpoints
open FleetManagement.API.Endpoints.RouteEndpoints
open FleetManagement.API.Endpoints.FleetEndpoints

let configureSerilog () =
    Log.Logger <-
        LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Akka",      LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/fleet-.log",
                rollingInterval     = RollingInterval.Day,
                retainedFileCountLimit = 14,
                outputTemplate      = "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger()

[<EntryPoint>]
let main args =
    configureSerilog()

    try
        let builder = WebApplication.CreateBuilder(args)
        builder.Host.UseSerilog() |> ignore

        let cfg = builder.Configuration
        let connStr       = cfg.GetConnectionString("Postgres")
                                    |> Option.ofObj
                                    |> Option.defaultValue "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=6EaL64EkXfGDmm5wZCE0"
        let jwtKey        = cfg.["Jwt:SecretKey"]      |> Option.ofObj |> Option.defaultValue "LqsV26coqNKnANgzICqrSOCdaRnY1hTCdSKhfiCGKVZ"
        let jwtIssuer     = cfg.["Jwt:Issuer"]         |> Option.ofObj |> Option.defaultValue "fleet-api"
        let jwtAudience   = cfg.["Jwt:Audience"]       |> Option.ofObj |> Option.defaultValue "fleet-client"
        let akkaHost      = cfg.["Akka:Hostname"]      |> Option.ofObj |> Option.defaultValue "127.0.0.1"
        let akkaPort         = cfg.["Akka:Port"]          |> Option.ofObj |> Option.map int |> Option.defaultValue 2551
        let akkaSeedNodes = cfg.["Akka:SeedNodes"]     |> Option.ofObj |> Option.defaultValue "127.0.0.1:2551"
        let allowedOrigins= cfg.["AllowedOrigins"]     |> Option.ofObj |> Option.defaultValue "http://localhost:5173"

        let services = builder.Services

        // PostgreSQL
        let dbConfig = DbConfig.fromConnectionString connStr
        services.AddSingleton<IDbContext>(fun sp ->
            let log = sp.GetRequiredService<ILogger<PostgresDbContext>>()
            PostgresDbContext(dbConfig, log) :> IDbContext) |> ignore

        // Repositories
        services.AddScoped<IVehicleRepository, PostgresVehicleRepository>() |> ignore
        services.AddScoped<IDriverRepository,  PostgresDriverRepository>()  |> ignore
        services.AddScoped<IRouteRepository,   PostgresRouteRepository>()   |> ignore
        services.AddScoped<IEventRepository,   PostgresEventRepository>()   |> ignore
        services.AddScoped<ITripsRepository,   PostgresTripsRepository>()   |> ignore

        services.Configure<JsonOptions> (fun (opts: JsonOptions) ->
            opts.SerializerOptions.Converters.Add(VehicleStatusConverter())) |> ignore

        services.Configure<JsonOptions> (fun (opts: JsonOptions) ->
            opts.SerializerOptions.Converters.Add(VehicleTypeConverter())) |> ignore

        services.Configure<JsonOptions> (fun (opts: JsonOptions) ->
            opts.SerializerOptions.Converters.Add(PriorityConverter())) |> ignore

        services.Configure<JsonOptions> (fun (opts: JsonOptions) ->
            opts.SerializerOptions.Converters.Add(RouteStatusConverter())) |> ignore

        services.Configure<JsonOptions> (fun (opts: JsonOptions) ->
            opts.SerializerOptions.Converters.Add(AlgorithmConverter())) |> ignore

        services.Configure<JsonOptions> (fun (opts: JsonOptions) ->
            opts.SerializerOptions.Converters.Add(TripStatusConverter())) |> ignore

        services.ConfigureHttpJsonOptions(fun o ->
                        o.SerializerOptions.PropertyNameCaseInsensitive <- true
                        o.SerializerOptions.PropertyNamingPolicy        <- JsonNamingPolicy.CamelCase
                        o.SerializerOptions.DefaultIgnoreCondition      <- JsonIgnoreCondition.WhenWritingNull
                        o.SerializerOptions.Converters.Add(
                            JsonFSharpConverter(JsonUnionEncoding.Default ||| JsonUnionEncoding.UnwrapSingleCaseUnions))
                    ) |> ignore

        // SignalR
        services.AddSignalR(fun opts ->
            opts.EnableDetailedErrors      <- builder.Environment.IsDevelopment()
            opts.MaximumReceiveMessageSize <- 64L * 1024L) |> ignore
        services.AddSingleton<IFleetHubBroadcaster, FleetHubBroadcaster>() |> ignore

        // Rate limiting
        configure services |> ignore

        // CORS
        services.AddCors(fun opts ->
            opts.AddDefaultPolicy(fun policy ->
                policy
                    .WithOrigins(allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                |> ignore)) |> ignore

        // JWT Auth
        // services
        //     .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        //     .AddJwtBearer(fun opts ->
        //         opts.TokenValidationParameters <- TokenValidationParameters(
        //             ValidateIssuer           = true,
        //             ValidateAudience         = true,
        //             ValidateLifetime         = true,
        //             ValidateIssuerSigningKey = true,
        //             ValidIssuer              = jwtIssuer,
        //             ValidAudience            = jwtAudience,
        //             IssuerSigningKey         = SymmetricSecurityKey(Encoding.UTF8.GetBytes jwtKey))
        //         // Allow JWT from SignalR query string
        //         opts.Events <- JwtBearerEvents(
        //             OnMessageReceived = fun ctx ->
        //                 let token = ctx.Request.Query.["access_token"]
        //                 if ctx.Request.Path.StartsWithSegments "/hubs" && not (String.IsNullOrEmpty (string token)) then
        //                     ctx.Token <- string token
        //                 System.Threading.Tasks.Task.CompletedTask)) |> ignore
        // services.AddAuthorization() |> ignore

        // Swagger
        services.AddEndpointsApiExplorer() |> ignore
        services.AddSwaggerGen(fun opts ->
            opts.SwaggerDoc("v1", OpenApiInfo(
                Title   = "Fleet Management API",
                Version = "v1",
                Description = "High-performance fleet management with A*, Dijkstra, Bellman-Ford pathfinding"))) |> ignore

        // Akka.NET Actor System (singleton lifetime)
        services.AddSingleton<FleetActorSystem>(fun sp ->
            let broadcaster   = sp.GetRequiredService<IFleetHubBroadcaster>()
            let eventRepo     = sp.GetRequiredService<IEventRepository>()
            let seedNodes     = akkaSeedNodes.Split(',') |> Array.map (fun s -> s.Trim()) |> Array.toList
            let publishEvent  = fun (ev: DomainEvent) ->
                eventRepo.Append ev |> Async.Start
            let broadcastTelemetry = fun (ev: TelemetryEvent) ->
                broadcaster.BroadcastTelemetry ev |> ignore
            start akkaHost akkaPort seedNodes publishEvent broadcastTelemetry) |> ignore

        // ── Build app ─────────────────────────────────────────────
        let app = builder.Build()

        // Run DB migrations
        Schema.run connStr |> Async.RunSynchronously

        if app.Environment.IsDevelopment() then
            app.UseSwagger()    |> ignore
            app.UseSwaggerUI()  |> ignore

        app.UseHttpsRedirection()                          |> ignore
        app.UseMiddleware<Middleware.SecurityMiddleware.SecurityHeadersMiddleware>()  |> ignore
        app.UseMiddleware<Middleware.SecurityMiddleware.CorrelationMiddleware>()      |> ignore
        app.UseCors()                                      |> ignore
        app.UseRateLimiter()                               |> ignore
        // app.UseAuthentication()                            |> ignore
        // app.UseAuthorization()                             |> ignore

        // SignalR hub
        app.MapHub<TelemetryHub>("/hubs/telemetry") |> ignore

        // REST endpoints
        let actorSystem = app.Services.GetRequiredService<FleetActorSystem>()
        let vehicleRepo = app.Services.GetRequiredService<IVehicleRepository>()
        let routeRepo   = app.Services.GetRequiredService<IRouteRepository>()
        let tripsRepo = app.Services.GetRequiredService<ITripsRepository>()

        mapVehicleEndpoints app vehicleRepo actorSystem.FleetSupervisor
        mapRouteEndpoints   app routeRepo   actorSystem.RouteCalculator
        mapFleetEndpoints   app actorSystem.FleetSupervisor actorSystem.RouteCalculator
        mapTripsEndpoints app tripsRepo

        // Graceful shutdown
        let lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>()
        lifetime.ApplicationStopping.Register(fun () ->
            Log.Information("Shutting down Akka cluster...")
            stop actorSystem) |> ignore

        Log.Information("Fleet Management API starting on port 5000")
        app.Run("http://0.0.0.0:5000")
        0

    with ex ->
        Log.Fatal(ex, "Application terminated unexpectedly")
        1
