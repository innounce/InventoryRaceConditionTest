# 資料庫 Schema（PostgreSQL）

## Product（商品／庫存主體）

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | uuid, PK | 主鍵 |
| Sku | varchar(50), unique | 商品編號 |
| Name | varchar(200) | 商品名稱 |
| Quantity | integer | 目前庫存量 |
| Version | integer, default 0 | 每次異動都遞增，但第一版**不**拿它做樂觀鎖檢查（不比對、不擋寫入）。留著是為了事後驗證：遞增次數若少於實際成功請求數，就代表發生了 lost update |
| CreatedAt | timestamptz | 建立時間 |
| UpdatedAt | timestamptz | 最後更新時間 |

## InventoryTransaction（庫存異動紀錄）

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | uuid, PK | 主鍵 |
| ProductId | uuid, FK → Product.Id | 關聯商品 |
| ChangeType | varchar(3) | `IN` / `OUT` |
| Quantity | integer | 異動數量 |
| BalanceAfter | integer | 異動後餘額（用來事後對帳） |
| CreatedAt | timestamptz | 時間戳 |

> 這張紀錄表很關鍵：之後驗證系統有沒有被「弄髒」，就是拿 `Product.Quantity` 去跟 `SUM(InventoryTransaction.Quantity)` 對帳，兩者對不上就代表資料不一致。每次 stock-in / stock-out 都必須寫入一筆紀錄，不能省略，否則之後無法對帳。

對應的 API 用法見 [API 規格](api-spec.md)。
