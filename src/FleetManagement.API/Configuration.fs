module FleetManagement.API.Configuration

open System

// ============================================================
//  Strongly-typed configuration
// ============================================================

[<CLIMutable>]
type JwtConfig = {
    Issuer      : string
    Audience    : string
    SecretKey   : string
    ExpiryHours : int
}

[<CLIMutable>]
type AkkaConfig = {
    Hostname  : string
    Port      : int
    SeedNodes : string  // comma-separated "host:port" pairs
}

[<CLIMutable>]
type AppConfig = {
    ConnectionString : string
    Jwt              : JwtConfig
    Akka             : AkkaConfig
    AllowedOrigins   : string   // comma-separated CORS origins
}

module AppConfig =
    let seedNodesList (cfg: AkkaConfig) =
        cfg.SeedNodes.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun s -> s.Trim())
        |> Array.toList
