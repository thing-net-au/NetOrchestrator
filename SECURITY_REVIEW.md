# Security Review (Defensive)

## Verified findings
1. API control endpoints currently do not enforce authentication/authorization.
2. CORS policy remains permissive (`AllowAnyOrigin/Method/Header`).
3. Subprocess command construction previously used concatenated arguments.
4. IPC method dispatch previously assumed valid payload shape.
5. Runtime config mutation endpoint updates in-memory state only.

## Remediations implemented in this audit pass
- Process launch now uses `ProcessStartInfo.ArgumentList` tokenization instead of raw command-line concatenation.
- IPC payload validation now handles missing/invalid method and params safely.
- Added input validation on API control/report/config mutation endpoints.
- Added warning/error logging around process lifecycle failures.

## Remaining risks
1. Missing authn/authz for administrative endpoints.
2. CORS policy should be tightened to explicit origins.
3. `orchestrator.json` should be integrity-protected in production workflows.
4. Consider policy-based allowlist for executable paths.
5. Add audit logging for all control-plane actions.

## Suggested hardening roadmap
- Introduce API authentication (mTLS/JWT) + role-based authorization.
- Restrict CORS and bind addresses by environment.
- Implement signed configuration bundles and immutable deployment pipeline.
- Add secure secret/config sourcing for sensitive values.
