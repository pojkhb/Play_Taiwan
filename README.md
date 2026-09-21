🇹🇼 Play Taiwan
結合地圖探索、任務系統、明信片收集與 AI 生成 Vlog 的智慧旅遊遊戲化後端







📖 專案簡介
Play Taiwan 是一套以 ASP.NET Core 打造的智慧旅遊遊戲化系統後端。玩家可以在地圖上探索景點與店家、完成系統派發的任務、拍照驗證任務成果、收集城市明信片、累積徽章，並透過 AI 服務自動生成旅程 Vlog，讓「玩台灣」變成一場可累積、可分享的收集型冒險。

專案採用 Controller / Service / DAO 分層架構，資料儲存以 MySQL 為主、Neo4j 圖形資料庫處理地點與路線關聯，並以 JWT 驗證保護 API。

✨ 核心功能
模組	說明
🔐 帳號驗證	JWT 登入 / 註冊，Middleware 攔截驗證 Token
🗺️ 地圖探索	景點、店家（Merchant）地圖資訊查詢
🎯 任務系統	任務生成（Task Generation）、任務提示（Hint）、任務成果驗證（Verification）
🏅 徽章成就	依任務完成度發放 Badge
📮 明信片收藏	明信片圖鑑（Catalog）與個人收集紀錄
🖼️ 剪影猜景點	以景點剪影作為互動猜謎小遊戲
📜 故事劇情	Story 模組提供地區文化 / 歷史敘事內容
🎬 AI Vlog 生成	透過 AI 服務將旅程紀錄自動剪輯成 Vlog
🕒 歷史紀錄	使用者操作 / 任務歷程查詢
📤 檔案上傳	任務驗證照片、明信片素材等檔案上傳
✉️ 通知信件	Email 服務發送系統通知
🏗️ 技術架構
text
Client (App / Web)
      │  REST API (JWT Bearer)
      ▼
┌─────────────────────────────┐
│   Controllers                │  ← 接收請求、參數驗證
├─────────────────────────────┤
│   Services                   │  ← 商業邏輯（任務生成/驗證、AI Vlog、Neo4j 查詢…）
├─────────────────────────────┤
│   DAO                        │  ← 資料存取邏輯
├─────────────────────────────┤
│   MySQL          Neo4j       │  ← 關聯式資料      圖形資料（景點/路線關聯）
└─────────────────────────────┘
🧰 技術棧
後端框架：ASP.NET Core（C#）

資料庫：MySQL、Neo4j

驗證機制：JWT + Middleware

API 文件：Swagger

其他整合：Email 通知服務、AI Vlog 生成服務

📁 專案結構
text
Play_Taiwan/
├── Controllers/        # API 進入點（Auth, Map, Task, Postcard, Badge, Story, Silhouette…）
├── Services/            # 商業邏輯層（含 AI Vlog、Neo4j、任務生成/驗證服務）
├── dao/                 # 資料存取層
├── Models/              # 資料模型
├── ViewModels/          # 前後端傳輸用視圖模型
├── Middleware/           # JWT 驗證等中介層
├── Extensions/           # 擴充方法
├── util/                 # 共用工具
├── Sqls/mysql/           # MySQL 建表 / 初始化腳本
├── wwwroot/               # 靜態資源
├── image/                 # 圖片素材
├── appsettings.json                       # 主要設定檔
├── appsettings.Development.json.example    # 開發環境設定範例
└── Program.cs / Startup.cs                 # 應用程式進入點與服務註冊
🚀 快速開始
環境需求
.NET SDK（版本請參考 global.json）

MySQL 資料庫

Neo4j 資料庫

安裝與設定
複製專案

bash
git clone https://github.com/pojkhb/Play_Taiwan.git
cd Play_Taiwan
設定環境變數 複製設定檔範例並填入自己的資料庫連線字串、JWT 密鑰等機敏資訊：

bash
cp appsettings.Development.json.example appsettings.Development.json
再依實際環境編輯 appsettings.Development.json 中的 MySQL / Neo4j 連線字串、JWT 設定與其他金鑰。

建立資料庫 使用 Sqls/mysql/ 內的腳本建立所需資料表。

還原套件並啟動專案

bash
dotnet restore
dotnet run
開啟 Swagger 文件 專案啟動後於瀏覽器開啟：

text
https://localhost:<port>/swagger
即可查看並測試所有 API。

📡 API 總覽
Controller	用途
Controller	用途
AuthController	註冊 / 登入 / JWT 發行
MapController	地圖與地點查詢
MerchantController	店家資訊
TaskController / TaskHintController	任務派發、提示
PostcardController / PostcardCatalogController	明信片收集與圖鑑
BadgeController	徽章成就
StoryController	劇情敘事內容
SilhouetteController	剪影猜景點小遊戲
HistoryController	使用者歷程紀錄
UploadController	檔案上傳
詳細請求參數與回應格式請以 Swagger UI 為準。

🔒 安全性注意事項
appsettings.json 中請勿提交任何真實資料庫密碼或 JWT 密鑰，敏感設定請放在 appsettings.Development.json（已加入 .gitignore）。

API 皆透過 JWT Middleware 驗證身份，請在請求標頭中帶入 Authorization: Bearer <token>。

🏪 商家資料維護 + NFC 模組（第一階段）

對應 `play_taiwan_db_v4` 新增的 `auth`(auth_type=2)/`store`/`coupon`/`nfc_coupon`/`user_coupon`/
`store_question`/`question_option` 資料表，Controller 分散在 `MerchantAccountController`、
`MerchantPlaceController`、`MerchantCouponController`、`MerchantNfcController`、`NfcController`、
`CouponRedeemController`、`MerchantQuestionController`（皆掛在 `api/merchant/*`、`api/nfc/*`、
`api/coupons/*` 底下），本階段不做登入驗證，直接以 request 帶入的 au_id / s_id 識別操作者
（見 `Services/ICurrentActorProvider.cs`，之後接登入驗證只要換一個實作即可）。

**部署前待辦：**
1. 執行 `Sqls/mysql/20260919_merchant_module_autoincrement.sql`，補上 `store`/`coupon`/
   `user_coupon`/`store_question`/`question_option` 這幾個 PK 欄位遺漏的 `AUTO_INCREMENT`
   （dump 匯出的 v4 schema 裡沒有設定，跟 `auth`/`story` 等表不同，不補的話新增資料時
   `LAST_INSERT_ID()` 拿不到正確的新 PK）。
2. 在本地測試用 Neo4j instance 上先建立版本鏈需要的 constraint：
   ```cypher
   CREATE CONSTRAINT IF NOT EXISTS FOR (v:Version) REQUIRE v.version_id IS UNIQUE;
   ```
   （景點身分節點的 `uid` 唯一約束沿用既有 `mergedb` 的 `Place.uid` constraint，不用重建。）
3. 確認 `appsettings.json` 的 `Neo4jSettings`（Uri/User/Password/Database）指到你本機的測試
   instance，帳密要跟該 instance 一致（目前 repo 裡的預設密碼只是佔位，本機驗證會直接失敗）。

**Neo4j 版本鏈設計**（`Services/Neo4j/PlaceVersionChainService.cs`）：政府開放資料的原始節點
永遠不變動；商家的補充/覆蓋資訊寫在新的 `:Current` 版本節點上，用 `[:HAS_VERSION]` 關聯回原始
節點，取代時舊版本轉標成 `:Historical`。商家全新建立的景點會建立一顆額外帶 `:MerchantPlace`
標籤的身分節點，走同一套版本鏈邏輯（`source: 'merchant'`），刪除商家帳號時只清這顆自建節點，
不會動到任何政府資料節點。

`Services/Neo4j/INeo4jGatewayService.cs` 有兩種實作，靠 `appsettings.json` 的 `Neo4j:Mode`
（`Local` / `Remote`）切換：
- `Local`：`LocalNeo4jDriverGatewayService`，用官方 Neo4j.Driver 直連本地測試 instance。
- `Remote`：`RemoteNeo4jApiGatewayService`，呼叫正式對外的 `POST /api/neo4j/cypher`
  （目前該端點只開放 MATCH 查詢，等外部開放完整 CRUD 後，把 `Neo4j:Mode` 改成 `Remote`
  即可直接生效，不用改任何業務邏輯程式碼）。

**已知假設 / 待補事項：**
- `store` 表沒有獨立的 phone/website 欄位，`PUT /api/merchant/{sId}` 目前只會同步
  name/address/description 到 Neo4j 版本節點，phone/website/opening_hours 欄位留空；
  之後如果要讓商家補這些資訊，需要先擴充 `store` 表或另開一支專門的「編輯 Neo4j 商家資訊」API。
- `GET /api/nfc/scan/{nfcUid}` 在該景點還沒有任何商家版本節點時，`merchant_place.merchant_override`
  會是 `null`，前端請自行 fallback 顯示 `gov_name`/`gov_address`（政府原始資料）。
- 刪除題目/商家時若被 `task.question_id` 引用，目前一律擋下並回傳 409 + 引用的 task_id 清單，
  沒有做「自動把 task.question_id 設為 NULL」之類的替代方案，如果之後想改成非阻擋式，
  對應邏輯在 `MerchantQuestionService.Delete` 與 `MerchantAccountDao.DeleteCascade`。
- 商家整體刪除時，MySQL 端有完整 Transaction 保護，但 Neo4j 清理是在 MySQL commit 成功
  「之後」才呼叫，兩邊沒有兩階段提交；若 Neo4j 那步失敗，只會記 log，不會讓 MySQL 回滾
  （因為 MySQL 資料已經確定刪除），這階段先接受這個限制。

🤝 貢獻方式
歡迎提出 Issue 或 Pull Request：

Fork 本專案

建立功能分支（git checkout -b feature/your-feature）

提交變更（git commit -m "feat: 說明你的變更"）

推送分支並發送 Pull Request

📄 授權
<!-- TODO: 請補充授權條款，例如 MIT License -->

👤 作者
GitHub: @pojkhb