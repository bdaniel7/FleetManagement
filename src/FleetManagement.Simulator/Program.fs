module FleetManagement.Simulator.Program

open System
open System.Threading
open Spectre.Console
open Types

// ── Help text ─────────────────────────────────────────────────

let private printHelp () =
    AnsiConsole.MarkupLine("[bold teal]FlitOS Vehicle Simulator[/]")
    AnsiConsole.MarkupLine("[grey]Generates realistic vehicle telemetry and posts it to the FlitOS API.[/]")
    printfn ""
    AnsiConsole.MarkupLine("[bold]USAGE:[/]")
    printfn "  FleetManagement.Simulator [OPTIONS]"
    printfn ""
    AnsiConsole.MarkupLine("[bold]OPTIONS:[/]")
    let opts = [
        "--api",       "<url>",   "API base URL (default: http://localhost:5000)"
        "--vehicles",  "<n>",     "Number of vehicles to simulate (default: all)"
        "--tick",      "<ms>",    "Milliseconds between ticks (default: 2000)"
        "--ticks",     "<n>",     "Total ticks to run then exit (default: run forever)"
        "--speed-min", "<km/h>",  "Minimum vehicle speed (default: 40)"
        "--speed-max", "<km/h>",  "Maximum vehicle speed (default: 120)"
        "--fuel-burn", "<rate>",  "Fuel burn %% per km (default: 0.08)"
        "--verbose",   "",        "Print per-vehicle details every tick"
        "--help",      "",        "Show this help"
    ]
    opts |> List.iter (fun (flag, arg, desc) ->
        AnsiConsole.MarkupLine($"  [green]{flag}[/] [yellow]{arg}[/]")
        printfn "      %s" desc)
    printfn ""
    AnsiConsole.MarkupLine("[bold]EXAMPLES:[/]")
    printfn "  # Simulate all vehicles, tick every 1s:"
    printfn "  dotnet run -- --tick 1000"
    printfn ""
    printfn "  # Simulate 5 vehicles for 60 ticks then exit:"
    printfn "  dotnet run -- --vehicles 5 --ticks 60"
    printfn ""
    printfn "  # Connect to a remote API:"
    printfn "  dotnet run -- --api http://192.168.1.100:5000 --verbose"
    printfn ""

// ── Argument parser ───────────────────────────────────────────

let private parseArgs (argv: string[]) =
    let mutable opts = SimOptions.defaults
    let mutable i    = 0
    let mutable ok   = true

    let next () =
        i <- i + 1
        if i >= argv.Length then
            AnsiConsole.MarkupLine($"[red]Error: missing argument after '{argv.[i-1]}'[/]")
            ok <- false
            ""
        else argv.[i]

    while i < argv.Length && ok do
        match argv.[i] with
        | "--help" | "-h" ->
            printHelp ()
            Environment.Exit 0

        | "--api" ->
            opts <- { opts with ApiBaseUrl = next() }

        | "--vehicles" ->
            match Int32.TryParse(next()) with
            | true, n when n > 0 -> opts <- { opts with VehicleCount = n }
            | _ ->
                AnsiConsole.MarkupLine("[red]Error: --vehicles must be a positive integer[/]")
                ok <- false

        | "--tick" ->
            match Int32.TryParse(next()) with
            | true, n when n >= 100 -> opts <- { opts with TickMs = n }
            | _ ->
                AnsiConsole.MarkupLine("[red]Error: --tick must be >= 100 ms[/]")
                ok <- false

        | "--ticks" ->
            match Int32.TryParse(next()) with
            | true, n when n > 0 -> opts <- { opts with TotalTicks = n }
            | _ ->
                AnsiConsole.MarkupLine("[red]Error: --ticks must be a positive integer[/]")
                ok <- false

        | "--speed-min" ->
            match Double.TryParse(next()) with
            | true, v -> opts <- { opts with SpeedMin = v }
            | _ ->
                AnsiConsole.MarkupLine("[red]Error: --speed-min must be a number[/]")
                ok <- false

        | "--speed-max" ->
            match Double.TryParse(next()) with
            | true, v -> opts <- { opts with SpeedMax = v }
            | _ ->
                AnsiConsole.MarkupLine("[red]Error: --speed-max must be a number[/]")
                ok <- false

        | "--fuel-burn" ->
            match Double.TryParse(next()) with
            | true, v when v >= 0.0 -> opts <- { opts with FuelBurnRate = v }
            | _ ->
                AnsiConsole.MarkupLine("[red]Error: --fuel-burn must be a non-negative number[/]")
                ok <- false

        | "--verbose" | "-v" ->
            opts <- { opts with Verbose = true }

        | unknown ->
            AnsiConsole.MarkupLine($"[red]Error: unknown option '{unknown}'. Use --help for usage.[/]")
            ok <- false

        i <- i + 1

    if ok then Some opts else None

// ── Banner ────────────────────────────────────────────────────

let private printBanner (opts: SimOptions) =
    let rule = Rule("[bold teal]FlitOS Vehicle Simulator[/]")
    rule.Justification <- Justify.Left
    AnsiConsole.Write(rule)
    printfn ""
    AnsiConsole.MarkupLine($"  API URL    : [cyan]{opts.ApiBaseUrl}[/]")
    AnsiConsole.MarkupLine($"""  Vehicles   : [cyan]{if opts.VehicleCount <= 0 then "all" else string opts.VehicleCount}[/]""")
    AnsiConsole.MarkupLine($"  Tick       : [cyan]{opts.TickMs} ms[/]")
    AnsiConsole.MarkupLine($"""  Duration   : [cyan]{if opts.TotalTicks <= 0 then "infinite (Ctrl+C to stop)" else string opts.TotalTicks + " ticks"}[/]""")
    AnsiConsole.MarkupLine($"  Speed range: [cyan]{opts.SpeedMin}–{opts.SpeedMax} km/h[/]")
    AnsiConsole.MarkupLine($"  Fuel burn  : [cyan]{opts.FuelBurnRate}%% per km[/]")
    printfn ""

// ── Entry point ───────────────────────────────────────────────

[<EntryPoint>]
let main argv =
    match parseArgs argv with
    | None -> 1
    | Some opts ->

    printBanner opts

    // Graceful shutdown on Ctrl+C
    use cts = new CancellationTokenSource()
    Console.CancelKeyPress.Add(fun e ->
        e.Cancel <- true   // don't terminate immediately
        AnsiConsole.MarkupLine("\n  [yellow]Stopping simulation…[/]")
        cts.Cancel())

    try
        Simulator.run opts cts.Token |> Async.RunSynchronously
        0
    with
    | :? OperationCanceledException -> 0
    | ex ->
        AnsiConsole.MarkupLine($"[red]Fatal error: {ex.Message}[/]")
        1
