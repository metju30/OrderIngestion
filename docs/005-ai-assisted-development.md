# 005 — How this was built with AI

AI tools were used during this assignment. This document records **what** was AI-assisted, **what was rejected**, and **how each change was verified**.

## AI-assisted, and verified

| Area | What the AI produced | How I verified it |
|---|---|---|
| xUnit scaffolding (fakes, `MockHttpMessageHandler` variants) | File structure and naming | Ran `dotnet test`; walked each assertion by hand |
| Polly wiring (`HandleTransientHttpError`, `WaitAndRetryAsync`) | The `AddPolicyHandler` extension | `OrderSourcePollyTests` asserts the exact request counter |
| SQLite in-memory fixture | `SqliteConnection` / `EnsureCreated` pattern | Inserted a row, read it back, confirmed the unique index is enforced |
| ADR drafts (001, 002) | First pass on all sections | Modified and checked against the actual code content of all sections |
| README structure | First-pass organisation | Manually verified and condensed |
| SQL `julianday()` fix for duration queries | The corrected queries in `002-monitoring-and-observability.md` | Verified against a live SQLite DB by running each query and comparing to a known duration |

## AI-suggested, and rejected

| Suggestion | Why rejected |
|---|---|
| `WHERE ExternalId > MAX(ExternalId)` for incremental fetch | The source does not guarantee monotonic IDs or absence of late arrivals. See [ADR-002](adr/ADR-002-order-identity-and-idempotency.md). |
| Parallel `Task.WhenAll` batch inserts | `OrderDbContext` is not thread-safe; SQLite serialises writes and throws on concurrent `SaveChangesAsync`. |
| `IAsyncEnumerable<Order>` streaming from the source | Adds complexity for a 100-record payload; matters only at very large volume, which is not in scope. |
| Infinite retry with `WaitAndRetryForeverAsync` | Violates AC-05 (no infinite retry loop). |
| Domain events + MediatR inside `OrderIngestionService` | Over-engineering for a two-step flow; would hide the orchestration that the tests need to verify. |
| Generic `IRepository<T>` | Adds indirection without value; `IOrderRepository` already exposes the two methods the sink needs. |
| `CAST(FinishedAtUtc - StartedAtUtc AS INT)` for duration | Does not work in SQLite — EF Core stores `DateTime` as text. Replaced with `julianday()`. Verified by running the query. |


## AI-suggested, kept (with modification)

| Suggestion | Modification |
|---|---|
| Serilog + Application Insights wiring | Kept Serilog; App Insights hook is present but disabled by default (`ApplicationInsights:ConnectionString` empty). |
| Polly delay `attempt => 2^attempt` seconds | Kept, but noted in [003-test-strategy.md](003-test-strategy.md) that the test suite pays ~14 s for this — the trade-off is accepted for a demo. |

## Where the framework was applied

The six-dimension clarification framework in [004-requirements-clarification.md](004-requirements-clarification.md) was developed during this assignment, with AI assistance, to structure the questions that a real integration would need answered. The most important part of that document is the **answers**. They show which questions matter, what we decided, and what we chose to do later.