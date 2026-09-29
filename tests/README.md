# 後端測試

測試專案在 `tests/TrafficSystem.Tests`，分兩種：

| 種類 | 工具 | 需要什麼 |
|---|---|---|
| 單元測試（`Badge/`、`Story/`、`Valhalla/`、`Vlog/`） | xUnit | 什麼都不用，clone 下來就能跑 |
| API 整合測試（`Api/`） | xUnit + WebApplicationFactory | 本機 MySQL 有開 |

## 執行

在 repo 根目錄：

```powershell
dotnet test tests/TrafficSystem.Tests
```

- 只跑單元測試：`dotnet test tests/TrafficSystem.Tests --filter "FullyQualifiedName!~Api"`
- 只跑 API 測試：`dotnet test tests/TrafficSystem.Tests --filter "FullyQualifiedName~Api"`
- 覆蓋率報告（coverlet 收集 + ReportGenerator 轉成網頁，打開 `TestResults/coverage/index.html`）：
  ```powershell
  dotnet tool restore
  dotnet test tests/TrafficSystem.Tests --results-directory TestResults --collect:"XPlat Code Coverage"
  dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:TestResults/coverage -reporttypes:Html
  ```
- VS Code：裝 C# Dev Kit 後，左邊的「測試」面板可以一個一個跑、看哪個失敗。

## API 整合測試怎麼運作

`Api/ApiFactory.cs` 會：
1. 在 MySQL 建一個全新的 `play_taiwan_test_xxxx` 資料庫，用 `Sqls/mysql/schema.sql`（資料表結構）和 `seed_reference.sql`（勳章、任務類型、敘事語氣）初始化
2. 把整個後端在記憶體裡啟動（不佔 port、不用先 `dotnet run`），測試直接打真的 API
3. 測完把測試資料庫刪掉，**不會動到開發用的 `play_taiwan_db`**

MySQL 連線：有設環境變數 `TEST_MYSQL` 就用它（CI 用），沒有就用 `appsettings.json` 的 `AppSettings:mydb`。

AI 服務、Valhalla、Neo4j 不會真的呼叫，所以只測不需要它們的 API 和輸入檢查。

**資料表有改時**要重新匯出 `schema.sql`，不然 API 測試用的是舊結構：

```powershell
& "C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe" -uroot -p --no-data --no-tablespaces --skip-comments --skip-dump-date play_taiwan_db > Sqls/mysql/schema.sql
```

## 目前有哪些測試

| 檔案 | 測什麼 |
|---|---|
| `Badge/BadgeRulesTests.cs` | 勳章類別規則：城市、地標（含名字很像但不算的）、台灣味、島嶼生靈、老台灣、午夜台灣 |
| `Story/StoryAlgorithmTests.cs` | 劇本挑景點：交通方式分組、景點去重、各圈層輪流抽、三組不重複、參觀順序 |
| `Valhalla/ValhallaServiceTests.cs` | 交通方式對應、等時圈內外判斷、路線形狀解碼 |
| `Vlog/VlogAiGatewayTests.cs` | VLOG 照片篩選、TAG 整理、AI 合成進度判斷、上傳檔案路徑安全 |
| `Vlog/VisitorVlogServiceTests.cs` | 遊客 VLOG 的 zip 照片命名、遊玩時長 |
| `Api/AuthApiTests.cs` | Swagger 文件、沒帶 / 過期 / 偽造 Token 回傳 401 |
| `Api/BadgeApiTests.cs` | 勳章圖鑑、開始遊玩 → 結束 → 抽勳章 → 重抽拿到同一枚、夜間劇本抽午夜台灣 |
| `Api/StoryMapApiTests.cs` | 劇本詳情、進行中劇本、地圖節點不重複、迷霧提示 |
| `Api/VlogApiTests.cs` | 敘事語氣選項、商家 VLOG 輸入檢查、沒玩過的劇本不能做遊客 VLOG |

## 新增測試

1. 單元測試放在對應模組的資料夾，API 測試放 `Api/` 並在類別加上 `[Collection(ApiCollection.Name)]`。
2. 單一情境用 `[Fact]`；同一個邏輯要測多組輸入用 `[Theory]` + `[InlineData(...)]`。
3. 測試方法用中文命名，寫清楚「在什麼情況下、應該怎樣」，例如 `夜間劇本可以抽整個午夜台灣類別`。
4. API 測試每個測試用 `_api.NewUserId()` 拿一個新玩家、`_api.CreateStoryAsync(...)` 建自己的劇本，不要依賴其他測試留下的資料。
5. 要測的函式如果是 `private`，改成 `internal` 就好（主專案已設定 `InternalsVisibleTo`），不要改成 `public`。

## CI

`.github/workflows/ci.yml`：每次 push 到 main 或開 PR，GitHub Actions 會開一個 MySQL 8.0 容器，
建置後執行全部測試，測試結果顯示在 Actions 頁面，覆蓋率報告可以在該次執行的 Artifacts 下載。
