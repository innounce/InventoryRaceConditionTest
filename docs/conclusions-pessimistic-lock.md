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

## 結論四：代價是延遲與 lock queue 風險
- 每筆請求持有 row-level exclusive lock 直到 transaction commit，其他請求在 DB 等待
- 高並發下每筆請求的 P99 延遲會上升（因為等待鎖）
- 極端負載下 lock queue 可能積壓，引發 statement timeout 或連線耗盡

## 總結
悲觀鎖把「並發衝突」從應用層（409 → 客戶端 retry）下沉到 DB 層（row lock → 排隊等待），正確性與吞吐量都優於樂觀鎖，代價是每筆請求的平均延遲提高。適合高衝突、不能丟單的場景；若延遲敏感或鎖等待時間難以預測，則需考慮 queue-based serialization。
