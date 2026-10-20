# 004 — Requirements clarification

This document records the questions that would need to be answered before building this integration
for a real client, and the decisions taken for this assignment. It is written in the spirit of the
*Developer–Project Manager Clarification Framework*: use the six dimensions as a checklist, then ask
only the questions that materially change the implementation.

## The request

> Build a worker that retrieves orders from an external source every 15 minutes (or on startup),
> stores them in a local database, ensures the same record is not inserted twice, logs each execution,
> implements a retry mechanism, and implements monitoring.

## Decisions taken for this repository

| Dimension | Question | Decision here | Would need PM input in production? |
|---|---|---|---|
| **Business outcome** | What counts as "an order"? | Every JSONPlaceholder `post` = one order. The source has no status field, so "created" is the only possible trigger. | Yes. "Created" vs "paid" changes the trigger event entirely. |
| **Data ownership** | Who is the source of truth? | JSONPlaceholder owns order data; SQLite is a sink, not a system of record. | Yes. An ERP would own order state, and the sink would need a read-back. |
| **Process** | Sync or async delivery? | Sync, for demo simplicity. `Ingestion:Mode = File` simulates the production 202-Accepted + queue pattern. See [ADR-004](adr/ADR-004-delivery-semantics-sync-vs-queued.md). | Yes. SLA and volume decide. |
| **Exceptions** | `ExternalId` is 0 or negative? | Reject the order, continue the batch, mark run `PartiallySucceeded`. | Yes. "Fail the whole batch" is equally defensible. |
| **Exceptions** | Source repeats an order? | Skip; unique index on `ExternalId` is the final guard. See [ADR-002](adr/ADR-002-order-identity-and-idempotency.md). | No. |
| **Exceptions** | Existing order's fields change upstream? | **Not updated.** Insert-or-skip only. Explicit non-goal. | Yes. This is the most likely follow-up ask. |
| **Timing** | How often? | 15 minutes. `PeriodicTimer` guarantees no overlapping runs. | No. |
| **Failure** | Retry policy? | Three attempts, exponential backoff (2 s / 4 s / 8 s), transient errors only. See [ADR-003](adr/ADR-003-retry-and-scheduling-policy.md). | Partly. Circuit-breaker and cap decisions belong to the PM/ops. |
| **Acceptance** | What proves success? | AC-01…AC-08 in README, each mapped to a test or a runbook step. | No. |

## Explicitly out of scope

- No updates or upserts — insert-or-skip only.
- No per-order retry — a failed batch is a failed run.
- No dead-letter queue, outbox, or poison-message handling.
- No authentication against the source (JSONPlaceholder has none).
- No incremental fetch (`since`, `updatedAfter`) — every run pulls the full set.
- No cross-process locking — two workers would race on the unique index.
- No live metrics emitted — `RetryCount` is persisted per run, not published to a monitoring backend.
- No File-mode tests — see [003-test-strategy.md](003-test-strategy.md).

## Questions I would ask before building this for real

1. Is an order **immutable**, or do we need updates and upserts? (Changes ADR-002 completely.)
2. Is "an order exists" the right trigger, or is it "an order is **paid**"? (Changes the event source.)
3. What is the maximum acceptable staleness? (Decides polling vs webhook vs queue.)
4. If the source is down for an hour, must the next run backfill, or is dropping acceptable?
5. Should a single invalid order fail the batch, or be rejected individually?
6. Who owns the upstream `ExternalId` uniqueness contract, and how is a violation reported to us?
7. Do late-arriving orders represent corrections? If yes, insert-only is the wrong policy.
8. Is `ExternalId` stable across systems, or do we need an internal surrogate key?
9. What is the retention policy for `IngestionLogs`?

## What I would do next (if this were real)

1. Confirm (1) and (7) with the PM — they change the schema.
2. Replace synchronous delivery with 202 + durable queue ([ADR-004](adr/ADR-004-delivery-semantics-sync-vs-queued.md)).
3. Add a circuit breaker and a per-order rejection log, so one bad order cannot mask a bad batch.
4. Emit run outcome and retry count as App Insights custom metrics — AC-07 is currently only partially met.