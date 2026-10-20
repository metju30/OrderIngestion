# ADR-002: Order identity and idempotent ingestion

**Status:** Accepted

## Context

The worker periodically imports orders from an external API. The same order can be returned by multiple API requests, including retries and subsequent scheduled runs.

## Decision

Use the source system's order ID as the external identity of an order. Enforce uniqueness in the database with a unique constraint or index. Skip an order when that external ID already exists.

Do not rely exclusively on filtering by the maximum stored ID unless the source guarantees monotonically increasing IDs and no late-arriving records.

Duplicates are collapsed within a payload before insert. Violating the upstream uniqueness
contract must not fail an otherwise-valid batch.

## Alternatives considered

1. Filter incoming records using `OrderId > MAX(OrderId)`.
2. Check for an existing ID in application code without a database uniqueness constraint.
3. Enforce database uniqueness and handle existing records as expected duplicates.

## Consequences

- Repeated ingestion does not create duplicate orders.
- Database uniqueness remains effective when concurrent executions attempt the same insert.
- The physical form of the uniqueness guarantee (column type, index name) is documented in [schema.md](../schema.md).
- Existing orders are not updated when their source fields change.
- A maximum-ID optimization may be added later if the source contract guarantees that it is safe.
