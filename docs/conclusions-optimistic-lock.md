# 樂觀鎖實驗結論（feature/optimistic-lock）

## 實測數字

| Scenario | 結果 |
|---|---|
| A — 1000 req, Initial=1000 | 1 交易, version=1, qty=999 |
| B — 1000 req, Initial=100 | 19 交易, version=19, qty=81 |
| C — 5s/50 concurrency | 179 交易, version=179, qty=325=expected |

## 結論一：正確性完全保證
- `version == successCount` 是鐵的物證（A：1 交易 version=1；B：19 交易 version=19）
- Scenario C 對帳：500 + (-175) = 325 == finalQuantity，完美吻合

## 結論二：吞吐量代價極大
- A：1000 req → 1 成功（0.1%）
- B：1000 req → 19 成功（1.9%）
- C：5s/50 concurrency → 179 筆成功交易
- 原因：同時讀到相同 Version，只有第一個拿到 PostgreSQL row lock 的能寫入，其餘 0 rows affected → 409

## 結論三：伺服器看不見損失，重試責任全在客戶端
- 伺服器回 409 後即忘記該請求，不追蹤失敗數量；CSV 只留成功記錄，失敗的 409 毫無蹤跡
- 重試負擔完全甩給客戶端——沒有 retry 邏輯，409 就是靜默丟單
- 高競爭場景下 retry 可能引發 retry storm：999 個客戶端同時重試 → 第二輪又只有 1 個成功 → 競爭越打越激烈，吞吐量不升反降

## 總結
樂觀鎖保證「寫入的東西一定是對的」，但它假設衝突是偶發的。一旦衝突變成常態，它只是把問題從「資料寫錯」轉移成「請求大量消失」——正確性買到了，可用性賠掉了。
