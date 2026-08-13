# 實作任務：xUnit 自動化併發測試專案

你要實作一個獨立的 xUnit 測試專案，把第 2 步手動跑的併發測試情境自動化，方便重複執行與之後跨分支比較。

## 開始前必讀

- [../docs/test-plan.md](../docs/test-plan.md) — 「自動化測試腳本設計」與「跨分支重複使用測試」兩個章節是這個任務的完整規格
- [../docs/api-spec.md](../docs/api-spec.md) — API 規格

## 這階段要做的事

1. 建立獨立測試專案 `Inventory.ConcurrencyTests`，跟一般 unit test 分開，避免拖慢日常測試回饋循環
2. 用 xUnit 的 `[Trait("Category", "Concurrency")]` 標記所有測試
3. **API host**：用 `WebApplicationFactory<Program>` 以 in-process 方式啟動 API
4. **資料庫：不用容器**。直接連現有的實體 PostgreSQL，每次測試在 Arrange 階段建立一個獨立 schema(命名格式為「毫秒時間戳 + 隨機字串」,例如 `test_20260813153042123_a1b2c3d4...`——單靠毫秒時間戳不夠,xUnit 預設會平行跑不同測試類別,同一毫秒內撞名是實測會發生的事,一定要加隨機字串),把 `Product`、`InventoryTransaction` 建在裡面,測試連線的 `search_path` 指到這個 schema。**測完不要砍 schema**——保留下來是為了之後能人工連進去複查資料,這是刻意的設計決定,不是因為沒有 Docker
5. 情境 A / B / C 各對應一個 `[Fact]`（或 `[Theory]` 搭配不同併發度、初始庫存參數化），Act 階段的併發發送邏輯（`Task.WhenAll` + `SemaphoreSlim` + 非同步門閂,不要用會阻塞執行緒的 `Barrier`,見 test-plan.md「共通測試環境準備」)可以跟第 2 步 Console App 共用同一套手法
6. Assert 階段依 test-plan.md 每個情境的「判定標準」寫斷言，注意用範圍式斷言（例如 `finalQuantity > 0`），不要斷言精確數值，因為併發時序本身不確定

## 完成定義

- 三個測試都能重複執行，且在 baseline（未加任何鎖的）分支上穩定通過（即穩定重現髒資料現象）
- 每次執行都會留下一個獨立命名的 schema，測試結束後仍可用 DB 工具連進去查看該次跑出的資料
- 測試專案本身有清楚標記（Trait），方便 CI 選擇性排除

## 之後會怎麼用（不用現在做，先知道即可）

之後在第 3 步的每個併發控制修正分支上，會沿用同一套測試骨架，只把 assertion 方向反過來（從「預期弄髒」改成「預期不弄髒」），細節見 test-plan.md「跨分支重複使用測試」。
