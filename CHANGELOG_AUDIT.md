# Audit Changelog

## Scope
Deep self-audit changes focused on understanding, observability, robustness, and defensive security posture.

## Changes made
1. **Supervisor hardening**
   - Replaced non-thread-safe process list storage with nested concurrent dictionaries.
   - Removed blocking sleep in process exit path.
   - Removed sync-over-async status reporting.
   - Added structured lifecycle logging and error handling.
   - Switched process launch to `ArgumentList` construction.

2. **Scheduler observability**
   - Added scheduler startup log.
   - Added health freshness logic based on last execution timestamp.
   - Added warning/debug logs for policy branches.

3. **IPC robustness**
   - Added payload validation (`method`, `params`) and explicit error responses.
   - Added IPC heartbeat freshness in internal status.
   - Improved cancellation/error logging.

4. **API diagnostics and validation**
   - Added health checks and endpoints.
   - Added request ID response header.
   - Added startup diagnostic log.
   - Added input validation and clearer responses in service/config controllers.

5. **Operational documentation artifacts**
   - Added `ARCHITECTURE.md`.
   - Added `OPERATIONS.md`.
   - Added `OBSERVABILITY_GAPS.md`.
   - Added `SECURITY_REVIEW.md`.
   - Added this `CHANGELOG_AUDIT.md`.

## Follow-up work
- Add authentication/authorization.
- Add OpenTelemetry metrics/traces.
- Add bounded queues with backpressure.
- Add integration tests for lifecycle and failure scenarios.
