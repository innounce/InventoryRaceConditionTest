# Serializable Isolation 實驗結論（feature/serializable-isolation）

## 實測數字

| Scenario | 結果 |
|---|---|
| A — 1000 req, Initial=1000 | 39 交易, version=39, qty=961 |
| B — 1000 req, Initial=100 | 47 交易, version=47, qty=53 |
| C — 5s/50 concurrency | 235 交易, version=235, qty=363=expected |

## 結論一：正確性完全保證
- `version == successCount` 依然成立（A：version=39；B：version=47）
- Scenario C 對帳：500 + (-137) = 363 == finalQuantity，完美吻合
- 被 abort 的交易（SQLSTATE 40001）完全不寫入任何資料

## 結論二：吞吐量介於樂觀鎖與悲觀鎖之間

| 機制 | Scenario A | Scenario B | Scenario C |
|---|---|---|---|
| 樂觀鎖 | 1 成功 | 19 成功 | 179 筆 |
| Serializable | 39 成功 | 47 成功 | 235 筆 |
| 悲觀鎖 | 1000 成功 | 100 成功 | 604 筆 |

- Serializable 優於樂觀鎖：PostgreSQL SSI 在 commit 時才偵測衝突，比應用層 version check 能多放行一些不真正衝突的請求
- Serializable 劣於悲觀鎖：衝突仍然導致 abort（不排隊），高競爭下 abort 率高

## 結論三：客戶端同樣需要 retry 邏輯
- 衝突回 409（SERIALIZATION_FAILURE），與樂觀鎖的 409 語義相同
- 伺服器不追蹤 abort 的請求，失敗即靜默丟棄
- 高競爭下 retry storm 風險與樂觀鎖相同

## 結論四：實作最簡單，不需要額外欄位或 raw SQL
- 不需要 `Version` ConcurrencyToken（不像樂觀鎖）
- 不需要 `SELECT ... FOR UPDATE` raw SQL（不像悲觀鎖）
- 只需在 `BeginTransactionAsync` 傳入 `IsolationLevel.Serializable`
- 衝突偵測完全由 PostgreSQL SSI（Serializable Snapshot Isolation）負責，能偵測更廣泛的讀寫衝突（包括 phantom read），不侷限於同一筆 row 的寫寫衝突

## 結論五：多 server 部署天然支援
- isolation level 設定在 DB connection 層，所有 API 實例共用同一 PostgreSQL，天然支援多 server
- 不需要 Redis 或 in-memory 鎖

## 結論六：環境注意事項
- 1000 個並發的 abort 請求在 Linux 下會快速消耗 inotify 實例（預設上限 128）
- 跑測試需要加 `DOTNET_USE_POLLING_FILE_WATCHER=true` 改用 polling 取代 inotify，或由系統管理員調高 `fs.inotify.max_user_instances`

## 總結
Serializable isolation 是三種機制裡實作最簡單的：不需要額外欄位、不需要 raw SQL，PostgreSQL 自動偵測並發讀寫衝突。代價與樂觀鎖類似——衝突導致 abort，客戶端需要 retry；吞吐量高於樂觀鎖但遠低於悲觀鎖。適合衝突率中等、可接受少量 retry、不想維護額外 lock 欄位的場景。
