module FleetManagement.API.Tracing

open System.Diagnostics
open FleetManagement.Core.Tracing

let serviceName = "FleetManagement.API"

let activitySource = new ActivitySource(serviceName)

let startActivity (name: string) : Activity option = startActivity activitySource name