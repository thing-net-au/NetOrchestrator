# Observability Gaps

## Verified gaps
1. No metrics pipeline for process churn, queue depth, or API latency.
2. No distributed tracing or correlation propagation across API/IPC/subprocess boundaries.
3. No bounded buffering strategy for log channels.
4. No alerting hooks for repeated start failures.
5. No persistence of historical status/events for post-incident forensics.

## Implemented in this audit pass
- Added startup diagnostics logs in worker host and API.
- Added API health endpoints (`/health/live`, `/health/ready`).
- Added request ID response header (`X-Request-ID`).
- Improved structured log coverage in scheduler/supervisor/IPC paths.
- Added stronger validation and explicit error responses for IPC payload handling.

## Recommended next steps
1. Add OpenTelemetry traces and metrics exporters.
2. Add counters/histograms for:
   - process start duration
   - process exits by code
   - scheduler reconciliation actions
   - SSE connected clients
3. Add bounded channels + drop metrics.
4. Add anomaly detection for crash loops and repeated configuration errors.
