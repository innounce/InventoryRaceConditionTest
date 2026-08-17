# 高併發庫存系統——並發控制機制實驗

以庫存扣減為場景，在七個分支上實作五種並發控制機制（Redis 分散式鎖含三個 timeout 變體），用同一套測試情境對照正確性與吞吐量的取捨。

---

## 專案目的

先建一套刻意沒有任何鎖的庫存系統（`master`），用 1000 個並發請求把 race condition 打出來，確認重現後，再在每個獨立分支上導入一種機制，重跑同一套測試，觀察正確性和吞吐量如何變化。

---

## 技術棧

- **後端**：ASP.NET Core Web API (.NET 9)
- **資料庫**：PostgreSQL + EF Core（Npgsql）
- **測試**：xUnit + `WebApplicationFactory`（in-process，不需啟動獨立 server）
- **並發測試基礎設施**：`TaskCompletionSource` 非同步門閂（取代 `Barrier`，避免耗盡執行緒池）
- **Redis**：StackExchange.Redis（分散式鎖分支）

---

## 快速開始

### 環境需求

- .NET 9 SDK
- PostgreSQL（本機或遠端）
- Redis（僅 `feature/distributed-lock*` 分支需要）

### 初始設定

```bash
# 建立資料庫使用者與資料庫
sudo -u postgres psql -c "CREATE ROLE inventory_app LOGIN PASSWORD 'YOUR_PASSWORD';"
sudo -u postgres psql -c "CREATE DATABASE inventory_dev OWNER inventory_app;"

# 設定連線字串（user-secrets，不進版控）
cd src/Inventory.Api
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=inventory_dev;Username=inventory_app;Password=YOUR_PASSWORD;Maximum Pool Size=60"

# 執行 migration
dotnet ef database update
cd ../..
```

> `Maximum Pool Size=60` 是刻意設定——沒有這個上限，1000 並發測試會耗盡 PostgreSQL 預設的 `max_connections=100`，產生大量 500 錯誤，污染測試訊號。

### 啟動 API

```bash
dotnet run --project src/Inventory.Api --urls http://localhost:5279
# Swagger: http://localhost:5279/swagger
```

### 執行並發測試

測試使用 `WebApplicationFactory` 在 process 內啟動 API，不需要手動啟動 server，但需要 PostgreSQL 運行中。

```bash
# 執行全部三個情境
DOTNET_USE_POLLING_FILE_WATCHER=true dotnet test tests/Inventory.ConcurrencyTests

# 單獨執行某個情境
dotnet test tests/Inventory.ConcurrencyTests --filter "FullyQualifiedName~ScenarioATests"
dotnet test tests/Inventory.ConcurrencyTests --filter "FullyQualifiedName~ScenarioBTests"
dotnet test tests/Inventory.ConcurrencyTests --filter "FullyQualifiedName~ScenarioCTests"
```

> `DOTNET_USE_POLLING_FILE_WATCHER=true`：避免 1000 並發測試耗盡 Linux inotify 實例上限（預設 128）導致後續測試失敗。

### 清理測試殘留 schema

測試完不會自動刪除 schema（保留供人工查閱）：

```bash
PGPASSWORD=YOUR_PASSWORD psql -h localhost -U inventory_app -d inventory_dev -c "
DO \$\$
DECLARE r RECORD;
BEGIN
  FOR r IN SELECT nspname FROM pg_namespace WHERE nspname LIKE 'test_%' LOOP
    EXECUTE format('DROP SCHEMA %I CASCADE', r.nspname);
  END LOOP;
END \$\$;"
```

---

## 測試情境設計

| 情境 | 目的 | 設定 | 判定指標 |
|------|------|------|---------|
| **A — Lost Update** | 讀-改-寫是否互相覆蓋 | 初始庫存 1000，1000 req 各扣 1 | `version == successCount`，無重複 `balanceAfter` |
| **B — 負庫存** | 業務規則在並發下是否失守 | 初始庫存 100，1000 req 各扣 1 | `finalQuantity >= 0`，`successCount <= 100` |
| **C — 對帳一致性** | 長時間混合進出是否對得上帳 | 初始庫存 500，5 秒 50 並發混合 | `500 ± Σ交易 == finalQuantity` |

`master` 分支：斷言「弄髒了才算通過」（驗證問題存在）。  
各 feature 分支：斷言反轉（驗證機制有效）。

---

## 各分支與 master 的實作差異

所有分支都以 `master` 為基礎，只修改最小範圍的生產程式碼。測試執行邏輯與並發基礎設施共用；斷言方向在各 feature 分支反轉——`master` 斷言「弄髒了才算通過」，feature 分支斷言「正確才算通過」。

### `master` — 控制組（無鎖）

`InventoryService.StockOutAsync` 的核心邏輯：

```csharp
var product = await productRepository.GetByIdAsync(productId);
product.Quantity -= request.Quantity;   // 記憶體內修改
await productRepository.SaveChangesAsync();  // 寫回，無版本檢查
```

read → modify in memory → write，三步之間沒有任何保護。多個請求同時執行時，後寫的會覆蓋先寫的（lost update）。

---

### `feature/optimistic-lock` — 樂觀鎖

**改動：** 只加一個 try-catch，不動 SQL。

```csharp
try
{
    await productRepository.SaveChangesAsync();
    // EF Core 生成：UPDATE "Product" ... WHERE "Id"=@id AND "Version"=@old
    // 若另一個請求已先更新，Version 不符，影響 0 行 → DbUpdateConcurrencyException
}
catch (DbUpdateConcurrencyException)
{
    throw new OptimisticConcurrencyException();  // → 409 Conflict
}
```

`Version` 欄位標記 `[ConcurrencyCheck]`，EF 自動在 UPDATE 的 WHERE 加版本條件。衝突由資料庫偵測，應用層拋 409，客戶端決定是否重試。

---

### `feature/pessimistic-lock` — 悲觀鎖

**改動：** 開交易 + 改用 `SELECT ... FOR UPDATE` 讀取。

```csharp
await using var tx = await productRepository.BeginTransactionAsync();
var product = await productRepository.GetByIdForUpdateAsync(productId);
// 此列被鎖住，其他請求在 GetByIdForUpdateAsync 這行阻塞，直到 tx.CommitAsync()
product.Quantity -= request.Quantity;
await productRepository.SaveChangesAsync();
await tx.CommitAsync();
```

等待發生在資料庫內，等待期間持有 DB 連線。不需要客戶端重試。

---

### `feature/serializable-isolation` — Serializable 隔離層級

**改動：** 開 Serializable 交易，捕捉 PostgreSQL SQLSTATE 40001。

```csharp
await using var tx = await productRepository.BeginTransactionAsync(IsolationLevel.Serializable);
try
{
    var product = await productRepository.GetByIdAsync(productId);  // 普通 SELECT，不加鎖
    product.Quantity -= request.Quantity;
    await productRepository.SaveChangesAsync();
    await tx.CommitAsync();
}
catch (Exception ex) when (IsSerializationFailure(ex))  // SQLSTATE 40001
{
    throw new SerializationFailureException();  // → 409
}
```

不寫任何鎖語法，由 PostgreSQL SSI（Serializable Snapshot Isolation）自動偵測衝突並中止交易。代價是存在誤殺（false abort）——本來不衝突的交易在高爭用下也可能被中止。

---

### `feature/distributed-lock` — Redis 分散式鎖（0ms 等待）

**新增檔案：** `Locking/IDistributedLockFactory.cs`、`Locking/RedisDistributedLockFactory.cs`

**改動：** 在 read 之前先向 Redis 取鎖。

```csharp
await using var lock_ = await AcquireLockAsync(productId);
// SET inventory:lock:{id} {token} NX PX 10000
// 若 key 已存在 → 立即 throw LockAcquisitionFailedException → 409

var product = await productRepository.GetByIdAsync(productId);
product.Quantity -= request.Quantity;
await productRepository.SaveChangesAsync();
// lock_ Dispose 時執行 Lua 腳本釋放鎖（token 一致才刪）
```

等待在 Redis，不佔 DB 連線。0ms 版本取不到鎖就立即失敗，客戶端必須自行重試。

---

### `feature/distributed-lock-retry` / `feature/distributed-lock-retry-5s` — Redis 鎖加等待

與 `feature/distributed-lock` 相同，只改 `TryAcquireAsync` 的第三個參數：

```csharp
// distributed-lock-retry：等最多 500ms
await lockFactory.TryAcquireAsync(key, LockExpiry, TimeSpan.FromMilliseconds(500));

// distributed-lock-retry-5s：等最多 5s
await lockFactory.TryAcquireAsync(key, LockExpiry, TimeSpan.FromSeconds(5));
```

等待期間在 Redis 輪詢，到期才失敗。timeout 越長，成功率越高，但 P99 延遲也越高。

---

### `feature/queue-based-serialization` — Channel 單寫者佇列

**新增檔案：** `Queue/InventoryChannel.cs`、`Queue/InventoryQueueWorker.cs`、`Queue/StockWorkItem.cs`、`Services/QueuedInventoryService.cs`

**機制：** `InventoryService` 本身不動（仍是無鎖的 read-modify-write），但把它包在單一消費者的 `Channel<T>` 後面，讓所有請求序列化執行。

```csharp
// QueuedInventoryService（HTTP handler 呼叫這個）
public Task<StockChangeResponse> StockOutAsync(Guid productId, StockChangeRequest request)
{
    var tcs = new TaskCompletionSource<StockChangeResponse>();
    await channel.Writer.WriteAsync(new StockWorkItem(svc => svc.StockOutAsync(...), tcs));
    return await tcs.Task;  // 等 worker 執行完才回傳
}

// InventoryQueueWorker（BackgroundService，SingleReader）
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    var result = await item.Work(inventoryService);  // 一次只跑一筆
    item.Completion.SetResult(result);
}
```

等待在 process 記憶體（`await tcs.Task`），沒有網路往返、沒有輪詢間隔，吞吐量最高。但 `Channel<T>` 是 in-process 的，無法跨多台 server 共享。

---

## 分支與實測結果

完整的 successCount / P50 / P99 / minSuccessLatencyMs 數字見 [docs/conclusions-comparison.md](docs/conclusions-comparison.md)。

**情境 A（1000 並發暴衝，初始庫存 1000）：** 丟棄型機制（樂觀鎖、Redis 0ms）successCount = 1；排隊型（悲觀鎖、Queue）= 1,000。Redis timeout 從 0ms 調到 5s，成功數從 1 增至 947，代價是 P99 從 127ms 升至 5,056ms。

**情境 B（1000 並發暴衝，初始庫存 100）：** 控制組成功 241 筆，超出初始庫存 141 筆，確認 race condition。悲觀鎖、Redis 5s、Queue 精確成功 100 筆。Serializable Isolation 因誤殺（false abort）僅達 47 筆。

**情境 C（5 秒持續壓力，50 並發）：** Queue 成功率 90.2% 最高且 P99 最穩（186ms）。Redis 0ms 在 5 秒送出 46,602 筆，快速失敗讓 totalRequests 暴增，但成功率僅 0.7%。控制組對帳不符，其餘機制均對帳一致。

---

## 核心結論

**吞吐量上限由臨界區決定，不由鎖機制決定**

各機制最快成功請求的端到端延遲約落在 5–18ms，代表一筆請求佔用共享資源的最短時間：

```
最大 TPS（單一商品）= 1 / 臨界區執行時間 ≈ 1 / 5ms = 200 TPS
```

Redis 再快、Queue 再輕，都無法突破這個上限。差別只在排隊等待的位置：DB 連線（悲觀鎖）、Redis 記憶體（分散式鎖）、process 記憶體（Queue）。

**丟棄 vs 排隊決定了成功率，不是機制種類**

- 丟棄型（樂觀鎖、Serializable、Redis 0ms）：衝突立即失敗，低延遲，需客戶端重試
- 排隊型（悲觀鎖、Redis 5s、Queue）：衝突等待，高成功率，無需客戶端重試

**Redis timeout 是不改架構就能調整成功率與延遲的唯一旋鈕**

情境 A 成功數：1（0ms）→ 39（500ms）→ 947（5s）。

詳細機制取捨分析與選擇建議見 [docs/conclusions-comparison.md](docs/conclusions-comparison.md)。

---

## 文件索引

| 文件 | 內容 |
|------|------|
| [docs/api-spec.md](docs/api-spec.md) | 完整 request/response 規格、錯誤格式、狀態碼 |
| [docs/db-schema.md](docs/db-schema.md) | `Product`、`InventoryTransaction` 欄位定義 |
| [docs/test-plan.md](docs/test-plan.md) | 三個測試情境的詳細步驟、判定標準、自動化測試設計 |
| [docs/operations.md](docs/operations.md) | 啟動 API、執行測試、清理 schema 的實際指令 |
| [docs/conclusions-comparison.md](docs/conclusions-comparison.md) | 所有機制結果總表與選擇建議 |
| [docs/conclusions-optimistic-lock.md](docs/conclusions-optimistic-lock.md) | 樂觀鎖實驗結論 |
| [docs/conclusions-pessimistic-lock.md](docs/conclusions-pessimistic-lock.md) | 悲觀鎖實驗結論 |
| [docs/conclusions-serializable-isolation.md](docs/conclusions-serializable-isolation.md) | Serializable Isolation 實驗結論 |
| [docs/conclusions-distributed-lock.md](docs/conclusions-distributed-lock.md) | Redis 分散式鎖（無等待）實驗結論 |
| [docs/conclusions-distributed-lock-retry.md](docs/conclusions-distributed-lock-retry.md) | Redis 分散式鎖（500ms 等待）實驗結論 |
| [docs/conclusions-queue-based-serialization.md](docs/conclusions-queue-based-serialization.md) | Queue 序列化實驗結論 |
