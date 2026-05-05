# Core Data Hardening — Small PR Execution Plan

This plan converts the review into six reviewable PRs with clear scope, risk, dependencies, and acceptance criteria.

## Sequencing

Recommended order (dependency-aware):

1. PR 1 — Persistence Stack Safety (Foundational)
2. PR 2 — Concurrency Fixes (Highest risk)
3. PR 3 — Save Pipeline Reliability
4. PR 4 — Fetch Correctness + Performance
5. PR 5 — Data Integrity Rules
6. PR 6 — Observability + Regression Tests

---

## PR 1 — Persistence Stack Safety

**Goal**
Make store loading, migration, and context defaults safe and explicit.

**Scope**
- Centralize Core Data stack configuration behind one owner (`PersistenceController` / equivalent).
- Set deterministic defaults for all contexts:
  - `mergePolicy` for `viewContext` and background contexts.
  - `automaticallyMergesChangesFromParent`.
- Configure store descriptions with lightweight migration flags.
- Replace silent store-load fallback with structured error reporting and explicit failure behavior.

**Implementation checklist**
- [ ] Move scattered persistent container/context setup into one module.
- [ ] Add `newBackgroundContext()` helper with standardized defaults.
- [ ] Configure `NSPersistentStoreDescription` migration options before `loadPersistentStores`.
- [ ] Route load failures through a typed error + structured logger.
- [ ] Ensure app startup path differentiates fresh-store creation vs migration failure.

**Acceptance criteria**
- App starts on both fresh install and pre-existing store.
- Migration failures are visible and diagnosable.
- Context defaults are deterministic and not call-site dependent.

**Risk**: Medium

---

## PR 2 — Concurrency Fixes

**Goal**
Eliminate cross-thread managed object misuse.

**Scope**
- Replace cross-queue `NSManagedObject` passing with `NSManagedObjectID`.
- Wrap all context interaction in `perform` / `performAndWait`.
- Refactor singleton/service patterns that hold shared mutable contexts unsafely.

**Implementation checklist**
- [ ] Audit code paths for `NSManagedObject` leaving its owning queue.
- [ ] Update APIs to accept `NSManagedObjectID` or scalar IDs.
- [ ] Add helper methods for object rehydration per-context.
- [ ] Remove nested `performAndWait` patterns likely to deadlock.
- [ ] Verify background writes merge into UI context after save.

**Acceptance criteria**
- No direct cross-thread object access in code search.
- Background writes merge cleanly into UI context.
- No deadlock-prone nested `performAndWait` patterns remain.

**Risk**: High

---

## PR 3 — Save Pipeline Reliability

**Goal**
Guarantee mutation paths persist correctly.

**Scope**
- Standardize on `saveIfNeeded()` helpers with error propagation.
- Validate parent/child context save chaining behavior.
- Remove `try?` / ignored save errors in persistence paths.

**Implementation checklist**
- [ ] Introduce canonical save helper(s) for main/background contexts.
- [ ] Return typed errors or result values from mutation services.
- [ ] Ensure child saves flow to persistent store via parent save sequence.
- [ ] Replace fire-and-forget save calls with explicit handling.

**Acceptance criteria**
- Every write path has explicit save outcome.
- Save failures are surfaced via logging/telemetry and safe user behavior.

**Risk**: Medium

---

## PR 4 — Fetch Correctness + Performance

**Goal**
Prevent logic bugs and reduce heavy fetch cost.

**Scope**
- Add required sort descriptors for deterministic UI lists.
- Add limits/batch sizes where result sets can grow.
- Use count/dictionary results when full materialization is unnecessary.
- Tighten predicates to avoid over-fetching.

**Implementation checklist**
- [ ] Inventory key list-producing fetch requests.
- [ ] Add explicit sort descriptors (stable tie-breakers where needed).
- [ ] Apply `fetchBatchSize`, `fetchLimit`, or pagination strategy.
- [ ] Convert existence/count checks to count fetches.
- [ ] Add targeted profiling notes before/after on expensive screens.

**Acceptance criteria**
- Deterministic ordering in all key list views.
- Reduced unnecessary object materialization.
- No behavior regressions in Core Data-backed screens.

**Risk**: Medium

---

## PR 5 — Data Integrity Rules

**Goal**
Enforce business correctness in schema usage.

**Scope**
- Validate delete rules against product requirements.
- Add/fix uniqueness constraints and upsert behavior.
- Make import/sync idempotent and duplicate-resistant.

**Implementation checklist**
- [ ] Review each relationship for cascade/nullify/deny correctness.
- [ ] Define uniqueness keys for entities that must be unique.
- [ ] Standardize upsert path (`fetch-or-create`/merge semantics).
- [ ] Add dedup migration/repair for pre-existing duplicates where applicable.

**Acceptance criteria**
- Duplicate creation paths are blocked.
- Deletions do not leave invalid/orphaned data.
- Re-running sync/import yields stable results.

**Risk**: Medium-High

---

## PR 6 — Observability + Regression Tests

**Goal**
Catch regressions early and make failures actionable.

**Scope**
- Add structured logs for:
  - store load/migration
  - save failures
  - merge conflicts
- Add targeted tests for:
  - background write + UI merge
  - migration smoke path
  - uniqueness/upsert behavior
  - delete rule behavior

**Implementation checklist**
- [ ] Introduce log event taxonomy with stable event IDs.
- [ ] Add unit/integration tests for each previously found bug class.
- [ ] Add fixtures for fresh-store and legacy-store migration scenarios.
- [ ] Ensure test assertions verify both data correctness and merge visibility.

**Acceptance criteria**
- At least one regression test per previously found bug class.
- Actionable logs at each critical Core Data failure point.

**Risk**: Low-Medium

---

## Suggested Task Board Metadata

Use the following defaults for planning artifacts:

- **Owner**: assign by subsystem (Persistence, Sync, UI Data).
- **Estimate**: S (1–2 days), M (3–5 days), L (1–2 weeks).
- **Risk**: Low / Medium / High.
- **Status**: Todo / In Progress / In Review / Done.
- **Dependencies**: represent PR ordering explicitly (PR2 depends on PR1, etc.).

Example sizing:

- PR1: M, Medium
- PR2: L, High
- PR3: M, Medium
- PR4: M, Medium
- PR5: M/L, Medium-High
- PR6: M, Low-Medium
