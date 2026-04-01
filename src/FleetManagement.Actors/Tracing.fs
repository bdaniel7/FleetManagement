module FleetManagement.Actors.Tracing

open System
open System.Diagnostics

let serviceName = "FleetManagement.Actors"

let activitySource = new ActivitySource(serviceName)

let startActivity (name: string) (kind: ActivityKind) (correlationId: Guid option) =
    match Activity.Current with
    | null ->
        activitySource.StartActivity(name, kind)
    | current ->
        let traceFlags =
            if current.Context.IsRemote then
                ActivityTraceFlags.Recorded
            else
                ActivityTraceFlags.None

        let parentContext = ActivityContext(current.TraceId, current.SpanId, traceFlags)

        let link = ActivityLink(parentContext)
        activitySource.StartActivity(name, kind, parentContext = parentContext, links = [link])

let tryStartActivity (name: string) (kind: ActivityKind) =
    let activity = activitySource.StartActivity(name, kind)
    match activity with
    | null -> None
    | _ -> Some activity
