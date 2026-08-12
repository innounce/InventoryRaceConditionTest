# 測試計畫（高併發弄髒系統）

目的：用測試 client（見 [README](../README.md) 第 2 步）對後端 API（見 [API 規格](api-spec.md)）發動大量併發請求，在沒有任何併發控制的 baseline 版本上，具體觀察並記錄系統被「弄髒」的現象，作為之後導入樂觀鎖／悲觀鎖等機制的效能與正確性比較基準。

## 共通測試環境準備

- 每個情境開始前，先用 `POST /products` 建立一個**全新商品**（不要重複用同一個商品跑不同情境，避免互相污染），記錄回傳的 `id`、初始 `quantity`、`version`（應為 0）。
- 測試 client 對每一個送出的請求都要記錄：送出時間、回應狀態碼、回應內容中的 `quantity` / `version`、耗時（latency）。這些資料是測完後分析用的原始素材。
- 為了讓併發真的「同時」發生（而不是陸續送出），起跑時建議用 `Barrier` 或 `ManualResetEventSlim` 讓所有 Task 先卡在同一個點，倒數結束後同時放行，最大化 race window。若只是單純 `Task.WhenAll` 依序建立 Task，前面的請求可能已經跑完，較難重現真正的併發碰撞。

## 共通分析方法（每個情境測完都要做）

1. 統計實際成功請求數（狀態碼 `200`）、失敗數與錯誤代碼分布。
2. 用 `GET /products/{id}/transactions` 撈出全部紀錄，依 `createdAt` 排序後檢查：
   - 紀錄筆數是否等於成功請求數（有沒有漏寫）
   - `balanceAfter` 序列是否連續遞增／遞減、有無重複值（重複代表兩個併發請求讀到同一個舊庫存值去計算）
   - 最後一筆 `balanceAfter` 是否等於 `GET /products/{id}` 當下回傳的 `quantity`
3. 檢查 `Product.version` 最終值是否等於成功請求數（若小於，代表有寫入被覆蓋，也就是 lost update）
4. 對帳：`Product.quantity` 是否等於「初始庫存 ± SUM(InventoryTransaction.quantity)」
5. 記錄整體 throughput（req/s）與 p50 / p95 / p99 latency，作為之後加鎖機制後的效能比較基準
6. 可搭配 PostgreSQL 的 `pg_stat_activity`、`pg_locks` 觀察測試當下的併發連線與鎖等待情形

---

## 情境 A：Lost Update 驗證

**目的**：驗證「讀-改-寫」在併發下是否會互相覆蓋，導致部分扣減操作消失。

**前置條件**：建立商品，初始庫存 `quantity = 1000`。

**測試步驟**：
1. 準備 1000 個 `POST /products/{id}/stock-out`請求，每個 `quantity = 1`
2. 併發度設定為 500～1000（用 `SemaphoreSlim` 或直接開滿），透過 Barrier 同時放行
3. 全部請求送完並收到回應後，暫停 1 秒（確保沒有還沒寫完的請求），再開始分析

**預期結果（健康系統應有的結果）**：最終 `quantity = 0`，成功請求數 = 1000，`version` 最終值 = 1000。

**測試後觀察重點**：
- 最終 `quantity` 是否 **大於 0**（理論上打了 1000 次扣 1，庫存應該歸零）
- `version` 最終值是否 **小於** 實際成功請求數
- `GET /products/{id}/transactions` 撈出的紀錄數是否等於成功請求數，`balanceAfter` 序列有沒有出現重複值（例如兩筆紀錄都是 `balanceAfter = 950`，代表兩個請求同時讀到 951 去扣 1）

**判定標準（怎樣算「弄髒了」）**：只要最終 `quantity != 0`，或 `version` 最終值 < 成功請求數，或 `balanceAfter` 出現重複值，三者任一成立就代表發生 lost update。

---

## 情境 B：負庫存驗證

**目的**：驗證「庫存不可為負數」這個業務規則在併發下是否還守得住。

**前置條件**：建立商品，初始庫存 `quantity = 100`。

**測試步驟**：
1. 準備 1000 個 `POST /products/{id}/stock-out` 請求，每個 `quantity = 1`（遠超過庫存量）
2. 併發度設定為 500 以上，同樣用 Barrier 同時放行
3. 全部請求送完後分析

**預期結果（健康系統應有的結果）**：只有前 100 次扣減成功，之後 900 次應收到 `400 INSUFFICIENT_STOCK`，最終 `quantity = 0`（不會是負數）。

**測試後觀察重點**：
- 最終 `quantity` 是否 **小於 0**
- 成功請求數是否 **超過 100**（理論上初始庫存只夠 100 次成功）
- 失敗請求（400）的比例與時間分布：是集中在後段，還是全程混雜（混雜代表判斷庫存是否足夠的檢查本身就有 race condition，不是單純「扣到後面才不夠」）

**判定標準**：最終 `quantity < 0`，或成功請求數 > 100，即代表「庫存不可為負」的業務規則在併發下失守。

---

## 情境 C：長時間混合壓力測試

**目的**：模擬較真實的使用情境（進貨、出貨交錯發生），驗證帳務資料在長時間高頻寫入下是否還能維持一致性。

**前置條件**：建立商品，初始庫存 `quantity = 500`。

**測試步驟**：
1. 啟動一段時間（例如 60 秒）的測試，期間持續發送請求：約 60% 為 `stock-out`（`quantity = 1~5` 隨機），40% 為 `stock-in`（`quantity = 1~5` 隨機）
2. 併發度維持一個中等值（例如 100），全程持續發送，不使用 Barrier 一次性放行（因為這裡要模擬的是持續流量，不是單一瞬間爆量）
3. 過程中即時記錄每筆請求結果；`stock-out` 若遇到庫存不足回傳 400 屬正常情況，需與其他錯誤分開統計

**預期結果（健康系統應有的結果）**：`Product.quantity` 應等於「初始庫存 + 所有成功 IN 的總和 - 所有成功 OUT 的總和」，且不會出現負數。

**測試後觀察重點**：
- 對帳是否成功：`Product.quantity` 是否等於初始庫存 ± `SUM(InventoryTransaction.quantity)`
- `InventoryTransaction` 紀錄總筆數是否等於（成功 IN 次數 + 成功 OUT 次數）
- 長時間運行下 latency 是否隨時間變長（可能代表沒有索引，或連線數耗盡等額外問題，不是本練習核心但值得記錄）

**判定標準**：對帳結果不一致（`quantity` 與交易紀錄總和對不上），即代表長時間混合寫入下資料已經髒掉。

---

## 自動化測試腳本設計（xUnit + 整合測試）

### 定位澄清

這些腳本**不是傳統定義的 unit test**——不 mock、不隔離依賴，必須打真實 API、連真實 PostgreSQL 才能重現併發時的 race condition，本質上是**整合測試／併發測試**。用 xUnit 撰寫是為了取得結構化的 Arrange/Act/Assert、可重複執行、能整合進 CI，而不是因為它符合 unit test 的定義。

### 專案結構

- 獨立開一個測試專案，例如 `Inventory.ConcurrencyTests`，跟一般 unit test（如果之後有的話）分開，避免拖慢日常的快速測試回饋循環。
- 用 xUnit 的 `[Trait("Category", "Concurrency")]` 標記這些測試，CI 可以選擇性地只在特定 pipeline（而非每次 PR）才執行。

### 測試基礎設施

- **API host**：用 `WebApplicationFactory<Program>` 讓 API 以 in-process 方式啟動，測試直接用它產生的 `HttpClient` 打請求，不用另外手動啟動一個 server。
- **資料庫**：用 `Testcontainers.PostgreSql` 在每次測試執行時開一個一次性、乾淨的 Postgres 容器，測試結束自動銷毀。這樣每次跑都是全新環境、不受先前測試殘留資料影響，CI 上也不用額外準備固定的 Postgres 環境。
- 每個測試方法（對應情境 A / B / C）在 Arrange 階段都建立一個全新商品，跟手動測試的「共通環境準備」原則一致，避免情境間互相污染。

### 測試方法對應

- 情境 A / B / C 各對應一個 xUnit `[Fact]`（或 `[Theory]` 搭配不同併發度、初始庫存做參數化）
- Act 階段：用 `Task.WhenAll` + `SemaphoreSlim` / `Barrier` 發動併發請求，跟 [README](../README.md) 第 2 步 Client 的併發發送邏輯一致，測試裡可以直接複用同一套邏輯，不用重寫一份
- Assert 階段：把本文件每個情境的「判定標準」直接轉成 assertion

### 斷言設計注意事項（跟一般 unit test 最大的不同）

併發時序本身有不確定性，不能斷言「一定弄髒到多精確的程度」，只能斷言「不健康的現象至少出現一次」或「數值落在不健康的範圍」：

- **情境 A**：`Assert.True(finalQuantity > 0)`，而不是斷言等於某個精確值——因為每次跑，實際 lost update 發生的次數會不同。
- **情境 B**：`Assert.True(finalQuantity < 0 || successCount > 100)`。
- **情境 C**：對帳斷言反而可以精確——`Assert.Equal(expectedQuantity, actualQuantity)`，但這裡驗證的是「`Product.quantity` 有沒有跟自己的異動紀錄打架」，不是驗證業務邏輯正確性。

因為這些測試在 baseline 版本的目的就是「要重現出髒資料」，所以在沒有任何鎖的版本上，**測試通過＝成功重現弄髒現象**（也就是說一開始這些測試"預期會抓到問題"，而不是預期全綠）。等之後加了鎖機制，把同一套測試的斷言方向反過來（例如情境 A 從 `finalQuantity > 0` 改成 `finalQuantity == 0`），就變成迴歸測試，用來確保鎖機制真的生效、以後改壞會被抓到——同一套測試骨架，baseline 階段用來證明問題存在，加鎖後用來防止問題復發。

### CI 執行建議

這些測試耗時、且會製造大量並發連線與大量資料庫寫入，不建議跟一般 PR 檢查（快速的 unit test）放在同一個 pipeline stage，建議獨立排程執行或手動觸發。

---

## 跨分支重複使用測試

天真版（baseline）測完、確認弄髒現象都有穩定重現之後，後續每種併發控制機制都各自開一個獨立分支開發（分支策略見 [README](../README.md) 第 3 步）。每個分支底下，測試怎麼沿用整理如下：

**照抄不用改的部分**：

- 情境 A / B / C 的環境準備（建立全新商品）、併發發送邏輯（Barrier + `SemaphoreSlim` 同時放行）
- 共通分析方法：對帳、`balanceAfter` 序列檢查、throughput / latency 記錄
- `Inventory.ConcurrencyTests` 測試專案架構（`WebApplicationFactory` + `Testcontainers.PostgreSql`）

**每個分支一定要改的部分**：Assertion 方向反過來。天真版是「斷言弄髒了才算通過」，用來證明問題存在；修正分支要改成「斷言沒弄髒才算通過」——情境 A 從 `Assert.True(finalQuantity > 0)` 改成 `Assert.Equal(0, finalQuantity)`；情境 B 從 `finalQuantity < 0 || successCount > 100` 改成 `Assert.Equal(0, finalQuantity)` 且 `successCount == 100`；情境 C 的對帳斷言維持不變（對帳本來就該一直成立，天真版只是剛好也常常失敗）。

**視機制額外補充的部分**（不是每個分支都一樣，依機制特性加）：

| 機制 | 額外要斷言／記錄的指標 |
|---|---|
| 樂觀鎖（Version 比對） | 衝突回應（`409`）的次數與比例；client 端有沒有重試邏輯、重試後最終是否仍全部成功 |
| 悲觀鎖（`SELECT FOR UPDATE`） | 平均等待鎖的時間；p95/p99 latency 相較 baseline 上升幅度 |
| Serializable Isolation | 交易被中止（serialization failure）的次數；是否需要 client 端重試 |
| Redis 分散式鎖 | 鎖取得失敗／逾時次數；額外的 Redis 網路延遲對整體 latency 的影響 |
| Queue 序列化寫入 | 請求排隊等待的時間；佇列堆積長度；是否有請求逾時被丟棄 |

**最終產出**：每個分支測完後，把「情境 A/B/C 是否仍出現髒資料」+「throughput/latency 相較 baseline 的變化」+「機制專屬指標」彙整成一份總表，對照哪種機制在正確性與效能之間最符合這個練習想要的取捨。

---

## 測試執行順序建議

1. 先在「完全無併發控制」的 baseline 版本上，依序跑完情境 A → B → C，把每個情境的「測試後觀察重點」數據都記錄下來（截圖或存成報告都好，之後要拿來對比）。
2. 之後在後端逐步導入樂觀鎖（`Version` 欄位比對）、悲觀鎖（`SELECT ... FOR UPDATE`）、交易隔離等級調整、甚至 Redis 分散式鎖或 Queue 序列化寫入等機制，**每導入一種機制就重新跑一次同樣的 A / B / C 三個情境**，並比較：
   - 資料是否還會髒（正確性）
   - throughput / latency 相較 baseline 掉了多少（效能代價）
3. 最終產出一份「機制 × 正確性 × 效能」的對照表，這就是本練習的核心產出。
