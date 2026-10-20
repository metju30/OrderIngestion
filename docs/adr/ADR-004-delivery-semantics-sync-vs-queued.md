# ADR-004: Delivery semantics — synchronous vs queued

**Status:** Accepted (for demo). Production target is queued.

## Context

The current flow is fully synchronous:
```
HTTP GET /posts → deserialize → validate → SqliteOrderSink.AcceptAsync → SaveChangesAsync
```
A single ingestion run issues three independent database transactions (log-start, sink-insert, log-complete) with no wrapping transaction around them. Any one can fail after the previous succeeded — a fact that is currently mitigated, not prevented, by the try..catch around CompleteExecutionAsync (see [docs/002-monitoring-and-observability.md](docs/002-monitoring-and-observability.md)). Latency is the sum of the slowest upstream call and the local writes, not one round-trip. A slow source blocks the entire run. `PeriodicTimer` serialises runs, so a slow run also delays
the next one.

This is not the shape a real integration should have. The production pattern for inbound integration
is **accept fast, process asynchronously**:
```
POST /orders → authenticate → validate schema → enqueue → 202 Accepted
↓
(durable queue: Service Bus / Storage Queue / Kafka)
↓
idempotent consumer → validate business rules → persist → ack
```

Each stage has bounded latency. Failures are retried per message, not per request. Poison messages go
to a dead-letter queue(DLQ) without blocking the rest of the stream.

## Decision (for this repository)

Keep the flow synchronous. The `Worker` calls `OrderIngestionService.RunAsync`, which does
fetch → validate → persist in-process, on a timer.
```
Worker                IOrderSource (HTTP)        IOrderSink (SQLite)
  |                        |                          |
  |-- FetchOrdersAsync --> |                          |
  |<----- List<Order> -----|                          |
  |                        |                          |
  |-- validate ----------->|                          |
  |                        |                          |
  |-- AcceptAsync(valid) ---------------------------> |
  |<--------------- (inserted, skipped) ------------- |
  |                        |                          |
  |-- CompleteExecutionAsync (IngestionLogs) -------> |
```
To make the production shape **runnable and observable**, the repository includes an alternate mode:

- `Ingestion:Mode = "File"` — the sink writes incoming orders to a durable JSON-lines queue
  (`data/orders-queue.jsonl`, protected by `FileQueueLock`), and a separate `OrderImporterWorker`
  drains the queue into SQLite on its own interval (`Ingestion:ImportIntervalSeconds`).

This is a working simulation of the "202-Accepted + queue" pattern. It is not a deliverable — it
exists so the shape of the production design is visible in the code, not only in this document.

## Alternatives considered

1. **Service Bus / Storage Queue in this demo** — rejected: adds Azure credentials and infrastructure
   a reviewer would have to provision. File mode achieves the same *shape* without the dependency.
2. **HTTP endpoint (Web API) with 202-Accepted** — rejected: the assignment asks for a worker, not a
   receiver. The worker is a **poller**, not a webhook endpoint; the queue pattern applies to the
   poller-to-sink boundary, not the source-to-worker boundary. If a future requirement is "the source
   pushes", ADR-004 must be rewritten as endpoint + queue (see [open question 3](ADR-004-delivery-semantics-sync-vs-queued.md#Open-questions)).
3. **Transactional outbox** — rejected: the outbox pattern applies to *outbound* events generated
   inside a transaction. This system is inbound, so the closest analogue is "queue on accept", which
   File mode demonstrates.

## Consequences

**What changes if this becomes a real queue:**

- `IngestionLogs` moves from per-run to per-batch. A "run" becomes "a polling cycle that enqueues N
  messages"; per-message outcomes need a separate `IngestionEvents` table.
- Monitoring changes: queue depth, age of oldest message, dead-letter count, and consumer lag become
  the primary health signals. `RetryCount` on `IngestionLogs` no longer covers per-message retries.
- Retry semantics move from Polly-on-`HttpClient` to queue-level redelivery. **ADR-003 becomes a
  queue policy, not an HTTP policy.**
- The unique index on `ExternalId` becomes load-bearing for idempotency. If a message is delivered
  twice (at-least-once), the second delivery must fail gracefully with a duplicate-key error that the
  consumer treats as success. This is the current `AddRangeIfNotExistsWithCountAsync` contract
  applied to a single-message boundary.

**What does not change:**

- Validation rules ([ADR-002](ADR-002-order-identity-and-idempotency.md)) stay the same.
- Identity is still `ExternalId`.
- The accept/reject decision (reject-and-continue vs fail-batch) is still per-batch in the poller; at
  the queue level it becomes per-message.

## Open questions (see [004-requirements-clarification.md](004-requirements-clarification.md))

1. Does the client need per-message acknowledgement, or is at-least-once with a duplicate-key swallow
   acceptable?
2. Is dead-letter replay required, or is "log and move on" sufficient?
3. Will the source ever push (webhook) rather than be polled? If yes, this ADR changes from
   "worker + queue" to "endpoint + queue".