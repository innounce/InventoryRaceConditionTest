# 單寫者 Queue 序列化結論

## 機制摘要

所有庫存寫入（StockIn / StockOut）都進入一個 `Channel<StockWorkItem>`（無界、單一消費者），
由唯一一個 `InventoryQueueWorker`（`BackgroundService`）逐一取出並執行。
API 呼叫者在 `TaskCompletionSource<T>` 上 await，直到 worker 處理完畢才得到回應。
讀取（GetTransactions）繞過 Queue 直接呼叫 `InventoryService`，不需序列化。

```
Producer（API 請求） → Channel<StockWorkItem> → Consumer（單一 worker）→ DB
                              ↑ in-process, lock-free
```

不需要任何資料庫層鎖定，也不需要外部基礎設施（無 Redis）。

## 測試結果

| 情境 | 初始庫存 | 請求數 | successCount | finalQuantity | Version | 交易筆數 |
|------|----------|--------|-------------|---------------|---------|---------|
| A Lost Update | 1,000 | 1,000 | **1,000** | **0** | **1,000** | 1,000 |
| B 負庫存 | 100 | 1,000 | **100** | **0** | **100** | 100 |
| C 混合持續（5s, 50 並發）| 500 | — | — | 3 | **2,010** | 2,010 |

**三個情境全部通過，且正確性指標完美：**

- A: `finalQuantity (0) == 1000 - 1000`，`version (1000) == successCount (1000)` ✓
- B: `finalQuantity (0) >= 0`，`successCount (100) <= 100`，`0 == 100 - 100` ✓
- C: `500 + Σ 有號交易 = 3`（finalQuantity），對帳完全吻合 ✓

## 各機制吞吐量總覽

| 機制 | A successCount | B successCount | C 交易筆數 |
|------|---------------|---------------|-----------|
| Baseline（無鎖）| ~900+（資料錯誤）| ~200+（資料錯誤）| 高（資料錯誤）|
| 樂觀鎖 | 個位數 | 個位數 | — |
| 悲觀鎖 | ~1,000 | ~100 | — |
| 可序列化隔離 | ~1,000 | ~100 | — |
| Redis 分散式鎖（0ms）| 1 | 2 | 338 |
| Redis 分散式鎖（500ms）| 24 | 77 | 1,343 |
| Redis 分散式鎖（5s）| 946 | 100 | 1,461 |
| **Queue 序列化** | **1,000** | **100** | **2,010** |

## 為什麼 Queue 的吞吐量最高

### 無輪詢開銷
Redis retry 版每 5ms 輪詢一次 `SET NX`，每次輪詢都是一次 Redis 網路往返（~0.1ms）。
Queue 使用 .NET 的 `Channel<T>`，基於 lock-free 資料結構，item 釋放後 worker 立刻拿到，
不存在任何輪詢或等待週期，交接延遲趨近於零。

### 無網路往返
Redis 鎖每次 acquire / release 至少 2 次網路往返；Queue 全部在 process 記憶體內，
沒有網路。

### 結果：情境 A 1000/1000
1000 個請求全部成功，完全不需要 timeout 設定，也不會有任何請求被拒絕。
worker 逐一處理，每筆 ~4ms，1000 筆共 ~4 秒，全部在測試 timeout 內完成。

## 代價與限制

| 限制 | 說明 |
|------|------|
| **單伺服器** | Channel 在 process 記憶體內，多台 API server 各自有獨立 Queue，寫入無法跨伺服器序列化 |
| **單點 worker** | 整個系統只有一個寫入 thread，所有商品共用同一條序列，不同商品無法並行 |
| **記憶體壓力** | Channel 無界（Unbounded），高流量下 item 堆積在記憶體；需 Bounded Channel + backpressure |
| **重啟遺失** | 尚在 Channel 中未處理的 item，程序重啟後全部消失，需結合持久化 Queue（如 Redis Streams）|
| **尾延遲不可預測** | 第 1000 個請求等了所有前面的人，P99 延遲 = 佇列長度 × 每筆處理時間 |

## 與其他機制的核心差異

```
鎖（悲觀/樂觀/Redis）：
  並發請求同時進入 → 搶鎖 → 勝者執行 → 敗者等待或重試

Queue 序列化：
  並發請求同時進入 → 全部排隊 → 唯一 worker 逐一執行 → 沒有搶鎖、沒有重試
```

鎖是「誰搶到誰先跑」；Queue 是「先到先服務，沒有搶奪」。
Queue 把並發衝突從執行層移到排隊層，完全消除衝突本身。

## 適用場景

| 適合 | 不適合 |
|------|--------|
| 單一 API 伺服器部署 | 多台伺服器橫向擴展 |
| 對吞吐量要求高、對尾延遲容忍度高 | SLA 嚴格限制單請求延遲（P99 < 100ms）|
| 不想依賴外部基礎設施 | 需要程序重啟後仍保留待處理請求 |
| 寫入熱點集中在少數 key | 大量不同商品高並發（可改為 per-key Queue）|

## 結論

Queue 序列化在本測試中達到所有機制中最高的吞吐量（情境 A 1000/1000，情境 C 2,010 TPS），
且正確性完美、實作簡單、無需外部依賴。

代價是**只適合單伺服器**——這是與 Redis 分散式鎖最根本的差異。
如果未來需要橫向擴展，可以把 Channel 替換成 Redis Streams 或 Kafka，
保留「單寫者消費」的語意，同時解決多伺服器問題。
