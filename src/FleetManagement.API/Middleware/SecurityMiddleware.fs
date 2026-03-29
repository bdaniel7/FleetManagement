module FleetManagement.API.Middleware.SecurityMiddleware

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open System.Threading.Tasks

// ============================================================
//  Security headers middleware
// ============================================================

type SecurityHeadersMiddleware(next: RequestDelegate) =
    member _.InvokeAsync(ctx: HttpContext) : Task =
        let h = ctx.Response.Headers
        h["X-Content-Type-Options"]           <- "nosniff"
        h["X-Frame-Options"]                  <- "DENY"
        h["X-XSS-Protection"]                 <- "1; mode=block"
        h["Referrer-Policy"]                  <- "strict-origin-when-cross-origin"
        h["Permissions-Policy"]               <- "geolocation=(), microphone=()"
        h["Content-Security-Policy"]          <- "default-src 'self'; connect-src 'self' ws: wss:"
        h["Strict-Transport-Security"]        <- "max-age=31536000; includeSubDomains"
        next.Invoke(ctx)

// ============================================================
//  Request correlation ID middleware
// ============================================================

type CorrelationMiddleware(next: RequestDelegate) =
    member _.InvokeAsync(ctx: HttpContext) : Task =
        let correlationId =
            match ctx.Request.Headers.TryGetValue "X-Correlation-ID" with
            | true, v when v.Count > 0 -> v.[0]
            | _ -> System.Guid.NewGuid().ToString("N")
        ctx.Items.["CorrelationId"] <- correlationId
        ctx.Response.Headers["X-Correlation-ID"] <- correlationId
        next.Invoke(ctx)

// ============================================================
//  Extension method for middleware registration
// ============================================================

[<System.Runtime.CompilerServices.Extension>]
type ApplicationBuilderExtensions =
    [<System.Runtime.CompilerServices.Extension>]
    static member UseSecurityHeaders(app: IApplicationBuilder) =
        app.UseMiddleware<SecurityHeadersMiddleware>()

    [<System.Runtime.CompilerServices.Extension>]
    static member UseCorrelationId(app: IApplicationBuilder) =
        app.UseMiddleware<CorrelationMiddleware>()
