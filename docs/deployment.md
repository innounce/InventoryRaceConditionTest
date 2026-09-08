# 部署指南

## Inventory.Api — 容器化部署

### 開發機（有原始碼，含即時 build）

```bash
cp .env.example .env
docker compose up -d        # 啟動 postgres + redis + api
# API: http://localhost:5279/swagger
```

切換分支後重新 build：

```bash
git checkout feature/distributed-lock-retry-5s

# 方式一：透過 compose（image 名稱由 compose 管理）
docker compose build api && docker compose up -d api

# 方式二：手動指定名稱（部署用，確保名稱為 inventory-api:latest）
docker build -t inventory-api:latest .
docker compose up -d api
```

---

### 目標機器（無原始碼，只需 Docker）

**開發機：打包 image**

```bash
docker build -t inventory-api:latest .
docker save inventory-api:latest | gzip > inventory-api.tar.gz
# 將 inventory-api.tar.gz、docker-compose.deploy.yml、.env.example 傳到目標機器
```

**目標機器：載入並啟動**

```bash
docker load < inventory-api.tar.gz
cp .env.example .env
docker compose -f docker-compose.deploy.yml up -d
# API: http://localhost:5279/swagger
```

---

## Inventory.LoadTestClient — 打包與執行

LoadTestClient 需打包成獨立執行檔，目標機器不需要安裝 .NET SDK。

**打包（開發機）：**

```bash
# Windows
dotnet publish src/Inventory.LoadTestClient -r win-x64 --self-contained -o ./publish/loadtest-win

# Linux
dotnet publish src/Inventory.LoadTestClient -r linux-x64 --self-contained -o ./publish/loadtest-linux
```

**執行（目標機器）：**

```bash
# Windows
.\publish\loadtest-win\Inventory.LoadTestClient.exe --scenario A --base-url http://localhost:5279
.\publish\loadtest-win\Inventory.LoadTestClient.exe --scenario B --base-url http://localhost:5279
.\publish\loadtest-win\Inventory.LoadTestClient.exe --scenario C --base-url http://localhost:5279

# Linux
./publish/loadtest-linux/Inventory.LoadTestClient --scenario ALL --base-url http://localhost:5279
```

---

## Inventory.ConcurrencyTests — 執行環境需求

xUnit 測試僅適合開發機與 CI/CD，不建議部署到目標機器。需要：
- .NET 9 SDK
- 原始碼
- Docker containers（PostgreSQL + Redis）運行中

```bash
dotnet test tests/Inventory.ConcurrencyTests
```

---

## docker-compose 檔案說明

| 檔案 | 用途 | 指令 |
|------|------|------|
| `docker-compose.yml` | 開發機，含 `build: .`，可即時 rebuild | `docker compose up -d` |
| `docker-compose.deploy.yml` | 目標機器，使用 `image: inventory-api:latest` | `docker compose -f docker-compose.deploy.yml up -d` |
