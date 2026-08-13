# 實作任務：第 2 步 — 高併發測試 Client（弄髒系統）

你要實作一支獨立的 .NET Console App，用來對第 1 步做好的庫存 API 發動大量併發請求，驗證並重現系統在沒有任何併發控制時會出現的髒資料現象。

## 開始前必讀

- [../README.md](../README.md) — 第 2 步：高併發 Client 設計、架構設計：前後端分離
- [../docs/api-spec.md](../docs/api-spec.md) — API 規格，client 要照這個規格打請求、解析回應
- [../docs/test-plan.md](../docs/test-plan.md) — 三個測試情境（A/B/C）的詳細測試步驟、預期結果、測試後觀察重點、判定標準，這是這支 client 要能重現與驗證的目標

## 這階段要做的事

1. 建立獨立的 .NET Console App 專案，跟後端專案完全分開、不共用程式碼，只透過 HTTP 呼叫後端 API
2. 用 `HttpClient`（`IHttpClientFactory`）+ `Task.WhenAll` / `Parallel.ForEachAsync` + `SemaphoreSlim` 控制併發度
3. 用共用的 `TaskCompletionSource` 當非同步門閂讓所有請求真正同時起跑,最大化 race window——**不要用 `Barrier`/`ManualResetEventSlim`**,這類會阻塞執行緒的同步機制在幾百到上千併發下會撞上執行緒集區成長速度限制,實測「同時放行」會拖成幾十秒(見 test-plan.md「共通測試環境準備」)
4. 記錄每個請求的送出時間、回應狀態碼、回應內容中的 `quantity`/`version`、耗時
5. 依 test-plan.md 實作情境 A、B、C 三種測試模式，可用命令列參數切換要跑哪個情境
6. 測試跑完後，依 test-plan.md「共通分析方法」自動統計並印出結果：成功/失敗數、對帳結果、`balanceAfter` 序列是否異常、throughput/latency

## 完成定義

- 三個情境都能各自獨立執行，且執行結果（是否弄髒、弄髒的具體數據）符合 test-plan.md 描述的「測試後觀察重點」與「判定標準」
- 輸出的統計報告要包含 test-plan.md 列出的所有共通分析項目
