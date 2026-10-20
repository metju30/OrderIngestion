# 002 — Monitoring and observability

## What exists in this repository

Per-run execution history is persisted to `IngestionLogs`. Each row records run ID, start/finish
time, status, counters (fetched / inserted / skipped / rejected / retries), and an error message.
Structured application logs (Serilog) carry the same `RunId` in a log scope, so a specific run can
be traced by filtering on that key.

**Failure handling around `CompleteExecutionAsync`.** If the log-write itself fails:
- On a **successful** run, the exception is rethrown — a run that could not be recorded must not be
  reported as successful. The `IngestionLogs` row stays in `Running` state and is visible in the
  stale-`Running` query below.
- On a **failed** run, the log-write exception is swallowed so the original (root-cause) exception
  is preserved for the caller. `RunMetricsContext.EndRun()` always runs.

Log lines are tagged with `RunKind` (Ingestion / Import) and `RunId` (when inside an active run).
Use `RunKind` to distinguish worker-level lines, `RunId` to correlate a specific run across
`IngestionLogs` and the log stream.

**Two log rows per cycle in File mode.** With `Ingestion:Mode = File` `(RunKind = 'Import')`, each ingestion cycle
produces two `IngestionLogs` rows: one from `OrderIngestionService` (source → queue) and one from
`FileToSqliteImporter` (queue → SQLite). In the ingestion row, `InsertedCount` means "queued", not
"written to the database"; `SkippedCount` there is always 0. The importer row carries the true
database counters. In SQLite mode there is a single row per cycle, and `InsertedCount` means
"written to the database" directly.

## What does not exist

- No live metrics emitted to Application Insights / Prometheus / any backend.
- No alerting rules.
- No dashboards.

`IngestionLogs` is **execution history**, not a monitoring solution. A monitoring solution must make
it possible to detect that the worker has stopped succeeding, is repeatedly retrying, or is taking
unusually long — **without a human running queries**. This is stated explicitly in [Known gaps](../README.md#known-gaps)
and is treated as an AC-07 partial gap, not as a completed requirement.

One important **edge case**: if the process terminates abruptly, the final update may never happen. A run left in Running can indicate an interrupted execution, but it could also indicate a run still in progress. If you need to detect stale runs, compare their start times against an appropriate timeout rather than treating every Running record as failed.

## Queries that approximate monitoring

Column names used in the queries below are defined in [schema.md](schema.md).

### 1. Recent failed runs
``` sql
SELECT *
FROM IngestionLogs
WHERE Status = 'Failed'
ORDER BY StartedAtUtc DESC
LIMIT 20;
```
### 2. Last successful execution
``` sql
SELECT MAX(FinishedAtUtc) AS LastSuccessfulRunUtc
FROM IngestionLogs
WHERE Status = 'Succeeded';
```
### 3. Runs with unusually high duration
``` sql
SELECT *, (julianday(FinishedAtUtc) - julianday(StartedAtUtc)) * 86400 AS DurationSeconds
FROM IngestionLogs
WHERE FinishedAtUtc IS NOT NULL
  AND (julianday(FinishedAtUtc) - julianday(StartedAtUtc)) * 86400 > 60; -- 1 minute
```
### 4. Last 10 runs
``` sql
SELECT StartedAtUtc, FinishedAtUtc,
       (julianday(FinishedAtUtc) - julianday(StartedAtUtc)) * 86400 AS DurationSeconds,
       FetchedCount, InsertedCount, SkippedCount, RejectedCount,
       RetryCount, Status,
       ErrorMessage
FROM IngestionLogs
WHERE RunKind = 'Ingestion' -- or 'Import'
ORDER BY StartedAtUtc DESC
LIMIT 10;
```
### 5. Stale Running rows
``` sql
SELECT *
FROM IngestionLogs
WHERE Status = 'Running'
  AND (julianday('now') - julianday(StartedAtUtc)) * 86400 > 1800; -- 30 min
```

## What breaks in production, and how you'd notice


| Failure mode | How you'd notice today | What a production build would add |
| :--- | :--- | :--- |
| **Worker stopped** | No new rows in IngestionLogs | Heartbeat metric; alert on "no runs in X minutes" |
| **Worker looping in retries** | Rising RetryCount, repeated Failed rows | Retry counter metric + backoff cap + circuit breaker |
| **Queue backing up (File mode)** | Queue file grows; importer logs slow progress | Queue depth and oldest-message-age metrics |
| **Stale Running row** | Manual query (above) | Startup reconciliation: mark runs older than threshold as Abandoned |
| **Unique-index violation storm** | DbUpdateException in app logs | Dead-letter queue + per-order rejection log |
