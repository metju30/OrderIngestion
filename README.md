# OrderIngestion

> .NET 9 worker that ingests orders from an HTTP source into SQLite on a 15-minute schedule

## In 5 minutes

`OrderIngestion.Worker` runs `OrderIngestionService.RunAsync` on a `PeriodicTimer` (15 min, no overlap).
The service fetches orders via `IOrderSource` (HTTP), validates them (`OrderValidator`), and persists
them idempotently via `IOrderSink` (SQLite, unique index on `ExternalId`). Every run writes to `IngestionLogs`.

Add a new source or sink by implementing `IOrderSource` / `IOrderSink` and registering the
implementation in `InfrastructureServiceCollectionExtensions`. Configuration lives in
`appsettings.json`: `OrderSource:BaseUrl`, `ConnectionStrings:DefaultConnection`,
`Ingestion:IntervalMinutes`, `Ingestion:Mode` (`Sqlite` | `File`).

## Run and verify

See [docs/001-how-to-run.md](docs/001-how-to-run.md). Covers setup, running, verifying duplicate
prevention, simulating an API failure, and running the tests.

> `tools/OrderIngestion.FaultyApi` is a **test tool**, not part of the ingestion service. It
> provides the four scenarios used to verify retry behaviour (AC-04, AC-05) end-to-end, plus a
> local substitute for the public mock API. See
> [docs/001-how-to-run.md §5](docs/001-how-to-run.md).

## Architecture decision records
- [ADR-001 — Clean Architecture](docs/adr/ADR-001-solution-architecture.md)
- [ADR-002 — Order identity and idempotency](docs/adr/ADR-002-order-identity-and-idempotency.md)
- [ADR-003 — Retry and scheduling](docs/adr/ADR-003-retry-and-scheduling-policy.md)
- [ADR-004 — Delivery semantics: sync vs queued](docs/adr/ADR-004-delivery-semantics-sync-vs-queued.md)

## Design notes and limitations

- **Schema:** see [docs/schema.md](docs/schema.md) for the two tables (`Orders`, `IngestionLogs`), their columns, and the unique index on `Orders.ExternalId`.
- **This is a demo**, not a production-hardened service. Gaps and open questions are named explicitly
  in [docs/004-requirements-clarification.md](docs/004-requirements-clarification.md) and [Known gaps](#known-gaps).
- **Duplicate prevention** has two layers: the `SqliteOrderSink` pre-filter and a **unique index on
  `Orders.ExternalId`**. See [ADR-002](docs/adr/ADR-002-order-identity-and-idempotency.md).
- **Late arrivals are inserted.** Identity is set-membership on `ExternalId`, not monotonic ordering.
  Existing orders are never updated. See [ADR-002](docs/adr/ADR-002-order-identity-and-idempotency.md#consequences).
- **Transient failures** are retried three times with exponential backoff (2 s / 4 s / 8 s). See
  [ADR-003](docs/adr/ADR-003-retry-and-scheduling-policy.md).
- **Synchronous delivery** is a demo simplification. `Ingestion:Mode = File` simulates the production
  async pattern (202-Accepted + durable queue). See
  [ADR-004](docs/adr/ADR-004-delivery-semantics-sync-vs-queued.md).
- **IngestionLogs.RunKind** distinguishes ingestion cycles from importer cycles. In SQLite mode every row is Ingestion.  

## Verification contract

| ID | Acceptance criterion | Verification |
| :--- | :--- | :--- |
| **AC-01** | The worker starts and performs an initial ingestion run. | Start the application and inspect the run log. |
| **AC-02** | Further ingestion runs are scheduled at 15-minute intervals. | Verify the configured schedule and run timestamps. |
| **AC-03** | Reprocessing the same orders does not create duplicate external IDs. | Submit the same payload twice and check the database. |
| **AC-04** | Temporary source failures trigger bounded retries. | Simulate transient failure followed by success. |
| **AC-05** | Exhausted retries produce a visible failed run and do not create an infinite retry loop. | Simulate a persistent source failure. |
| **AC-06** | Every run has a useful execution record. | Inspect its run ID, outcome, timing, counts, and relevant error details. |
| **AC-07** | Monitoring signals distinguish successful, failed, and retried work. | Inspect `IngestionLogs` and application logs. *Partially met — see [Known gaps](#known-gaps).* |
| **AC-08** | The project builds and the core tests pass from a clean checkout. | Follow the README setup and run `dotnet test`. |

| Priority | Test | Type | Covered by |
| :--- | :--- | :--- | :--- |
| **P1** | Insert a new order | Integration | `SqliteOrderRepositoryTests` |
| **P1** | Process the same order twice | Integration | `SqliteOrderRepositoryTests` |
| **P1** | Batch with new + existing IDs | Integration | `SqliteOrderRepositoryTests` |
| **P1** | API returns an error | Unit | `OrderSourcePollyTests`, `OrderIngestionServiceTests` |
| **P1** | API returns invalid JSON | Unit | `OrderIngestionClientTests`, `OrderIngestionServiceTests` |
| **P1** | Order has missing / invalid ID | Unit | `OrderValidatorTests`, `OrderIngestionServiceTests` |
| **P2** | Empty API response | Unit | `OrderIngestionServiceTests` |
| **P2** | Older, previously unseen ID | Integration | `SqliteOrderRepositoryTests` |

Test layout and rationale: [docs/003-test-strategy.md](docs/003-test-strategy.md).

## Monitoring

Per-run outcome, counts, and retry totals are persisted to `IngestionLogs`; structured application
logs carry the same `RunId` scope. Queries for failed runs, last success, slow runs, and a stale-
`Running` check are in [docs/002-monitoring-and-observability.md](docs/002-monitoring-and-observability.md).

## Known gaps

- **AC-07 is only partially met.** No live metrics are emitted to a monitoring backend; `RetryCount`
  is persisted per run, not published. A production worker would emit custom App Insights metrics.
- **No per-order rejection log.** A rejected order is counted and logged at `Warning` level but is not
  quarantined for later inspection.
- **No incremental fetch.** Every run pulls the full source set. A `since` / `updatedAfter` parameter
  would reduce load but is not defined by the source contract.
- **No cross-process locking.** Two worker instances would race on the unique index. SQLite handles
  this safely at the database level, but there is no distributed scheduler lock.
- **File mode is verified manually, not by automated tests.** `Ingestion:Mode = File` is a
  working simulation of the production async pattern (see [ADR-004](docs/adr/ADR-004-delivery-semantics-sync-vs-queued.md)).
  Its happy path is exercised manually per [docs/001-how-to-run.md §5](docs/001-how-to-run.md); it
  is not part of the test suite because the pattern it simulates is not a deliverable. Deferred
  test is listed in [docs/003-test-strategy.md](docs/003-test-strategy.md).

## How this was built with AI

See [docs/005-ai-assisted-development.md](docs/005-ai-assisted-development.md).

