
--CREATE UNIQUE INDEX "IX_Orders_ExternalId" ON "Orders" ("ExternalId")
SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name='Orders';
SELECT * FROM sqlite_sequence

SELECT datetime(), julianday('now')

SELECT * FROM IngestionLogs ORDER BY StartedAtUtc;
SELECT * FROM Orders;
----------------------------------------------------------------------
/* RESET
DELETE FROM IngestionLogs;
DELETE FROM Orders;
-- Reset auto-increment counters so IDs start at 1 again:
DELETE FROM sqlite_sequence WHERE name IN ('Orders', 'IngestionLogs');
VACUUM;
--*/
----------------------------------------------------------------------
-- MONITORING 
--1. Recent failed runs
SELECT *
FROM IngestionLogs
WHERE Status = 'Failed'
ORDER BY StartedAtUtc DESC
LIMIT 20;

--2. Last successful execution
SELECT MAX(FinishedAtUtc) AS LastSuccessfulRunUtc
FROM IngestionLogs
WHERE Status = 'Succeeded';

--3. Runs with unusually high duration
SELECT *, (julianday(FinishedAtUtc) - julianday(StartedAtUtc)) * 86400 AS DurationSeconds
FROM IngestionLogs
WHERE FinishedAtUtc IS NOT NULL
  AND (julianday(FinishedAtUtc) - julianday(StartedAtUtc)) * 86400 > 60;

--4. Last 10 runs
SELECT StartedAtUtc, FinishedAtUtc,
       (julianday(FinishedAtUtc) - julianday(StartedAtUtc)) * 86400 AS DurationSeconds,
       FetchedCount, InsertedCount, SkippedCount, RejectedCount,
       RetryCount, Status,
       ErrorMessage
FROM IngestionLogs
ORDER BY StartedAtUtc DESC
LIMIT 10;

--5. Stale Running rows
SELECT *
FROM IngestionLogs
WHERE Status = 'Running'
  AND (julianday('now') - julianday(StartedAtUtc)) * 86400 > 1800; -- 30 min