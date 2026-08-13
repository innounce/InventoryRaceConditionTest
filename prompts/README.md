# Prompts 資料夾

這裡放的不是給人看的設計文件（那些在 [`../docs/`](../docs/) 和 [`../README.md`](../README.md)），而是**日後要開新的對話請 AI 動手實作時，直接整份貼給它的 prompt**。每份 prompt 只給任務摘要與邊界，完整規格一律指向 `docs/` 底下的文件，避免內容重複、之後改規格要兩邊同步。

用法：想開始實作哪個階段，就把對應檔案的內容整份貼給 AI（或用 `@` 引用檔案路徑），讓它先讀過連結的規格文件再動手。

## 檔案索引

- [step1-inventory-crud.md](step1-inventory-crud.md)：實作基礎庫存進出系統 + CRUD（baseline，不含任何併發控制）
- [step2-load-test-client.md](step2-load-test-client.md)：實作高併發測試 Client（弄髒系統的手動測試工具）
- [concurrency-test-project.md](concurrency-test-project.md)：實作 xUnit 自動化併發測試專案
- [step3-concurrency-fixes.md](step3-concurrency-fixes.md)：在指定分支實作某一種併發控制機制的修正

建議照順序執行：`step1` → `step2`（或 `concurrency-test-project`，兩者互相獨立可交換順序）→ 確認 baseline 弄髒現象重現後 → `step3`（每個機制各自開分支各跑一次）。
