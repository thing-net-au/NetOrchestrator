# Operations Guide

## Runtime assumptions
- .NET runtime is available to launch configured DLL workloads.
- `orchestrator.json` exists and is valid JSON.
- API/UI ports are available and not blocked by host firewall.

## Startup diagnostics
At startup, the host and API now log configured service counts, interval, runtime version, and API port.

## Health and diagnostics endpoints
- `GET /health/live`
- `GET /health/ready`
- `GET /api/services`
- `GET /api/services/internal`
- SSE:
  - `/api/services/{name}/logs/stream`
  - `/api/status/stream`

## Common failure modes
1. **Child process start failures**
   - Symptoms: no instances running, supervisor error logs.
   - Recovery: validate executable path/args/working directory.
2. **Runaway log volume / memory pressure**
   - Symptoms: growth in resident memory.
   - Recovery: restart service; implement bounded channels + metrics.
3. **Config drift**
   - Symptoms: behavior differs from file because API updates are in-memory only.
   - Recovery: reapply desired config in `orchestrator.json` and restart.
4. **IPC malformed payloads**
   - Symptoms: IPC error responses in logs.
   - Recovery: correct client payload format (`method`, `params`).

## Incident response playbook
1. Check process counts (`/api/services`).
2. Check internal component status (`/api/services/internal`).
3. Stream `_supervisor` logs for lifecycle errors.
4. If unstable, stop/start affected service through API.
5. If configuration corruption suspected, restore known-good `orchestrator.json` and restart.

## Recovery and restart behavior
- Scheduler reconciles towards configured min/max instances.
- Initial launcher enforces minimum startup baseline.
- Process exits are reported immediately and reflected in status stream.
