# Docker & Redis 安裝紀錄

環境：Ubuntu 22.04 (Jammy)，架構：amd64

---

## 一、安裝 Docker

### 移除舊版本
```bash
sudo apt remove docker docker-engine docker.io containerd runc
```

### 安裝必要套件
```bash
sudo apt update
sudo apt install -y ca-certificates curl gnupg
```

### 新增 Docker 官方 GPG 金鑰
```bash
sudo install -m 0755 -d /etc/apt/keyrings

curl -fsSL https://download.docker.com/linux/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg

sudo chmod a+r /etc/apt/keyrings/docker.gpg
```

### 新增 Docker 套件庫
```bash
echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu jammy stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
```

### 安裝 Docker
```bash
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

### 免 sudo 使用 Docker
```bash
sudo usermod -aG docker $USER

newgrp docker
```

### 設定開機自動啟動
```bash
sudo systemctl enable docker
sudo systemctl start docker
```

### 驗證安裝
```bash
sudo docker run hello-world
```

---

## 二、安裝 Redis（Docker）

> 系統已有實體 Redis 佔用 6379，改用 6380。

### 啟動 Redis container
```bash
docker run -d --name redis -p 6380:6379 redis
```

### 設定重開機後自動啟動
```bash
docker update --restart always redis
```

### 驗證連線
```bash
redis-cli -h 127.0.0.1 -p 6380 ping
# 回應 PONG 代表成功
```

---

## 三、Redis container 常用指令

### 手動開關
```bash
docker stop redis      # 停止
docker start redis     # 啟動
docker restart redis   # 重啟
```

### 查看狀態
```bash
docker ps -a
```


### 查看 Log
```bash
docker logs redis                   # 查看全部 log
docker logs -f redis                # 即時追蹤
docker logs --tail 100 redis        # 只看最後 100 行
docker logs -f --tail 100 redis     # 即時追蹤最後 100 行
```

---

## 四、安裝 PostgreSQL（Docker）

> 系統已有實體 PostgreSQL 佔用 5432，改用 5433。
> PostgreSQL 18+ 掛載點須使用 `/var/lib/postgresql`（非 `/var/lib/postgresql/data`）。

### 建立資料目錄
```bash
mkdir -p ~/postgres-data
```

### 啟動 PostgreSQL container
```bash
docker run -d --name postgres -p 5433:5432 -e POSTGRES_PASSWORD=postgres -v ~/postgres-data:/var/lib/postgresql --restart always postgres
```

### 驗證連線
```bash
docker exec -it postgres psql -U postgres -c "\l"
```

成功列出資料庫清單代表完全啟動。

### 預設設定

| 項目 | 值 |
|------|-----|
| Port | 5433 |
| 使用者 | postgres |
| 密碼 | postgres |
| 資料目錄 | ~/postgres-data |

### 手動開關
```bash
docker stop postgres      # 停止
docker start postgres     # 啟動
docker restart postgres   # 重啟
```

### 查看 Log
```bash
docker logs postgres                   # 查看全部 log
docker logs -f postgres                # 即時追蹤
docker logs --tail 100 postgres        # 只看最後 100 行
docker logs -f --tail 100 postgres     # 即時追蹤最後 100 行
```
