# 操作手冊

實際跑起來要用的指令集合(建 DB、啟動服務、跑測試、清理)。設計規格見 [README](../README.md)、[API 規格](api-spec.md)、[測試計畫](test-plan.md)。

以下指令預設在 repo 根目錄(`inventory/`)下執行。

## 前置準備(只需做一次)

```bash
sudo -u postgres psql -c "CREATE ROLE inventory_app LOGIN PASSWORD '你自訂的密碼';"
sudo -u postgres psql -c "CREATE DATABASE inventory_dev OWNER inventory_app;"

cd src/Inventory.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=inventory_dev;Username=inventory_app;Password=你剛設的密碼;Maximum Pool Size=60"
dotnet ef database update
cd ../..
```

> `Maximum Pool Size=60` 是刻意設定的,不要拿掉——沒設上限的話,情境 A/B 打 1000 併發會把 PostgreSQL 預設 `max_connections=100` 打爆,炸出一堆跟 lost update 無關的 500 錯誤,把測試訊號弄髒。

## 1. 啟動 API 服務

```bash
dotnet run --project src/Inventory.Api --urls http://localhost:5279
```

- Swagger UI:`http://localhost:5279/swagger`
- 背景執行(不佔用終端機):

  ```bash
  dotnet run --project src/Inventory.Api --urls http://localhost:5279 > /tmp/inventory-api.log 2>&1 &
  ```

- 停止背景執行的服務:

  ```bash
  pkill -f "Inventory.Api"
  ```

## 2. 觸發 3 種測試

有兩種跑法:**手動 Console App**(自己看報告、可以慢慢觀察)跟**自動化 xUnit**(斷言判定過不過、適合重複執行)。API 服務要先啟動(見上一步)。

### 手動:Inventory.LoadTestClient

```bash
dotnet run --project src/Inventory.LoadTestClient -- --scenario A --base-url http://localhost:5279
dotnet run --project src/Inventory.LoadTestClient -- --scenario B --base-url http://localhost:5279
dotnet run --project src/Inventory.LoadTestClient -- --scenario C --base-url http://localhost:5279
```

- 情境 A、B 是瞬間爆量,幾秒內結束
- 情境 C 會連續跑 60 秒,指令不會馬上回來

### 自動化:Inventory.ConcurrencyTests

```bash
# 三個情境一次跑完
dotnet test tests/Inventory.ConcurrencyTests

# 只跑其中一個情境
dotnet test tests/Inventory.ConcurrencyTests --filter "FullyQualifiedName~ScenarioATests"
dotnet test tests/Inventory.ConcurrencyTests --filter "FullyQualifiedName~ScenarioBTests"
dotnet test tests/Inventory.ConcurrencyTests --filter "FullyQualifiedName~ScenarioCTests"

# 想看詳細的 SQL / HTTP log
dotnet test tests/Inventory.ConcurrencyTests --logger "console;verbosity=detailed"
```

這個測試專案**不需要**先手動啟動 API——`WebApplicationFactory` 會自己用 in-process 方式啟動一份。但 PostgreSQL 本身要有在跑(見前置準備)。

## 3. 刪除測試 schema

`Inventory.ConcurrencyTests` 每次執行都會留下一個 `test_<時間戳>_<亂數>` schema(刻意不砍,方便事後人工查資料,見 [測試計畫](test-plan.md))。跑久了要清掉時:

```bash
# 列出目前所有測試 schema
PGPASSWORD=你的密碼 psql -h localhost -p 5432 -U inventory_app -d inventory_dev \
  -c "SELECT nspname FROM pg_namespace WHERE nspname LIKE 'test_%' ORDER BY nspname;"

# 刪除單一 schema(把 <schema_name> 換成實際名稱)
PGPASSWORD=你的密碼 psql -h localhost -p 5432 -U inventory_app -d inventory_dev \
  -c "DROP SCHEMA \"<schema_name>\" CASCADE;"

# 一次刪掉全部測試 schema
PGPASSWORD=你的密碼 psql -h localhost -p 5432 -U inventory_app -d inventory_dev -c "
DO \$\$
DECLARE
  r RECORD;
BEGIN
  FOR r IN SELECT nspname FROM pg_namespace WHERE nspname LIKE 'test_%' LOOP
    EXECUTE format('DROP SCHEMA %I CASCADE', r.nspname);
  END LOOP;
END \$\$;
"
```

`public` schema(API 正式在用的那份 `Product`/`InventoryTransaction`)不會被上面的指令動到,因為篩選條件只抓 `test_` 開頭的 schema。
