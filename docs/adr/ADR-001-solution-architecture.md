### ADR 1: Adoption of Clean Architecture
* **Status:** Approved
* **Context:** The system needs to ingest orders reliably from upstream services. The core business rules around order eligibility and structural processing must remain stable, even if database technologies or external API contracts change.
* **Decision:** We separate the codebase into four concentric layers (`Domain`, `Application`, `Infrastructure`, `Worker`). Dependencies point exclusively inward.
* **Consequences:** 
  * Business rules inside `Domain` and `Application` are isolated and highly testable.
  * Infrastructure can be swapped out with minimal impact on business use cases.
  * Slightly higher initial boilerplate code due to mapping between layers.

### ADR 2: Infrastructure Inversion via Interfaces
* **Status:** Approved
* **Context:** The `Application` layer needs to save orders and call external services, but according to Clean Architecture rules, it cannot directly reference the `Infrastructure` layer.
* **Decision:** All infrastructure operations are declared as interfaces inside `OrderIngestion.Application`. The `OrderIngestion.Infrastructure` layer implements these interfaces, and the `Worker` layer hooks them together using Dependency Injection.
* **Consequences:** 
  * decouples the business flow from concrete implementations like Entity Framework or HttpClient.
  * Allows straightforward mocking during unit and integration testing.

### ADR 3: Worker as the Composition Root
* **Status:** Approved
* **Context:** A centralized location is needed to handle application startup, configuration parsing, background execution schedules, and DI container setup.
* **Decision:** The `OrderIngestion.Worker` project serves as the system executable and Composition Root. It maintains dependencies on all other projects exclusively to wire up infrastructure classes to application interfaces.
* **Consequences:**
  * Keeps startup configurations decoupled from core business code.
  * Changes to hosting infrastructure (e.g., migrating from a Worker Service to an AWS Lambda or Web API) only impact this boundary layer.

### ADR 4: Persistence Layer via SQLite (Demo Environment)
* **Status:** Approved
* **Context:** The system requires a low-overhead data store for demonstration, rapid prototyping, and minimal deployment friction.
* **Decision:** We use SQLite via Entity Framework Core inside `OrderIngestion.Infrastructure`.
* **Consequences:** Provides a fully functional relational database without external server infrastructure. Care must be taken around concurrency limits if production scale changes.

### ADR 5: Scheduling via Native BackgroundWorker
* **Status:** Approved
* **Context:** Order ingestion must occur periodically in the background without introducing external dependencies like Quartz.net or Hangfire.
* **Decision:** We use .NET's native `BackgroundService` combined with a `PeriodicTimer` inside `OrderIngestion.Worker`.
* **Consequences:** Clean, built-in asynchronous loops with no external infrastructure footprint. Advanced features like job persistence, retry UI dashboard, or cluster locking are absent.

### Diagram

```      
      +-----------------------------------------+
      |         OrderIngestion.Worker           |
      |   (Host, Scheduler, DI, Config)         |
      +-------------------+---------------------+
                          |
            +-------------+-------------+
            |                           |
            v                           v
+---------------------------+   +---------------------------+
|OrderIngestion.Infrastructure| |OrderIngestion.Application |
| (HTTP Clients, Persistence) | |(Orchestration & Interfaces|
+--------------+------------+   +-------------+-------------+
               |                              |
               +--------------+---------------+
                              |
                              v
                +---------------------------+
                |    OrderIngestion.Domain  |
                |   (Order & Core Concepts) |
                +---------------------------+
```

### Delivery semantics (footnote)

The diagram above describes **layer dependencies**, not runtime control flow. At runtime, the Worker
fetches from the source and persists to SQLite **synchronously** within a single run. In production,
the fetch-to-persist path would be split into "accept fast, enqueue, consume asynchronously" — see
[ADR-004](ADR-004-delivery-semantics-sync-vs-queued.md). `Ingestion:Mode = File` in this repo is a
working simulation of that pattern.