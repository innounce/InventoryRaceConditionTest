# 實作任務：第 3 步 — 修正併發問題（分支開發）

你要在一個獨立分支上，針對第 1 步的 baseline 系統實作特定的併發控制機制，修正第 2 步驗證出的弄髒問題。**開始前先確認目前 checkout 在哪個分支**，只實作該分支對應的那一種機制，不要一次做多種、也不要動到 baseline 分支或其他機制分支的程式碼。

## 開始前必讀

- [../README.md](../README.md) — 第 3 步：修正併發問題、分支策略（列出五種機制各自對應的分支名稱）
- [../docs/test-plan.md](../docs/test-plan.md) — 「跨分支重複使用測試」章節，說明哪些測試邏輯要沿用、assertion 方向怎麼反轉、各機制專屬要補的觀察指標
- [../docs/db-schema.md](../docs/db-schema.md)、[../docs/api-spec.md](../docs/api-spec.md) — 修正時如果需要擴充回應格式（例如樂觀鎖衝突要回 `409`），請對照現有規格，只在必要處擴充，不要破壞既有欄位與既有端點行為

## 這階段要做的事

1. 依目前分支對應的機制修改 Service 層（以及必要時的 API 回應，例如樂觀鎖衝突要回 `409 Conflict`）：
   - `feature/optimistic-lock`：比對 `Version`，不符就回 `409`
   - `feature/pessimistic-lock`：用 `SELECT ... FOR UPDATE` 鎖資料列
   - `feature/serializable-isolation`：改用 PostgreSQL Serializable isolation level
   - `feature/distributed-lock`：引入 Redis 分散式鎖
   - `feature/queue-based-serialization`：寫入請求丟進 Queue，由單一 worker 序列化處理
2. 把 `concurrency-test-project.md` 產出的 xUnit 測試（或第 2 步的 Console App）重新對這個分支跑，依 test-plan.md「跨分支重複使用測試」的規則把 assertion 方向反過來（從「預期弄髒」改成「預期不弄髒」）
3. 補上該機制專屬的觀察指標（見 test-plan.md 的機制對照表，例如樂觀鎖要記錄衝突率、悲觀鎖要記錄等待鎖的時間）
4. 重跑情境 A/B/C，記錄「是否還弄髒」與「throughput/latency 相較 baseline 的變化」

## 完成定義

- 情境 A/B/C 重跑後，反轉方向的 assertion 全部通過（不再出現 test-plan.md 定義的髒資料現象）
- 有記錄該機制的效能代價（相較 baseline 的 throughput/latency 變化）與機制專屬指標
- 只改動這個分支該負責的機制，沒有動到 baseline 分支或其他機制分支
