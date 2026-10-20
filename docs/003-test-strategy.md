# 003 — Test strategy

## Layering

Tests mirror the four-layer architecture ([ADR-001](adr/ADR-001-solution-architecture.md)). Unit
tests are fast and deterministic; integration tests exercise real SQLite and the real schema
(including the unique index), so they prove that the database enforces the invariants the application
relies on.

| File | Layer | What it protects against |
|---|---|---|
| `OrderIngestionClientTests` | Infrastructure — HTTP client | Silent deserialization failures, wrong error classification |
| `OrderSourcePollyTests` | Infrastructure — DI + Polly | Policy not applied, retry loop absent |
| `OrderValidatorTests` | Application — validation | Invalid orders slipping into the sink |
| `OrderIngestionServiceTests` | Application — orchestration | Falsely-successful runs, lost failure signals |
| `SqliteOrderRepositoryTests` | Infrastructure — persistence | Duplicate orders, batch failure on partial duplicates |

## Mapping to acceptance criteria

The README carries the authoritative AC → verification table and the P1/P2 priority table. This
document explains *why* each layer exists, not what each test asserts.

## Why these tests, and not more

The assignment does not require "comprehensive test scenarios." The suite is bounded by one
question: **can this suite catch the failure modes that would cause duplicate data, lost orders, or
a falsely-successful run?** Anything outside that boundary is deliberately deferred:

- **File mode (`Ingestion:Mode = File`) has no automated tests.** It is a working simulation of the
  production async delivery pattern (see [ADR-004](adr/ADR-004-delivery-semantics-sync-vs-queued.md)),
  verified manually per [001-how-to-run.md §5](001-how-to-run.md). The shared invariants — validation,
  duplicate prevention, run recording — are covered by the SQLite-mode tests because both modes call
  the same `IOrderRepository.AddRangeIfNotExistsWithCountAsync`. Only `FileQueueLock` and the JSONL
  round-trip are unique to File mode, and neither is a deliverable.
- **`OrderImporterWorker` and `FileToSqliteImporter` are not tested.** Same reason.
- **Logger output is not asserted.** Behaviour is verified by state, not by log text. If log text
  becomes a contract, add a test with `NullLoggerFactory` swapped for a spy.
- **The validator is tested as a pure function.** `OrderIngestionService` is tested for the
  orchestration decision (reject-and-continue vs fail-batch), not for re-testing individual rules.
- **Retry timing is not asserted.** Polly is proven to have retried by **counting requests**, not by
  measuring wall time. A strict `Assert.Equal(4, requestCounter)` is preferred over `> 1` because it
  pins the retry count to the policy configuration.
- **Concurrency is not tested.** SQLite serialises writes and the unique index makes concurrent
  duplicate inserts safe at the database level; a concurrency test would primarily exercise SQLite,
  not this code.

## If the scope grows

| Addition | Why | Trigger |
|---|---|---|
| `SqliteIngestionLogRepository` round-trip | Start/Complete is trivial, but a regression means every run silently reports `Running` | If ingestion logging becomes load-bearing for alerting |
| `FileToSqliteImporter` happy path | Only if File mode becomes a deliverable | Scope change |
| Per-order rejection test with a spy logger | Asserts that rejections are *observable* | If AC-07 is upgraded to include per-order visibility |