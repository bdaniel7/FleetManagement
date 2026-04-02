module FleetManagement.Core.Tracing

open System
open System.Diagnostics

let getTraceId (activity: Activity option) : string option =
    match activity with
    | Some a -> Some (a.TraceId.ToString())
    | None -> None

let getSpanId (activity: Activity option) : string option =
    match activity with
    | Some a -> Some (a.SpanId.ToString())
    | None -> None

let dispose (activity: Activity option) : unit =
    match activity with
    | Some a -> a.Dispose()
    | None -> ()

let setTag (key: string) (value: string) (activity: Activity option): Activity option =
    match activity with
    | Some a ->
        a.SetTag(key, value) |> ignore
        Some a
    | None -> None

let setTagInt (key: string) (value: int)  (activity: Activity option) : Activity option =
    match activity with
    | Some a ->
        a.SetTag(key, value) |> ignore
        Some a
    | None -> None

let setTagGuid (key: string) (value: Guid) (activity: Activity option) : Activity option =
    match activity with
    | Some a ->
        a.SetTag(key, value) |> ignore
        Some a
    | None -> None

let setTagBool (key: string) (value: bool) (activity: Activity option) : Activity option =
    match activity with
    | Some a ->
        a.SetTag(key, value) |> ignore
        Some a
    | None -> None

let setError (msg: string) (activity: Activity option) : unit =
    match activity with
    | Some a ->
        a.SetTag("error", true) |> ignore
        a.SetTag("error.message", msg) |> ignore
    | None -> ()

let startActivity (activitySource: ActivitySource) (name: string) : Activity option =
    match activitySource.StartActivity(name, ActivityKind.Server) with
    | null -> None
    | a ->
        Activity.Current <- a
        (Some a) |> setTag "fleet.operation" name |> ignore
        Some a