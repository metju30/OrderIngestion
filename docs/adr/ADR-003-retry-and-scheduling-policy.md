# ADR-003: Retry and scheduling policy

**Status:** Accepted

## Context

Order ingestion runs on a fixed schedule (initial run on startup, then every 15 minutes) against an
external HTTP source. Transient failures (5xx, 408, `HttpRequestException`) are expected and must not
fail a run permanently. Runs must not overlap: a slow run must not queue a second run behind it,
because source data is unordered and duplicate work is wasted.

The worker is a .NET `BackgroundService`. There is no external scheduler, job store, or distributed
lock.

## Decision

**Scheduling:** `PeriodicTimer` inside `BackgroundService.ExecuteAsync`. `WaitForNextTickAsync`
awaits the previous run, so ticks are inherently serialised. No `Task.Yield()` workaround is needed
on .NET 6+.

**Retries:** `HttpPolicyExtensions.HandleTransientHttpError()` (5xx, 408, `HttpRequestException`)
with `WaitAndRetryAsync(3, attempt => 2^attempt seconds)`. Worst-case wall time bounded at ~14 s.
Each retry increments `RunMetricsContext.RetryCount` (an `AsyncLocal` counter that flows through
the pooled handler chain — a scoped `IRunMetrics` cannot see it). The count is persisted to
`IngestionLogs.RetryCount` at run end.

**What gets retried:** only the HTTP fetch. A sink failure after the log-start transaction leaves the run in Running state. Detecting and reconciling this is the job of the stale-Running query in [docs/002-monitoring-and-observability.md](docs/002-monitoring-and-observability.md); the run itself cannot self-heal. The assumption is that the source is unreliable but the
local database is not; a sink retry would require per-order retry, which changes the failure model
(see [ADR-002](ADR-002-order-identity-and-idempotency.md) consequences).

## Alternatives considered

1. **Quartz.NET / Hangfire** — rejected: adds a persistent job store, a scheduler process, and a
   dashboard. That is the correct production answer, but it is out of scope for a demo and would
   distract from the ingestion logic the assignment asks to demonstrate.
2. **Infinite retry with `WaitAndRetryForeverAsync`** — rejected: violates AC-05 ("no infinite retry loop").
3. **Circuit breaker** — deferred: materially useful in production, but it changes the failure
   semantics (a broken circuit must be surfaced distinctly from a failed run). Recorded as an open
   question in [004-requirements-clarification.md](../004-requirements-clarification.md).
4. **Per-order retry inside the sink** — rejected: the current failure model treats a batch as
   atomic. Per-order retry requires a dead-letter store and a per-order status column, which is out
   of scope.

## Consequences

- Every run is bounded in time: 3 retries × 2^n backoff ≤ ~14 s of waiting, plus the 30 s request
  timeout on the `HttpClient`.
- Runs are serialised by `PeriodicTimer`. If a run exceeds 15 minutes, the next tick is *skipped*,
  not queued. This matches the assignment's intent ("every 15 minutes", not "every 15 minutes after
  the previous one finishes").
- The retry policy is exercised end-to-end by `OrderSourcePollyTests`, which asserts that a 503
  response is retried exactly the configured number of times — counting requests, not measuring time.
- `IRunMetrics` remains registered as a scoped seam for future per-run metrics (e.g. App Insights),
  but the retry counter itself lives in `RunMetricsContext` because it must cross the handler boundary.  
- No distributed lock. Two worker instances would run in parallel; the unique index on `ExternalId`
  prevents duplicates, but not wasted work. A production build would add leader election.
- No dead-letter queue. A batch that fails after retries is logged as `Failed` and not reprocessed
  until the next scheduled run.