## Running and Verifying the Worker

### Prerequisites

- .NET SDK 9
- Access to the configured order API.
- database provider is SQLite, no separate database server is required.

### 1. Clone and build

```bash
git clone https://github.com/metju30/OrderIngestion
cd <repository-directory>
dotnet restore
dotnet build
```

### 2. Configure the application

Configure the order API endpoint and database connection using `appsettings.json` or the supported environment variables.

Example configuration:

```json
{
  "OrderSource": {
    "BaseUrl": "https://jsonplaceholder.typicode.com/"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=orders.db"
  }
}
```

### 3. Run the worker

```bash
dotnet run --project ./OrderIngestion.Worker
```

The worker creates `orders.db` and its schema automatically on first startup (`EnsureCreated`).
If you deleted the database, just run the worker again — no manual schema setup is required.

The worker performs an initial ingestion run on startup and then runs every 15 minutes.

During execution, inspect the application logs for the run ID, execution status, number of fetched orders, inserted orders, skipped duplicates, retry attempts, and any errors.

Inspect the `IngestionLogs` table to verify that each completed execution has a corresponding run record.

### 4. Verify duplicate prevention

1. Run the worker against the configured order source.
2. Record the number of orders inserted.
3. Run the worker again against the same source data.
4. Inspect the database and confirm that existing external order IDs have not been inserted again.
5. Check the execution logs for the number of skipped duplicates.
6. Confirm the schema contains a unique index on `Orders.ExternalId` (this is created by
   `EnsureCreated` / migrations and is the last line of defence if the pre-filter is ever bypassed).

The expected result is that each external order ID occurs at most once in the database.

### 5. Simulate an API failure

For a deterministic failure test, configure the worker to use a test endpoint that returns HTTP 500.

1. Start the worker with the failure endpoint configured.
2. Observe the retry attempts in the application logs.
3. Wait for the configured retry policy to be exhausted.
4. Verify that the run is recorded as failed in `IngestionLogs`.
5. Confirm that the worker does not report the failed ingestion as successful.

#### 5.5 Local mock API

> **Before each case below**, reset the database:
>
> ```bash
> sqlite3 orders.db "DELETE FROM IngestionLogs; DELETE FROM Orders; DELETE FROM sqlite_sequence WHERE name IN ('Orders','IngestionLogs'); VACUUM;"
> ```
>
> Otherwise `SkippedCount` depends on what a previous case left in `Orders`.

> **Reset counter** then re-run the worker
>``` cmd
> curl -X POST http://localhost:5001/flaky/reset
> ```

```powershell
# Terminal 1: start the test API
dotnet run --project tools/OrderIngestion.FaultyApi

# Terminal 2: run the worker against each endpoint

# Case A — offline success (acts like jsonplaceholder)
$env:OrderSource__BaseUrl="http://localhost:5001/"; dotnet run --project ./OrderIngestion.Worker
# Case B — always fail (AC-05, retry-then-fail)
$env:OrderSource__BaseUrl="http://localhost:5001/fail/"; dotnet run --project ./OrderIngestion.Worker
# Case C — fails 3× then succeeds (AC-04, retry-then-success)
$env:OrderSource__BaseUrl="http://localhost:5001/flaky/"; dotnet run --project ./OrderIngestion.Worker
```

Expected outcomes:

| Case | Expected worker log | Expected `IngestionLogs` row |
|---|---|---|
| **A — offline success** |  `Received HTTP response … 200`; `Accepted 3 new orders`; `Fetched 3, inserted 3, skipped 0, rejected 0` | `Status=Succeeded`, `FetchedCount=3`, `InsertedCount=3`, `SkippedCount=0`, `RetryCount=0` |
| **B — always fail** | 3 × `Retry N after 2^N s due to: ServiceUnavailable`; `Order ingestion failed` | `Status=Failed`, `RetryCount=3`, `InsertedCount=0`, `ErrorMessage` non-null |
| **C — fails 3× then succeeds** | 3 × `Retry N …`; then `Received HTTP response … 200`; `Accepted 3 new orders` | `Status=Succeeded`, `RetryCount=3`, `FetchedCount=3`, `InsertedCount=3`, `SkippedCount=0` |
| **C′ — same as C, without DB reset** | 3 × `Retry N …`; then `Accepted 0 new orders, skipped 3 duplicates` | `Status=Succeeded`, `RetryCount=3`, `FetchedCount=3`, `InsertedCount=0`, `SkippedCount=3` |

### 6. Verify File mode (optional)

> The importer runs on its own interval (`Ingestion:ImportIntervalSeconds`, default 30 s). After the
> ingestion cycle finishes, wait **at least one full importer interval** before checking the queue
> file. If you stop the worker before the next importer tick, the queue will still contain the
> unimported orders — this is expected: the queue is durable storage, not a transient buffer.

To exercise the async-delivery simulation:

1. Set `Ingestion:Mode` to `File` in `appsettings.json` (or `Ingestion__Mode=File` as an environment
   variable). Reset the database first (see the reset command at the top of §5).
2. Start the worker. Two log rows are produced per cycle: one from `OrderIngestionService`
   (`RunKind=Ingestion`) and one or more from `FileToSqliteImporter` (`RunKind=Import`).
3. Verify:
   - `data/orders-queue.jsonl` exists after the ingestion run and shrinks after the importer runs.
   - No `*.processing` file remains after the importer finishes (leftovers indicate a crash).
   - `SELECT * FROM IngestionLogs WHERE RunKind = 'Import'` shows `InsertedCount > 0` on the first
     run and `SkippedCount > 0` on a re-run with the same source data.
4. Restore `Ingestion:Mode = Sqlite` after the test.

### 7. Run automated tests

```bash
dotnet test
```
```
Test summary: total: 24; failed: 0; succeeded: 24; skipped: 0; duration: 19,5s
Build succeeded in 47,6s
```
See [003-test-strategy.md](../docs/003-test-strategy.md) for what each test proves and which behaviours are deliberately out of scope.