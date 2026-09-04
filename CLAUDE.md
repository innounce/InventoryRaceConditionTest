# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

This is a deliberate concurrency-study project. The baseline (`master`) intentionally has **no concurrency control** so that race conditions (lost update, negative stock) can be reproduced and measured. Later branches (`feature/optimistic-lock`, `feature/pessimistic-lock`, etc.) each add one locking mechanism and re-run the same tests with reversed assertions.

Do not add locking to `InventoryService` on `master` — the race condition is the point.

## Commands

### First-time setup

```bash
cp .env.example .env        # 可視需要修改帳密
docker compose up -d        # 啟動 PostgreSQL 18（port 5434）與 Redis（port 6381）
```

API 啟動時會自動執行 EF migration，無需手動跑 `dotnet ef database update`。

連線設定已寫在 `appsettings.Development.json`，預設值：
- PostgreSQL：`Host=localhost;Port=5434;Database=inventory_dev;Username=inventory_app;Password=dev_password`
- Redis：`localhost:6381`

> `Maximum Pool Size=60` is intentional — without it, 1000-concurrent tests exhaust PostgreSQL's default `max_connections=100` and produce 500 errors that pollute the concurrency signal.

### Run the API

```bash
dotnet run --project src/Inventory.Api --urls http://localhost:5279
# Swagger: http://localhost:5279/swagger
```

### Run concurrency tests (xUnit integration tests)

The tests start the API in-process via `WebApplicationFactory` — no manual server needed. Docker containers must be running (`docker compose up -d`).

```bash
# All three scenarios
dotnet test tests/Inventory.ConcurrencyTests

# Single scenario
dotnet test tests/Inventory.ConcurrencyTests --filter "FullyQualifiedName~ScenarioATests"
dotnet test tests/Inventory.ConcurrencyTests --filter "FullyQualifiedName~ScenarioBTests"
dotnet test tests/Inventory.ConcurrencyTests --filter "FullyQualifiedName~ScenarioCTests"

# With detailed output
dotnet test tests/Inventory.ConcurrencyTests --logger "console;verbosity=detailed"
```

### Run the manual load-test client

```bash
dotnet run --project src/Inventory.LoadTestClient -- --scenario A --base-url http://localhost:5279
dotnet run --project src/Inventory.LoadTestClient -- --scenario B --base-url http://localhost:5279
dotnet run --project src/Inventory.LoadTestClient -- --scenario C --base-url http://localhost:5279
```

### EF Core migrations

```bash
dotnet ef migrations add <MigrationName> --project src/Inventory.Api
dotnet ef database update --project src/Inventory.Api
```

### Clean up leftover test schemas

Docker PostgreSQL 無持久化，`docker compose down` 後所有 `test_*` schema 隨容器一併消失，無需手動清除。

若需在容器運行中手動清理：

```bash
PGPASSWORD=dev_password psql -h localhost -p 5434 -U inventory_app -d inventory_dev \
  -c "SELECT nspname FROM pg_namespace WHERE nspname LIKE 'test_%' ORDER BY nspname;"

# Drop all test schemas
PGPASSWORD=dev_password psql -h localhost -p 5434 -U inventory_app -d inventory_dev -c "
DO \$\$
DECLARE r RECORD;
BEGIN
  FOR r IN SELECT nspname FROM pg_namespace WHERE nspname LIKE 'test_%' LOOP
    EXECUTE format('DROP SCHEMA %I CASCADE', r.nspname);
  END LOOP;
END \$\$;"
```

## Architecture

### Solution layout

```
src/
  Inventory.Api/           # ASP.NET Core Web API (.NET 9)
  Inventory.LoadTestClient/ # Console app: manual concurrency tester
tests/
  Inventory.ConcurrencyTests/ # xUnit integration tests (concurrency scenarios)
```

### Inventory.Api layers

`Controller → Service → Repository → InventoryDbContext (EF Core / Npgsql / PostgreSQL)`

- **Controllers** (`ProductsController`) — thin; delegates to `IProductService` / `IInventoryService`, maps exceptions via `ExceptionHandlingMiddleware`.
- **Services** — business logic. `InventoryService.StockInAsync/StockOutAsync` is the deliberately racy read-modify-write path. `ProductService` handles CRUD.
- **Repositories** — `ProductRepository` / `InventoryTransactionRepository` wrap EF Core queries.
- **Middleware** — `ExceptionHandlingMiddleware` converts domain exceptions (`InsufficientStockException`, `ProductNotFoundException`, `InvalidQuantityException`) into structured JSON error responses with the correct HTTP status code.
- **Models** — `Product` (with `Version` counter for post-hoc lost-update detection) and `InventoryTransaction` (append-only audit log).

Every `stock-in` / `stock-out` must write both a `Product` update and an `InventoryTransaction` row — the transaction log is the ground truth for reconciliation.

### Test infrastructure (Inventory.ConcurrencyTests)

- Uses `WebApplicationFactory<Program>` (in-process host) with the DbContext replaced to point at a per-test PostgreSQL schema named `test_<timestamp>_<guid>`.
- Schemas are **never dropped after the test** — they're left for manual inspection. Clean them up manually when storage is a concern.
- `EnsureCreated()` doesn't work here (it checks if any tables exist in the DB, sees `public.Product`, and no-ops). `MigrateAsync()` is used instead because it tracks migrations inside the per-test schema.
- Both `DbContextOptions<InventoryDbContext>` and `InventoryDbContext` registrations must be removed before re-adding the scoped one — removing only the options descriptor is silently ignored.

### Async gate pattern (ConcurrentBurst / ConcurrentDispatcher)

All burst tests use a `TaskCompletionSource` gate instead of `Barrier` or `ManualResetEventSlim`. Blocking primitives starve the thread pool when hundreds of threads all call `SignalAndWait()` simultaneously (the pool grows at ~1 thread/500 ms, so 1000 waiters take minutes to release). The async gate lets every task `await gate.Task` without occupying a thread, then releases all at once when the last task arrives.

### Branch strategy for concurrency mechanisms

| Branch | Mechanism |
|---|---|
| `master` | Baseline — no locking |
| `feature/optimistic-lock` | `Version` field compare-and-swap, `409` on conflict |
| `feature/pessimistic-lock` | `SELECT ... FOR UPDATE` |
| `feature/serializable-isolation` | PostgreSQL Serializable isolation level |
| `feature/distributed-lock` | Redis distributed lock |
| `feature/queue-based-serialization` | Single-writer queue |

On feature branches, flip assertion direction: baseline asserts dirty (`finalQuantity > 0`, `successCount > 100`); fixed branches assert clean (`finalQuantity == 0`, `successCount == 100`).
