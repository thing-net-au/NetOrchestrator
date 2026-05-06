# NetOrchestrator Architecture

## Overview
NetOrchestrator is a .NET 8 orchestration platform composed of:
- **Orchestrator**: long-running host process that boots runtime services.
- **Orchestrator.Supervisor**: process lifecycle management and log capture.
- **Orchestrator.Scheduler**: policy loop enforcing min/max instance bounds.
- **Orchestrator.IPC**: named-pipe command/status boundary.
- **Orchestrator.WebApi**: HTTP management, status and log streaming surface.
- **Orchestrator.WebUI**: Blazor dashboard for operators.

## Entry points
- `Orchestrator/Program.cs`: worker host lifecycle entry.
- `Orchestrator.WebApi/Program.cs`: API entry and SSE surfaces.
- `Orchestrator.WebUI/Program.cs`: UI server and API client wiring.

## Data flow
1. `orchestrator.json` is loaded into `OrchestratorConfig.Current`.
2. `InitialProcessLauncher` starts each service up to `MinInstances`.
3. `PolicyScheduler` periodically reads status and reconciles desired vs actual counts.
4. `ProcessSupervisor` launches/stops child processes and pushes status/log events.
5. `Worker` pushes component health snapshots into the internal status stream.
6. API and UI consume status via REST and SSE.

## Trust boundaries
- **HTTP boundary**: `/api/services/*`, `/api/config/*`, and SSE endpoints.
- **IPC boundary**: named pipe `orc_ipc_pipe` accepting JSON messages.
- **Subprocess boundary**: configured workloads launched via `dotnet`.
- **Configuration boundary**: runtime behavior driven by `orchestrator.json`.

## State model
- In-memory process registry in supervisor.
- In-memory per-stream log channels.
- In-memory worker status cache from IPC reports.
- Configuration currently mutable in memory; file persistence is not implemented.

## External dependencies
- OS process execution APIs.
- Named pipes for local IPC.
- HTTP/SSE stack for operator integrations.

## Deployment assumptions
- Dotnet runtime installed where orchestrator executes.
- Config file present in app base directory.
- Network ports for API/UI are available and routable per deployment policy.
