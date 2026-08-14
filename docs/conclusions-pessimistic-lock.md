# 悲觀鎖實驗結論（feature/pessimistic-lock）

## 實測數字

| Scenario | 結果 |
|---|---|
| A — 1000 req, Initial=1000 | 1000 交易, version=1000, qty=0 |
| B — 1000 req, Initial=100 | 100 交易, version=100, qty=0 |
| C — 5s/50 concurrency | 604 交易, version=604, qty=79=expected |

## 結論一：正確性完全保證
- `version == successCount` 依然成立（A：version=1000；B：version=100）
- Scenario A：1000 筆請求全部成功，庫存精確歸零（1000 - 1000 = 0）
- Scenario B：恰好 100 筆成功，耗盡庫存後剩餘 900 筆收到 400（庫存不足），庫存精確歸零
- Scenario C 對帳：500 + (-421) = 79 == finalQuantity，完美吻合

## 結論二：吞吐量顯著優於樂觀鎖
- A：1000 req → 1000 成功（100%），樂觀鎖同場景只有 1 筆（0.1%）
- B：1000 req → 100 成功（恰好耗盡庫存），樂觀鎖同場景只有 19 筆
- C：5s/50 concurrency → 604 筆，樂觀鎖同場景只有 179 筆（3.4 倍吞吐量）
- 原因：SELECT ... FOR UPDATE 讓並發請求在 PostgreSQL row lock 排隊，不是失敗後回 409，每筆請求都能拿到鎖並成功寫入（或因庫存不足收到 400，但這是業務邏輯正確拒絕）

## 結論三：客戶端不需要 retry 邏輯
- 失敗只有兩種：庫存不足（400，業務邏輯正確）或連線錯誤（500，環境問題）
- 不存在「操作衝突請重試」的情況，伺服器自行在 DB 層序列化

## 結論四：高並發下連線池與無關請求會受牽連
等鎖期間 DB 連線持續被佔住，三個因素形成惡性循環：

```
request 量增加
    ↓
等鎖時間拉長（lock queue 積壓）
    ↓
每筆請求持有連線的時間增加
    ↓
連線池更快被耗盡
    ↓
無關的 GET /products 等查詢也拿不到連線 → 500
    ↓
整體 API 響應惡化，用戶重試，request 量更大
    ↓
惡化加劇
```

調高連線池上限無法根本解決問題，只是延後發生——DB 本身的 `max_connections` 終究是天花板。

## 結論五：多 server 部署的相容性

`SELECT ... FOR UPDATE` 的鎖存在於 PostgreSQL，所有 API 實例共用，天然支援多 server 部署。

若改用應用層鎖，相容性因實作方式而異：

| 做法 | 多 server 有效？ |
|---|---|
| DB 層 FOR UPDATE（本分支） | ✅ 有效，鎖在 DB，所有 server 共用 |
| 應用層 in-memory（SemaphoreSlim 等） | ❌ 無效，鎖只活在單一 process，其他 server 不知道 |
| 應用層 Redis distributed lock | ✅ 有效，鎖在 Redis，所有 server 共用 |

in-memory lock 在單一 server 上完全正確，但只要水平擴展到多台，race condition 立刻復活——每台 server 的記憶體獨立，彼此之間沒有協調。

## 總結
悲觀鎖把「並發衝突」從應用層（409 → 客戶端 retry）下沉到 DB 層（row lock → 排隊等待），正確性與吞吐量都優於樂觀鎖，多 server 部署也天然支援。核心代價是：等鎖期間 DB 連線被綁住，高並發下連線池會成為全站瓶頸，讓原本無關的請求一起受害。
