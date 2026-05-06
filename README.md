# NetOrchestrator

A .NET 8 process-supervision framework that launches, monitors, and auto-restarts managed services. It exposes an HTTP API, a Blazor Server dashboard, an IPC pipe for inter-service communication, and a policy-driven scheduler with support for *steady*, *demand*, and *cron* policies.

---

## Projects

| Project | Description |
|---|---|
| `Orchestrator.Core` | Shared models, interfaces, and helpers (e.g. `TopologicalSort`) |
| `Orchestrator.Supervisor` | `ProcessSupervisor` – launches/stops processes, captures logs, auto-restarts on crash |
| `Orchestrator.Scheduler` | `PolicyScheduler` – enforces steady/demand/cron scheduling policies |
| `Orchestrator.IPC` | Named-pipe IPC server for worker heartbeats and neighbor-execution requests |
| `Orchestrator.WebApi` | ASP.NET Core Web API (REST + SSE log streams + Prometheus `/metrics`) |
| `Orchestrator.WebUI` | Blazor Server dashboard |
| `Orchestrator` | Generic Host entry point (Windows Service / systemd) |
| `Orchestrator.Tests` | xUnit unit tests |
| `Orchestrator.WebApi.Tests` | xUnit integration tests using `WebApplicationFactory` |

---

## Configuration

All configuration lives in `orchestrator.json` (one per runnable project). The JSON maps directly to `OrchestratorConfig`:

```json
{
  "Services": {
    "WorkerApp": {
      "Name": "WorkerApp",
      "ExecutablePath": "apps/WorkerApp.dll",
      "Arguments": "--env=prod",
      "MinInstances": 2,
      "MaxInstances": 10,
      "SchedulePolicy": { "Type": "demand", "Threshold": 75, "Cron": null },
      "Dependencies": ["QueueListener"]
    }
  },
  "Global": { "LoggingLevel": "Info", "IpcTimeout": 5000, "HealthCheckInterval": 10000 },
  "Scheduling": { "DefaultPolicy": "steady", "DemandThreshold": 80 },
  "Web": {
    "UiPort": 5000,
    "ApiPort": 5001,
    "BindIP": "127.0.0.1",
    "StreamBufferSize": 8192,
    "ApiBaseUrl": "http://127.0.0.1:5001"
  }
}
```

`BindIP` restricts listening to a single IP address (default `127.0.0.1`). Change to `0.0.0.0` to listen on all interfaces.

### Scheduling Policies

| Policy | Description |
|---|---|
| `steady` | Keeps `MinInstances` running at all times |
| `demand` | Scales up/down based on aggregate CPU % vs `Threshold` |
| `cron` | Fires at times matching the `Cron` expression (5-field, via [Cronos](https://github.com/HangfireIO/Cronos)) |

---

## Running

### As a Windows Service / systemd unit

```bash
dotnet publish Orchestrator -o /opt/orchestrator
# Windows
sc create NetOrchestrator binPath="C:\opt\orchestrator\Orchestrator.exe"
# Linux
systemctl enable /opt/orchestrator/orchestrator.service
```

### Development

```bash
dotnet run --project Orchestrator.WebApi    # API on :5001
dotnet run --project Orchestrator.WebUI     # Dashboard on :5000
dotnet run --project Orchestrator           # Generic host (supervisor+scheduler+IPC)
```

### HTTPS

Generate a self-signed certificate for development:

```bash
dotnet dev-certs https --export-path ./devcert.pfx --password "devpassword"
```

Then uncomment the HTTPS `opts.Listen(...)` call in `Program.cs` for the project you want to secure.

---

## API Reference

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/services` | List all services and their runtime status |
| `GET` | `/api/services/{name}/status` | Worker heartbeat statuses for a service |
| `POST` | `/api/services/{name}/start` | Start one instance of a service |
| `POST` | `/api/services/{name}/stop` | Stop one instance of a service |
| `POST` | `/api/services/report` | Worker reports its health via HTTP (alternative to IPC) |
| `GET` | `/api/services/internal` | Health of internal components (PolicyScheduler, IpcBackgroundService) |
| `GET` | `/api/services/{name}/logs/stream` | SSE stream of process stdout/stderr |
| `GET` | `/api/status/stream` | SSE stream of serialised `ServiceStatus` objects |
| `GET` | `/api/config` | Current `OrchestratorConfig` |
| `PUT` | `/api/config/services/{name}` | Update and persist a service config |
| `GET` | `/metrics` | Prometheus-compatible metrics |
| `GET` | `/swagger` | Swagger UI (Development only) |

---

## Building & Testing

```bash
dotnet build NetOrchestrator.sln
dotnet test NetOrchestrator.sln
```

---

## Architecture Notes

- **No static singleton** – `OrchestratorConfig` is registered via `IOptions<OrchestratorConfig>` and injected everywhere.
- **Bounded log channels** – `LogStreamService` uses bounded channels (capacity = `Web.StreamBufferSize`) with `DropOldest` to prevent unbounded memory growth.
- **Auto-restart** – `ProcessSupervisor` detects non-zero exit codes and restarts after a 5 s back-off unless the stop was intentional.
- **Dependency ordering** – `InitialProcessLauncher` uses Kahn's topological sort on service `Dependencies` before starting them.
- **Graceful shutdown** – `ProcessSupervisor` implements `IHostedService.StopAsync` and kills all child processes on host shutdown.
- **Pipe security** – On Windows, the named pipe is locked down to the current user with `NamedPipeServerStreamAcl`.
