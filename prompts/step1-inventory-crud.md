# 實作任務：第 1 步 — 基礎庫存進出系統

你要在這個 repo 裡實作一套 ASP.NET Core Web API + PostgreSQL 的庫存進出系統。這是一個練習高併發處理的專案的**第一階段**，目標是先做出一套「看起來正常」、但刻意不做任何併發控制的 baseline 版本，之後會拿它來做併發破壞測試（見第 2、3 步）。

## 開始前必讀

實作前請完整讀過以下文件，所有規格以這些文件為準，這份 prompt 只是任務摘要，不是完整規格：

- [../README.md](../README.md) — 專案目標、技術棧、架構設計（前後端分離）、第一版刻意的設計原則
- [../docs/db-schema.md](../docs/db-schema.md) — `Product`、`InventoryTransaction` 完整欄位定義與型別
- [../docs/api-spec.md](../docs/api-spec.md) — 所有 API 的 request/response JSON 規格、錯誤格式、狀態碼

## 這階段要做的事

1. 建立 ASP.NET Core Web API 專案，分層為 Controller → Service → Repository → DbContext（簡單三層即可）
2. 用 EF Core（或 Dapper）搭配 PostgreSQL，依 `db-schema.md` 建出 `Product`、`InventoryTransaction` 兩張表
3. 實作 `api-spec.md` 列出的所有端點：CRUD（`GET/POST/PUT/DELETE /products`）+ 庫存進出（`stock-in`/`stock-out`）+ 查詢對帳（`GET /products/{id}/transactions`）
4. 加上 Swagger / OpenAPI（`Swashbuckle.AspNetCore`）
5. 後端純 JSON API，不含任何 View / Razor Page，不用處理 CORS

## 這階段刻意不做的事（重要，不要自作主張加上去）

- **不要加任何併發控制**：不用 `SELECT ... FOR UPDATE`、不調整 Transaction Isolation Level，`Version` 欄位只負責每次異動遞增，不拿來做樂觀鎖比對或擋寫入
- 「庫存不可為負數」只用應用層簡單的 `if` 判斷，不用資料庫層級的 constraint 或鎖來保證它
- 不要做前端網頁 UI，也不用管 CORS
- Service 層維持最直覺的「讀出庫存 → 記憶體加減 → 寫回去（Version + 1）→ 寫一筆 InventoryTransaction」寫法，不要加額外的鎖、重試或防呆包裝——這些「漏洞」是刻意保留的，之後步驟需要靠它們重現問題

## 完成定義（Definition of Done）

- 所有端點行為與 `api-spec.md` 的 request/response 範例一致，包含錯誤格式與狀態碼
- 資料庫欄位、型別與 `db-schema.md` 一致
- 可以用 Swagger UI 手動測試每一支 API，涵蓋成功與錯誤情境（例如商品不存在、庫存不足）
