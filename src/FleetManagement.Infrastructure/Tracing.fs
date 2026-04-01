module FleetManagement.Infrastructure.Tracing

open System.Diagnostics
open FleetManagement.Core.Tracing

let serviceName = "FleetManagement.Infrastructure"

let activitySource = new ActivitySource(serviceName)

let startActivity (name: string) : Activity option = startActivity activitySource name