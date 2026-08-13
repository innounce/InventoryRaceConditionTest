# 高併發庫存系統練習專案

一個使用 .NET + PostgreSQL 練習高併發處理的專案。

## 專案目標

先建一套「看起來正常」的庫存系統，再用大量併發小額扣庫存的 client 把它的併發漏洞打出來，藉此體驗 race condition、lost update、負庫存等問題，之後才逐步導入鎖機制修正。

## 文件索引

- [API 規格](docs/api-spec.md)：完整 request/response JSON、錯誤格式、狀態碼
- [資料庫 Schema](docs/db-schema.md)：`Product`、`InventoryTransaction` 兩張表的完整欄位定義
- [測試計畫](docs/test-plan.md)：三個測試情境的詳細步驟、預期結果、測試後觀察重點與判定標準，以及自動化測試腳本設計
- [操作手冊](docs/operations.md)：啟動 API、跑三種測試、刪測試 schema 的實際指令

---

## 第 1 步：基礎庫存進出系統設計

### 技術棧

- **後端**：ASP.NET Core Web API (.NET)
- **資料庫**：PostgreSQL
- **ORM**：EF Core（或 Dapper，若想更貼近底層 SQL 行為，方便之後觀察鎖與交易）
- **架構分層**：Controller → Service → Repository → DbContext（簡單三層即可，不用過度設計）

### 架構設計：前後端分離

- 後端**只提供 RESTful JSON API**，不含任何 View / Razor Page / 伺服器端渲染畫面。所有操作都是 `GET/POST/PUT/DELETE` + JSON body/response。
- 加上 **Swagger / OpenAPI**（`Swashbuckle.AspNetCore`），方便之後寫測試 client 時直接查規格，甚至能用 OpenAPI 文件自動產生 client 端的 request model，不用手刻 DTO。
- 「前端」在這個練習裡不是給人看的 UI，而是**你自己寫的 API 測試/模擬工具**（第 2 步的高併發 client）。它是一支完全獨立的專案（可以是 .NET Console App，或任何語言），只透過 HTTP 呼叫後端 API，兩邊沒有共用程式碼、沒有共用 DbContext，純粹用 API 溝通。
- API 回傳格式要固定且明確：
  - 成功：`200 OK` / `201 Created`，body 回傳異動後的 `Product`（含最新 `Quantity`、`Version`）
  - 業務錯誤（例如庫存不足）：`400 Bad Request`，body 帶錯誤訊息
  - 找不到資源：`404 Not Found`
  - 之後導入樂觀鎖後，版本衝突用 `409 Conflict`
  - 這些狀態碼是測試工具統計「成功率 / 錯誤率」的依據，所以一定要明確區分，不要全部丟 500。
- 因為呼叫方是程式（測試工具），不是瀏覽器，**不需要特別處理 CORS**；如果之後想額外做一個網頁儀表板來看即時庫存變化，才需要另外開放 CORS。

### 資料模型

系統有兩張表：`Product`（商品／庫存主體）與 `InventoryTransaction`（庫存異動紀錄，每次 stock-in / stock-out 都必須寫入一筆，之後拿來對帳、偵測資料錯亂）。

完整欄位定義見獨立文件：[資料庫 Schema](docs/db-schema.md)

### API 設計

- 基本 CRUD：`GET /products`、`GET /products/{id}`、`POST /products`、`PUT /products/{id}`、`DELETE /products/{id}`
- 庫存進出（核心，之後併發測試打的就是這兩支）：`POST /products/{id}/stock-in`、`POST /products/{id}/stock-out`
- 查詢／對帳（給測試工具跑完之後驗證用）：`GET /products/{id}/transactions`

完整的 request/response JSON 規格、錯誤格式、狀態碼定義見獨立文件：[API 規格](docs/api-spec.md)

### 第一版刻意的設計（重點）

- **先不要加任何併發控制**（不用 `SELECT ... FOR UPDATE`、不用 Transaction Isolation Level 調整，Version 欄位也不拿來做樂觀鎖檢查）。
- Service 層邏輯就是最直覺的寫法：讀出目前庫存 → 記憶體裡做加減 → 寫回去（同時把 Version + 1）→ 寫一筆異動紀錄。這種「read-modify-write」正是 race condition 的溫床。
- 唯一該有的業務規則：**庫存不可為負數**（但先不用鎖去保證它，只在應用層做個簡單 if 判斷，之後會證明這個判斷在高併發下會失效）。
- Version 欄位跟 InventoryTransaction 都要照樣寫入，**不是為了防止錯亂，而是為了事後拿來偵測錯亂**：Version 的最終值理論上應等於成功請求數，InventoryTransaction 的筆數與 BalanceAfter 序列理論上應該連續、不重複——實際跑起來後這兩者很可能都會出問題，這就是要觀察的現象。

這樣設計是為了讓第 2 步的破壞測試「有東西可以破壞」，作為之後導入鎖機制的 baseline 對照組。

---

## 第 2 步：高併發 Client 設計（弄髒系統）

### 目標

用大量併發、小額的 stock-out 請求打向系統，觀察在沒有並發控制的情況下會出現什麼髒資料現象：lost update、負庫存、Quantity 與交易紀錄對不上帳。

### Client 實作方式

這支 client 就是「前後端分離」架構裡獨立的一端，**跟後端專案完全分開、不共用程式碼**，單純透過 HTTP 呼叫 API，方便你自由替換測試工具或改寫測試邏輯而不動到後端。

建議直接寫一個獨立的 .NET Console App（跟主專案同生態，之後好維護，且方便解析 API 回傳的 JSON 去做統計），用：

- `HttpClient`（透過 `IHttpClientFactory`）
- `Task.WhenAll` 或 `Parallel.ForEachAsync` 發動大量併發請求
- 用 `SemaphoreSlim` 控制併發度，方便做不同併發量的對照測試
- 每個請求的結果（成功/失敗、狀態碼、回傳的 `Quantity`/`Version`）都記錄下來，測試跑完直接在 client 端統計，不用另外寫分析腳本

（如果想要更專業的壓測工具，也可以搭配 k6 / JMeter，但既然是練 .NET、且要客製化統計邏輯，自己寫 client 更貼近目的。）

### 測試情境設計

三個核心情境：Lost Update 驗證（初始庫存 1000，用 500～1000 併發扣 1）、負庫存驗證（初始庫存 100，用 1000 併發扣 1）、長時間混合壓力測試（stock-in / stock-out 交錯跑一段時間後對帳）。

每個情境的詳細測試步驟、預期結果、測試後觀察重點與判定標準，見獨立文件：[測試計畫](docs/test-plan.md)

---

## 第 3 步：修正併發問題

天真版（baseline）開發並測試完成、確認能穩定重現弄髒現象後，接著針對已知的併發控制機制逐一實作修正。

### 分支策略

- **天真版保留成獨立分支**（例如 `baseline/no-concurrency-control`），之後不再修改，永遠可以拿來重跑「弄髒」的示範，作為所有後續分支的對照組。
- **每種併發控制機制各自開一個獨立分支**，不要疊加在同一個分支上一直改，方便各自獨立開發、測試、記錄結果，也方便之後想單獨展示某種機制的效果：
  - `feature/optimistic-lock`：用 `Version` 欄位做樂觀鎖（比對版本，不符就回 `409`，由 client 決定要不要重試）
  - `feature/pessimistic-lock`：用 `SELECT ... FOR UPDATE` 鎖資料列
  - `feature/serializable-isolation`：改用 PostgreSQL Serializable isolation level，靠資料庫自動偵測衝突並中止/重試交易
  - `feature/distributed-lock`：引入 Redis 分散式鎖（若想額外練習跨多台 API instance 的情境）
  - `feature/queue-based-serialization`：改成寫入請求丟進 Queue，由單一 worker 序列化處理，徹底避免併發寫入同一列
- 每個分支測完後，把結果彙整成一份「機制 × 正確性 × 效能」比較表，這是本練習的最終產出。

### 測試怎麼沿用

三個測試情境、環境準備、併發發送邏輯、共通分析方法都原封不動沿用，不用每個分支重寫一份；每個分支主要差在 assertion 方向要反過來，並視機制補充專屬的觀察指標。詳細規則見獨立文件：[測試計畫 — 跨分支重複使用測試](docs/test-plan.md#跨分支重複使用測試)
