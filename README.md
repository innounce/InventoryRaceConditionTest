# 高併發庫存系統——並發控制機制實驗

以庫存扣減為場景，依序實作並測量六種並發控制機制，用同一套測試情境對照正確性與吞吐量的取捨。

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

## 分支與實測結果

### 情境 A — 暴衝 1000 req，初始庫存 1000

| 分支 | successCount | 說明 |
|------|-------------|------|
| `master`（無鎖）| ~900+（資料損毀）| Lost update，version 遠小於成功數 |
| `feature/optimistic-lock` | **1** | 衝突立即 409，無重試 |
| `feature/serializable-isolation` | **39** | SSI abort，吞吐量低於悲觀鎖 |
| `feature/distributed-lock`（0ms）| **1** | 立即放棄，等同樂觀鎖 |
| `feature/distributed-lock-retry`（500ms）| **24** | Redis 層排隊 |
| `feature/distributed-lock-retry-5s`（5s）| **946** | 足夠長的等待讓大多數成功 |
| `feature/pessimistic-lock` | **1,000** | DB 層排隊，全部成功 |
| `feature/queue-based-serialization` | **1,000** | in-process Channel，零競爭 |

### 情境 B — 暴衝 1000 req，初始庫存 100

| 分支 | successCount | finalQuantity |
|------|-------------|--------------|
| `master`（無鎖）| ~200+（含負庫存）| < 0（規則失守）|
| `feature/optimistic-lock` | **19** | 81 |
| `feature/distributed-lock`（0ms）| **2** | 98 |
| `feature/serializable-isolation` | **47** | 53 |
| `feature/distributed-lock-retry`（500ms）| **77** | 23 |
| `feature/pessimistic-lock` | **100**（滿）| **0** |
| `feature/distributed-lock-retry-5s`（5s）| **100**（滿）| **0** |
| `feature/queue-based-serialization` | **100**（滿）| **0** |

### 情境 C — 5 秒持續壓力，50 並發，混合進出

| 分支 | 交易筆數 | 對帳 |
|------|---------|------|
| `master`（無鎖）| 高（對帳不符）| ❌ |
| `feature/optimistic-lock` | **179** | ✓ |
| `feature/serializable-isolation` | **235** | ✓ |
| `feature/distributed-lock`（0ms）| **338** | ✓ |
| `feature/pessimistic-lock` | **604** | ✓ |
| `feature/distributed-lock-retry`（500ms）| **1,343** | ✓ |
| `feature/distributed-lock-retry-5s`（5s）| **1,461** | ✓ |
| `feature/queue-based-serialization` | **2,010** | ✓ |

---

## 機制特性對比

| 機制 | 暴衝吞吐量 | 持續吞吐量 | 多伺服器 | DB 連線壓力 | 額外依賴 | 客戶端需 retry |
|------|-----------|-----------|---------|------------|---------|---------------|
| 樂觀鎖 | 極低 | 低 | ✓ | 低 | 無 | 需要 |
| Serializable | 低 | 低-中 | ✓ | 中 | 無 | 需要 |
| 悲觀鎖 | 高 | 中 | ✓ | **高** | 無 | 不需要 |
| Redis（0ms）| 極低 | 低 | ✓ | 低 | Redis | 需要 |
| Redis（500ms）| 低 | 中 | ✓ | 低 | Redis | 需要 |
| Redis（5s）| 高 | 中-高 | ✓ | 低 | Redis | 少量 |
| Queue | **最高** | **最高** | **❌ 單機** | 低 | 無 | 不需要 |

---

## 核心結論

**吞吐量上限由臨界區決定，不由鎖機制決定**

```
最大 TPS（單一商品）= 1 / 臨界區執行時間 ≈ 1 / 5ms = 200 TPS
```

Redis 再快、Queue 再輕，都無法突破這個上限。差別只在排隊開銷放在哪裡。

**悲觀鎖 vs Redis vs Queue 的本質差異**

- 悲觀鎖：等待佔 DB 連線，連線池是瓶頸
- Redis 分散式鎖：等待在 Redis 記憶體，DB 連線只在持鎖後才取出
- Queue（Channel）：等待在 process 記憶體，零網路往返，吞吐量最高但只能單機

**Redis timeout 是唯一可以不改架構就調整吞吐量與延遲的參數**

timeout 越長 → 更多請求成功，但 P99 延遲越高。

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
