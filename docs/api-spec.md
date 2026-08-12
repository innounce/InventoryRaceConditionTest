# API 規格

後端只提供 RESTful JSON API（詳見 [README](../README.md) 的「架構設計：前後端分離」）。這份文件是給測試 client 開發時查閱的完整規格。

## 端點總覽

**基本 CRUD**

- `GET /products`、`GET /products/{id}`
- `POST /products`
- `PUT /products/{id}`
- `DELETE /products/{id}`

**庫存進出（核心，之後併發測試打的就是這兩支）**

- `POST /products/{id}/stock-in`：增加庫存 + 寫一筆 IN 紀錄
- `POST /products/{id}/stock-out`：扣減庫存 + 寫一筆 OUT 紀錄

**查詢／對帳（給測試工具跑完之後驗證用）**

- `GET /products/{id}/transactions`：查詢該商品的所有異動紀錄

## 共通原則

- 時間欄位一律用 ISO 8601 UTC，例如 `2026-08-12T08:30:00Z`
- 錯誤回應統一格式：
  ```json
  {
    "error": "INSUFFICIENT_STOCK",
    "message": "庫存不足，目前庫存為 10，無法扣除 15"
  }
  ```
  `error` 是機器可判讀的錯誤代碼（測試工具用它分類統計），`message` 是人看的說明。

## Product 資源物件

GET / POST / PUT 共用的回傳格式（欄位定義見 [資料庫 Schema](db-schema.md)）：

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sku": "SKU-0001",
  "name": "無線滑鼠",
  "quantity": 100,
  "version": 3,
  "createdAt": "2026-08-12T02:00:00Z",
  "updatedAt": "2026-08-12T08:15:00Z"
}
```

## 端點細節

### `GET /products`

Response `200`：Product 物件陣列。

### `GET /products/{id}`

Response `200`：單一 Product 物件。
Response `404`：`{ "error": "PRODUCT_NOT_FOUND", "message": "找不到商品 {id}" }`

### `POST /products`

Request：

```json
{
  "sku": "SKU-0001",
  "name": "無線滑鼠",
  "initialQuantity": 100
}
```

Response `201`：Product 物件（`quantity` = `initialQuantity`，`version` = `0`）

### `PUT /products/{id}`

Request（只能改 `sku` / `name`，**不可直接改 `quantity`**——庫存只能透過 stock-in / stock-out 異動，確保每次異動都留下可稽核的 InventoryTransaction）：

```json
{
  "sku": "SKU-0001",
  "name": "無線滑鼠（改款）"
}
```

Response `200`：更新後的 Product 物件
Response `404`：同上

### `DELETE /products/{id}`

Response `204`：無內容
Response `404`：同上

### `POST /products/{id}/stock-in`

Request：

```json
{ "quantity": 50 }
```

Response `200`：

```json
{
  "product": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "sku": "SKU-0001",
    "name": "無線滑鼠",
    "quantity": 150,
    "version": 4,
    "createdAt": "2026-08-12T02:00:00Z",
    "updatedAt": "2026-08-12T08:20:00Z"
  },
  "transaction": {
    "id": "6b1f...",
    "productId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "changeType": "IN",
    "quantity": 50,
    "balanceAfter": 150,
    "createdAt": "2026-08-12T08:20:00Z"
  }
}
```

Response `400`：`{ "error": "INVALID_QUANTITY", "message": "quantity 必須大於 0" }`

### `POST /products/{id}/stock-out`

Request：

```json
{ "quantity": 1 }
```

Response `200`：格式同 stock-in，`transaction.changeType` = `"OUT"`
Response `400`（庫存不足）：`{ "error": "INSUFFICIENT_STOCK", "message": "庫存不足，目前庫存為 0，無法扣除 1" }`

### `GET /products/{id}/transactions`

Response `200`：

```json
[
  { "id": "...", "changeType": "OUT", "quantity": 1, "balanceAfter": 99, "createdAt": "2026-08-12T08:21:01Z" },
  { "id": "...", "changeType": "OUT", "quantity": 1, "balanceAfter": 100, "createdAt": "2026-08-12T08:21:00Z" }
]
```

> 這支 API 是第 2 步驗證的關鍵：測試工具／對帳腳本把回傳的 `balanceAfter` 依 `createdAt` 排序後檢查是否連續遞減/遞增、有沒有重複值——重複就代表兩個併發請求讀到同一個舊庫存值去計算，藉此偵測 lost update。

## 狀態碼一覽

| 狀態碼 | 情境 |
|---|---|
| 200 | 查詢成功 / stock-in / stock-out 成功 |
| 201 | 建立商品成功 |
| 204 | 刪除成功 |
| 400 | 業務錯誤（庫存不足、quantity 不合法等） |
| 404 | 商品不存在 |
| 409 | （之後導入樂觀鎖後）Version 衝突 |

這些狀態碼是測試工具統計「成功率 / 錯誤率」的依據，所以後端一定要明確區分，不要全部丟 500。
