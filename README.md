# Play Taiwan 後端（日遊所思夜遊所夢）

結合地圖解謎、任務、明信片收集與 AI 生成 Vlog 的智慧旅遊 App 後端。
2026 第 31 屆大專校院資訊應用服務創新競賽參賽作品。

- 前端：Flutter App（另一個 repo）
- 後端：本 repo，ASP.NET Core 8 Web API
- AI 服務：Python FastAPI（AI 組維護，網址在設定檔 `AiService:BaseUrl`）

---

## 目錄

- [功能](#功能)
- [系統架構](#系統架構)
- [專案結構](#專案結構)
- [快速開始](#快速開始)
- [設定檔說明](#設定檔說明)
- [測試與 CI](#測試與-ci)
- [API 文件](#api-文件)
- [商家模組說明](#商家模組說明)
- [相關文件](#相關文件)

---

## 功能

| 模組 | 說明 | 主要 API |
|---|---|---|
| 帳號 | 註冊（寄驗證信）、登入（JWT）、忘記密碼、修改名稱與密碼 | `/api/Auth/*` |
| 首頁 | 總覽卡片：已完成探索數、明信片、勳章、Vlog 等統計 | `/api/Home/Overview` |
| 劇本生成 | 現在揪出發（定位或指定縣市，一次生成 3 份劇本）、遊你說算（一句話生成）、AI Agent 情緒推薦 | `/api/Story/GenerateGameStory`、`/api/Story/GenerateByText`、`/api/Story/spin` |
| 劇本 | 劇本詳情、確認開始遊玩、結束劇本 | `/api/Story/*` |
| 解謎地圖 | 地圖節點、抵達打卡、導航、周邊推薦。迷霧由後端處理：未解鎖的站只回傳地點代號、景點照片的霧化版與大概位置，而且不能查看、導航或打卡 | `/api/Map/*` |
| 解謎地圖 | 地圖節點（未解鎖顯示剪影）、抵達打卡、導航、周邊推薦 | `/api/Map/*` |
| 景點剪影 | 用景點照片產生去背實心剪影，地圖上未解鎖的節點顯示剪影 | `/api/Silhouette/*` |
| NPC 語音 | 劇情前傳與任意文字轉語音 | `/api/Npc/*` |
| 任務 | 任務清單、答題、提示、隱藏關卡 | `/api/Task/*` |
| 交通規劃 | 等時圈可到達景點、多點路線規劃（步行／自行車／機車／汽車／公車／捷運）、公車即時到站 | `/api/Route/*`、`/api/Bus/*`、`/api/Metro/*` |
| 明信片 | AI 生成明信片、ibon 列印、分享 | `/api/PostcardCatalog/*` |
| 勳章 | 完成劇本後依劇本內容抽一枚勳章，夜間劇本才能抽「午夜台灣」系列 | `/api/Badge/*` |
| 過往旅途 | 通關紀錄與旅程內容 | `/api/History/*` |
| 遊客 Vlog | 依玩家走過的景點與照片，由 AI 產生旁白並合成影片 | `/api/VisitorVlog/*` |
| 商家 Vlog | 店家資訊與照片 → AI 旁白、推薦配文、TAG → 合成影片 | `/api/MerchantVlog/*` |
| 商家後台 | 商家註冊、優惠券、NFC 貼紙綁定與掃描、核銷、題庫 | `/api/merchant/*`、`/api/nfc/*`、`/api/coupons/*` |
| 上傳 | 任務照片、影片、錄音 | `/api/Upload` |

---

## 系統架構

```text
                 Flutter App
                      │  REST API（JWT Bearer）
                      ▼
┌──────────────────────────────────────────────┐
│ Controllers   接收請求、驗證參數               │
│ Services      商業邏輯                         │
│ DAO           資料存取（Dapper）               │
└──────────────────────────────────────────────┘
      │           │            │            │
      ▼           ▼            ▼            ▼
    MySQL       Neo4j       Valhalla      外部服務
  主要資料    商家景點     交通路網      ・AI 服務（劇本、Vlog、明信片、語音、景點圖譜）
             版本鏈       （Docker）    ・TDX（公車、捷運資料）
                                        ・SMTP（驗證信、重設密碼信）
                                        ・ibon 列印微服務
```

使用技術：ASP.NET Core 8、Dapper、MySQL 8、Neo4j、Valhalla、JWT、Swagger、xUnit、GitHub Actions

---

## 專案結構

```text
.
├── Controllers/        API 進入點，依功能分資料夾（Auth、Story、Map、Merchant…）
├── Services/           商業邏輯
├── dao/                資料存取
├── Models/             資料模型
├── ViewModels/         API 請求與回應格式
├── Middleware/         JWT 驗證、例外處理
├── Extensions/         擴充方法
<<<<<<< HEAD
├── util/               共用工具（設定、雜湊…）
├── Sqls/mysql/         MySQL 建表與初始資料
├── wwwroot/            靜態檔案（背景音樂、迷霧圖、上傳檔案）
=======
├── util/               共用工具（設定、雜湊、剪影影像處理…）
├── Sqls/mysql/         MySQL 建表與初始資料
├── wwwroot/            靜態檔案（背景音樂、剪影、上傳檔案）
>>>>>>> 25982c763398f1a26f9991595ecb8356a41295bc
├── tests/              單元測試與 API 整合測試
├── docs/               文件（API 清單、Valhalla 架設…）
├── .github/workflows/  CI 設定
├── appsettings.json    設定檔
└── Program.cs、Startup.cs
```

---

## 快速開始

### 1. 需要的環境

| 項目 | 說明 |
|---|---|
| .NET SDK 8.0.414 | 版本見 `global.json` |
| MySQL 8.0 | 主要資料庫 |
| Neo4j | 商家模組用。本機開發建議用 Neo4j Desktop |
| Docker Desktop + Valhalla | 交通路網。劇本生成、交通規劃會用到，架設步驟見 [docs/valhalla-setup.md](docs/valhalla-setup.md) |

### 2. 下載專案

```bash
git clone https://github.com/pojkhb/Play_Taiwan.git
cd Play_Taiwan
```

### 3. 建立 MySQL 資料庫

```bash
mysql -u root -p -e "CREATE DATABASE play_taiwan_db CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;"
mysql -u root -p play_taiwan_db < Sqls/mysql/schema.sql            # 所有資料表
<<<<<<< HEAD
mysql -u root -p play_taiwan_db < Sqls/mysql/seed_reference.sql    # 勳章、敘事語氣、任務類型、迷霧圖等基本資料
```

> 已經建好的資料庫如果還有 `silhouette`、`story_node_silhouette` 兩張表，執行一次 `Sqls/mysql/20260930_silhouette_to_fog.sql`，換成迷霧圖表 `fog`。

公車與捷運資料有兩種來源，擇一即可：

=======
mysql -u root -p play_taiwan_db < Sqls/mysql/seed_reference.sql    # 勳章、敘事語氣、任務類型等基本資料
```

公車與捷運資料有兩種來源，擇一即可：

>>>>>>> 25982c763398f1a26f9991595ecb8356a41295bc
- 直接匯入 `Sqls/mysql/BusData_Taichung.sql`（臺中市公車 + 全台台灣好行）與 `Sqls/mysql/MetroData.sql`（捷運）
- 設定 TDX 金鑰，由後端自動從 TDX 同步（見[設定檔說明](#設定檔說明)）

### 4. 設定 Neo4j（商家模組）

在 Neo4j Browser 執行：

```cypher
CREATE DATABASE playtaiwandb IF NOT EXISTS;
```

切換到 `playtaiwandb` 後建立約束：

```cypher
CREATE CONSTRAINT place_uid IF NOT EXISTS FOR (p:Place) REQUIRE p.uid IS UNIQUE;
CREATE CONSTRAINT version_id IF NOT EXISTS FOR (v:Version) REQUIRE v.version_id IS UNIQUE;
```

> Neo4j 社群版不能建立多個資料庫。用社群版時直接在預設的 `neo4j` 資料庫建立約束，並把設定檔的 `Neo4jSettings:Database` 改成 `neo4j`。

### 5. 設定本機密碼

在專案根目錄建立 `appsettings.Development.json`（已列入 `.gitignore`，不會被提交），只放要覆寫的值：

```json
{
  "AppSettings": {
    "mydb": "Server=localhost;Database=play_taiwan_db;User Id=root;Password=你的密碼;"
  },
  "Neo4jSettings": {
    "Password": "你的 Neo4j 密碼"
  }
}
```

### 6. 啟動

```bash
dotnet restore
dotnet run
```

後端固定聽 `5501` port，啟動後開啟 <http://localhost:5501/swagger> 就能看到並測試所有 API。

---

## 設定檔說明

`appsettings.json` 是共用設定，`appsettings.Development.json` 只放本機要覆寫的值。**密碼和金鑰請不要寫進 `appsettings.json` 再提交。**

| 設定 | 用途 | 必填 |
|---|---|---|
| `AppSettings:mydb` | MySQL 連線字串 | ✅ |
| `AppSettings:jwt_secret` | JWT 簽章金鑰 | ✅ |
| `AppSettings:expires` | JWT 有效時間（分鐘） | ✅ |
| `AppSettings:hash_key` | 密碼雜湊用的金鑰 | ✅ |
| `Neo4jSettings:*` | 本機 Neo4j 連線（Uri、User、Password、Database） | 商家模組需要 |
| `Neo4j:Mode` | `Local`：直連本機 Neo4j；`Remote`：改用 AI 服務的 `/api/neo4j/cypher` | |
| `AiService:BaseUrl` | AI 服務網址 | 劇本生成、Vlog、明信片、語音需要 |
| `Valhalla:BaseUrl` | Valhalla 網址，預設 `http://localhost:8002` | 交通功能需要 |
| `SmtpSettings:*` | 寄信用的 SMTP 設定 | 註冊驗證信、忘記密碼需要 |
| `AppSettings:tdx_client_id`、`tdx_client_secret` | TDX 交通資料金鑰 | 同步公車、捷運時需要 |
| `BusSync:*`、`MetroSync:*` | 啟動時是否在背景同步公車、捷運資料（`Enabled`） | |
| `IbonPrinterSettings:ApiUrl` | ibon 列印微服務，預設 `http://127.0.0.1:9000/upload` | 明信片列印需要 |
<<<<<<< HEAD
| `Fog:RadiusMeters` | 地圖迷霧範圍的半徑（公尺），預設 300 | |
=======
>>>>>>> 25982c763398f1a26f9991595ecb8356a41295bc

---

## 測試與 CI

```bash
dotnet test tests/TrafficSystem.Tests
```

- **單元測試**：不需要資料庫。
- **API 整合測試**：會在 MySQL 建立臨時資料庫 `play_taiwan_test_*`（用 `schema.sql` + `seed_reference.sql` 建立），測完自動刪除，不會動到開發資料。MySQL 連線優先使用環境變數 `TEST_MYSQL`，沒設定時使用 `appsettings.json` 的 `AppSettings:mydb`。
- AI 服務、ibon、地理編碼等外部請求在測試中都會被模擬，不會真的連線；寄信也不會真的寄出。

**CI**：每次 push 到 `main` 或開 Pull Request，GitHub Actions（[`.github/workflows/ci.yml`](.github/workflows/ci.yml)）會自動建置、在 MySQL 8.0 容器上執行全部測試，並產出測試結果與覆蓋率報告。

---

## API 文件

- **Swagger**：<http://localhost:5501/swagger>，每支 API 的參數與範例都在這裡。
- **API 總覽**：[docs/notion/API總覽.md](docs/notion/API總覽.md)，依 App 畫面整理每個畫面要接哪些 API，以及每支 API 目前的狀態。
- **API 清單**：[docs/notion/API清單.csv](docs/notion/API清單.csv)，可以直接匯入 Notion 當資料庫。

共用規則：

| 項目 | 說明 |
|---|---|
| 登入 | 先呼叫 `POST /api/Auth/Login` 取得 token，之後在標頭帶 `Authorization: Bearer <token>` |
| 回傳格式 | `{ "isSuccess": true, "message": "說明", "Result": 資料 }`，失敗時看 `message` |
| 欄位命名 | 跟 C# 屬性同名（例如 `story_id`、`isSuccess`），不會轉成小駝峰 |
<<<<<<< HEAD
| 圖片網址 | `/` 開頭的（例如上傳的檔案）是後端上的檔案，要接在伺服器網址後面；`http` 開頭的直接使用 |
=======
| 圖片網址 | `/` 開頭的是後端上的檔案，要接在伺服器網址後面；`http` 開頭的直接使用 |
>>>>>>> 25982c763398f1a26f9991595ecb8356a41295bc
| 很久才回應的 API | 劇本生成可能要好幾分鐘，前端請把逾時設長；Vlog 合成送出後用 Status 輪詢 |

---

## 商家模組說明

相關 Controller：`MerchantAccountController`、`MerchantPlaceController`、`MerchantCouponController`、`MerchantNfcController`、`NfcController`、`CouponRedeemController`、`MerchantQuestionController`。

**目前不驗證登入**：操作者由請求帶入的 `au_id`／`s_id` 決定（`Services/ICurrentActorProvider.cs`）。之後要接登入驗證，只要換一個實作。

**Neo4j 版本鏈**（`Services/Neo4j/PlaceVersionChainService.cs`）

- 政府開放資料的景點節點永遠不修改。
- 商家補充或修改的資訊寫在新的 `:Current` 版本節點，用 `[:HAS_VERSION]` 連回景點；再修改時，舊版本改標成 `:Historical`。
- 商家自己新建的景點會多一個 `:MerchantPlace` 標籤。刪除商家時只會刪掉這種自建景點，不會動到政府資料。

**已知限制**

- `store` 表沒有電話、網站欄位，`PUT /api/merchant/{sId}` 只會把名稱、地址、簡介同步到 Neo4j。
- 景點還沒有商家版本時，`GET /api/nfc/scan/{nfcUid}` 回傳的 `merchant_override` 是 `null`，前端請改顯示 `gov_name`、`gov_address`。
- 題目或商家被任務（`task.question_id`）引用時不能刪除，會回傳 409 與引用的 `task_id` 清單。
- MySQL 與 Neo4j 沒有共用交易：刪除商家時，MySQL 刪除成功後才清 Neo4j，Neo4j 失敗只記 log；註冊時如果 MySQL 寫入失敗，會把剛建立的 Neo4j 景點刪掉。

---

## 相關文件

| 文件 | 內容 |
|---|---|
| [docs/valhalla-setup.md](docs/valhalla-setup.md) | Valhalla（Docker）架設步驟 |
| [docs/notion/API總覽.md](docs/notion/API總覽.md) | API 總覽（依 App 畫面整理） |
| [wwwroot/bgm/README.md](wwwroot/bgm/README.md) | Vlog 背景音樂說明 |

---

維護者：[@pojkhb](https://github.com/pojkhb)
