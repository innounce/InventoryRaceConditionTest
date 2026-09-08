# Docker 容器化 Q&A

本文整理容器化過程中的常見問題與解答。

---

## Dockerfile 與 docker-compose.yml 的差別？

**Dockerfile** — 定義「怎麼打包一個 image」，像食譜，描述如何把程式碼變成可執行的封裝包。

**docker-compose.yml** — 定義「跑哪些 container、怎麼組合在一起」，像劇本，說明要啟動哪些服務、port 怎麼對應、誰依賴誰。

- `image: postgres:18` → 直接用 Docker Hub 現成 image，不需要 Dockerfile
- `build: .` → 用你自己寫的 Dockerfile 先 build 出 image，再跑起來

---

## Docker image 打包出來的是跨平台執行實體嗎？

是。Dockerfile 把 OS 層、runtime、編譯好的 dll、所有相依套件全部封進去，任何有 Docker 的機器行為完全一致。

唯一底線是 **CPU 架構**（amd64 vs arm64）——在 amd64 打包的 image，在 Apple Silicon（arm64）跑會有相容性問題。現在大多數官方 image 會同時發布兩種架構版本。

---

## PostgreSQL 持久化跟 image 有關係嗎？

無關。image 只是模板，持久化是「跑起來時」決定的事：

```yaml
# 不掛 volume → container 刪掉資料就消失
postgres:
  image: postgres:18

# 掛 volume → 資料活在 host，container 怎麼死都還在
postgres:
  image: postgres:18
  volumes:
    - ~/postgres-data:/var/lib/postgresql
```

同一個 image，掛不掛 volume 在 docker-compose.yml 決定，image 本身不在乎。

---

## PostgreSQL 18+ 的掛載路徑跟舊版不同？

是。掛載點因版本不同：

| 版本 | 容器內資料目錄 | Volume 寫法 |
|------|-------------|------------|
| PostgreSQL 17 以前 | `/var/lib/postgresql/data` | `-v ./data:/var/lib/postgresql/data` |
| PostgreSQL 18+ | `/var/lib/postgresql` | `-v ./data:/var/lib/postgresql` |

寫錯路徑，PostgreSQL 會用容器內部暫存空間，container 一刪資料全消失。

---

## self-contained 與 framework-dependent build 的差別？

| | self-contained | framework-dependent |
|---|---|---|
| .NET runtime | 打進 image | 由 base image 提供 |
| image 大小 | 較大（~200MB）| 較小（~100MB）|
| 目標需要 | 只要 Docker | 只要 Docker |

**在 k8s 的差異：** framework-dependent 的 `aspnet:9.0` base layer 在同一節點上只需 pull 一次，多個服務共用。self-contained 每個 image 都打了完整 runtime，節點擴容時重複傳輸相同內容，浪費頻寬與磁碟。

---

## `docker build -t inventory-api:latest .` 與 `docker compose build api` 的差別？

| | `docker build` | `docker compose build` |
|---|---|---|
| image 命名 | 自己用 `-t` 指定 | compose 自動產生 |
| 設定來源 | 自己指定 Dockerfile 路徑 | 從 docker-compose.yml 讀 |
| 適合情境 | 單獨打包、推 Docker Hub、匯出 tar.gz | 開發環境快速 rebuild |

**注意：** `docker-compose.deploy.yml` 用 `image: inventory-api:latest`，需要用 `docker build -t inventory-api:latest .` 確保名稱一致；若用 `docker compose build`，compose 自動產生的名稱不符，部署 compose 會找不到 image。

---

## image 如何轉移到其他機器？

**方式一：Docker Hub**
```bash
docker build -t your-username/inventory-api:latest .
docker push your-username/inventory-api:latest
# 目標機器
docker pull your-username/inventory-api:latest
```

**方式二：docker save / load（離線）**
```bash
docker save inventory-api:latest | gzip > inventory-api.tar.gz
# 傳過去後
docker load < inventory-api.tar.gz
```

**方式三：GitHub Container Registry（ghcr.io）**
```bash
docker push ghcr.io/innounce/inventory-api:latest
```

---

## 高並發測試已包含多 AP 條件了嗎？

部分是。精確區分：

- **DB 層機制**（樂觀鎖、悲觀鎖、Serializable）：競態發生在 DB，DB 是仲裁者，1 個 AP 還是 10 個 AP 行為一樣，現有測試已涵蓋。
- **Redis 分散式鎖**：Redis 是共享協調者，同上，現有測試已涵蓋。
- **Queue 序列化**：`Channel<T>` 住在 process 記憶體，現有測試 1000 個請求全進同一個 Channel，所以序列化成功。**多 AP 才會暴露失效**——每個 AP 各自有 Channel，跨 AP 沒有協調。

---

## 三個元件是微服務嗎？

| 元件 | 是否微服務 | 原因 |
|------|----------|------|
| Inventory.Api | ✓ 是 | 獨立部署、提供 HTTP 服務、有自己的資料庫 |
| Inventory.LoadTestClient | ✗ 不是 | Console 工具，只送請求不接請求，不需部署 |
| Inventory.ConcurrencyTests | ✗ 不是 | 測試套件，執行完就結束，沒有 HTTP 端點 |

微服務定義是「獨立部署、透過網路互相溝通的服務」。LoadTestClient 和 ConcurrencyTests 是開發工具，不是服務。

---

## xUnit 測試可以發布成執行檔部署嗎？

技術上可以：

```bash
dotnet publish tests/Inventory.ConcurrencyTests -r win-x64 --self-contained -o ./publish/tests-win
dotnet test ./publish/tests-win/Inventory.ConcurrencyTests.dll
```

但這個專案有根本限制：ConcurrencyTests 用 `WebApplicationFactory` 在 process 內起整個 API，執行時需要 Docker containers（PostgreSQL + Redis）在跑。部署到目標機器後等同需要完整開發環境。

| 用途 | 可行性 |
|------|-------|
| 在開發機跑 | ✓ 最自然 |
| 帶到有 Docker 的機器跑 | ✓ 可以 |
| 帶到生產機器跑 | ✗ 不合理 |

xUnit 的定位是**開發與 CI/CD 流程**，不是部署到外部機器執行的工具。
