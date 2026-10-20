# Database Schema

## Orders table
- **Id** (INTEGER, PK, auto-increment) — internal surrogate key
- **ExternalId** (INTEGER, UNIQUE INDEX) ← **Deduplication lever**
- UserId, Title, Body (TEXT)
- IngestedAtUtc (TEXT, datetime ISO8601)

## IngestionLogs table
- **Id** (INTEGER, PK)
- **RunId** (TEXT, UNIQUE) — trace key; indexed
- **RunKind** (TEXT: 'Ingestion' | 'Import') — distinguishes worker cycles
- **Status** (TEXT: 'Succeeded' | 'Failed' | 'PartiallySucceeded' | 'Running')
- StartedAtUtc, FinishedAtUtc (TEXT, ISO8601)
- FetchedCount, InsertedCount, SkippedCount, RejectedCount, RetryCount (INTEGER)
- ErrorMessage (TEXT, nullable)

### Key Invariants
- ExternalId is globally unique. The same order can never exist twice, even across retries or runs.
- RunId is per-run globally unique. Correlates application logs (via scope) to IngestionLogs rows.
- IngestionLogs may contain stale Running rows if the process crashed. Use 30-min staleness threshold to flag for manual reconciliation.