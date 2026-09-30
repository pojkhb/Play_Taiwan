# Play Taiwan 後端 API 總覽

更新日期：2026-09-30 ・ 共 87 支 API ・ 詳細參數請看 Swagger：`http://<伺服器>:5501/swagger`

完整清單在「API 清單」資料庫（同時匯入的 `API清單.csv`），可以依分類、狀態、對應畫面篩選。

## 共用規則

| 項目 | 說明 |
|---|---|
| API 網址 | `http://<伺服器>:5501/api/...`（伺服器網址不含 `/api`） |
| 登入 | 先呼叫 `POST /api/Auth/Login` 取得 token，之後在標頭帶 `Authorization: Bearer <token>`。「需要登入」的 API 沒帶會回 401 |
| 回傳格式 | 全部包在 `{ "isSuccess": true/false, "message": "說明", "Result": 資料 }`；失敗時看 `message` |
| 欄位命名 | 跟 C# 屬性同名（`story_id`、`isSuccess`、`Result`），不會自動轉成小駝峰 |
| 圖片網址 | `/` 開頭的（例如上傳的檔案）是後端上的檔案，要接在**伺服器網址**後面；`http` 開頭的（例如景點照片 `image_url`）直接用 |
| 很久才回應的 API | 劇本生成（`GenerateGameStory`、`GenerateByText`）可能要好幾分鐘，請把逾時設長並顯示等待畫面；VLOG 合成送出後用 Status 輪詢 |

## 狀態說明

| 狀態 | 意思 | 數量 |
|---|---|---|
| ✅ 實測可用 | 實際打過或有自動化測試，正常 | 73 |
| ⚠️ 卡在 AI 服務 | 後端正常，卡在配璇的 AI 服務（AI 的 Neo4j 沒開、CUDA 錯誤、未知的 node_type） | 5 |
| 🔧 隊友改寫中 | 隊友改寫中，合併後再測 | 5 |
| 🛠 管理員用 | 管理員同步資料用 | 4 |

## App 畫面對應的 API

### 一、首頁

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 目前總覽 | `GET /api/Home/Overview` | ✅ 實測可用 |  |
| 徽章收藏 | `GET /api/Badge/Status` | ✅ 實測可用 |  |
| 過往旅途 | `GET /api/History` | ✅ 實測可用 |  |
| 探員資訊 | `GET /api/Auth/Profile` | ✅ 實測可用 |  |
| 進行中的劇本 | `GET /api/Story/CurrentPlaying` | ✅ 實測可用 |  |

### 二、出發探險

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 現在揪出發（定位或指定地區） | `POST /api/Story/GenerateGameStory` | ⚠️ 卡在 AI 服務 | 一次生成 3 份劇本（劇本檔案館的 3 張車票）。會呼叫 AI，可能要好幾分鐘，前端請把逾時設長並顯示等待畫面。需要 Valhalla（Docker）。來點遊意思：前端轉盤抽出縣市後，用 city_name 呼叫這支。節點有 image_url（車票照片）與 location_codename（模糊線索） |
| 來點遊意思（前端轉盤抽縣市後呼叫） | `POST /api/Story/GenerateGameStory` | ⚠️ 卡在 AI 服務 | 同上方說明 |
| 遊你說算 | `POST /api/Story/GenerateByText` | ⚠️ 卡在 AI 服務 | 遊你說算。AI 在背景生成，後端每 3 秒查一次進度，最多等 10 分鐘才回應；失敗時 message 會寫卡在哪個階段 |

### 三、組隊集結

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 人數、偏好、交通方式（party_size、preferences、transportation） | `POST /api/Story/GenerateGameStory` | ⚠️ 卡在 AI 服務 | 同上方說明 |
| 交通方式能不能選（反灰） | `GET /api/Route/Availability` | ✅ 實測可用 |  |

### 四、劇本檔案館

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 三份劇本（標題、前傳、照片、模糊線索、預計獲得） | `POST /api/Story/GenerateGameStory` | ⚠️ 卡在 AI 服務 | 同上方說明 |
| 重新查詢劇本內容 | `GET /api/Story/{story_id}/Detail` | ✅ 實測可用 | 9/30 起多了 is_night_mode（日夜）與每站 task_type（任務類型），原本的 FullDetail 已合併進來 |

### 五、劇情前傳

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 確認劇本、開始遊玩 | `POST /api/Story/Confirm` | ✅ 實測可用 |  |
| 前傳文字 | `GET /api/Story/{story_id}/Detail` | ✅ 實測可用 | 同上方說明 |
| NPC 語音 | `POST /api/Npc/Prologue/{story_id}` | ✅ 實測可用 | 回傳 audio_url（mp3），直接播放。voice 可省略 |

### 六、解謎地圖

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 地圖、收集進度、迷霧提示 | `GET /api/Map/{story_id}` | ✅ 實測可用 | 迷霧由後端處理，前端照欄位顯示：未解鎖的站 location_name 是地點代號、image_url 是這一站照片的霧化版（若隱若現，還沒做好時先給通用迷霧圖）、lat/lng 是迷霧中心，以它為中心畫半徑 fog_radius_m 公尺的迷霧；解鎖後才是真正的站名、照片與座標 |
| 目前位置 | `POST /api/Map/Location` | ✅ 實測可用 | 不用登入 |
| 抵達節點 | `POST /api/Map/Node/{node_id}/Arrive` | ✅ 實測可用 | 要照順序：還在迷霧中的站回傳 403；抵達後下一站的迷霧散開 |
| 節點詳情 | `GET /api/Map/Node/{node_id}` | ✅ 實測可用 | 還在迷霧中的站回傳 403 |
| 開始導航 | `POST /api/Map/Navigate` | ✅ 實測可用 | 還在迷霧中的站回傳 403（避免洩漏精確位置） |
| 交通規劃 | `POST /api/Route/Plan` | ✅ 實測可用 | 帶 story_id 依劇本節點規劃；不帶時用 points 規劃自選地點（原本的 Story/TravelRoute、Story/{story_id}/Transit 已合併進來） |
| 查看任務 | `POST /api/Task/List` | 🔧 隊友改寫中 |  |
| 周邊好去處 | `GET /api/Map/{story_id}/Nearby` | ✅ 實測可用 |  |

### 七、任務完成與明信片

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 任務答題 | `POST /api/Task/Answer` | 🔧 隊友改寫中 |  |
| 拍照上傳 | `POST /api/Upload` | ✅ 實測可用 |  |
| 生成明信片 | `POST /api/PostcardCatalog/GenerateAi` | ✅ 實測可用 | multipart/form-data：user_image（照片）、spot_name、story_id、node_id。任務完成後由前端呼叫 |
| 完成劇本 | `POST /api/Story/EndStory` | ✅ 實測可用 | 把劇本標成完成（首頁總覽、過往旅途、抽徽章都以這個為準） |
| 抽徽章 | `POST /api/Badge/Draw` | ✅ 實測可用 | 要先呼叫 EndStory 把劇本標成完成才能抽；一個劇本只能抽一次，重抽回傳同一枚（is_new=false） |

### 八、徽章收藏

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 徽章圖鑑與已擁有 | `GET /api/Badge/Status` | ✅ 實測可用 |  |

### 九、過往旅途

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 通關紀錄 | `GET /api/History` | ✅ 實測可用 |  |
| 旅程故事 | `GET /api/History/{story_id}` | ✅ 實測可用 |  |
| 旅程明信片 | `GET /api/PostcardCatalog/by-story/{storyId}` | ✅ 實測可用 |  |
| Vlog 旁白草稿 | `POST /api/VisitorVlog/Preview` | ⚠️ 卡在 AI 服務 | AI 端目前 CUDA 錯誤（9/30 重測仍是 no kernel image is available） |
| Vlog 合成 | `POST /api/VisitorVlog/CreateFinal` | ✅ 實測可用 | 照片：玩家拍的優先，某站沒拍就用景點照片。送出後用 Status 輪詢（建議每 5–10 秒）直到 status=3 拿 video_url |
| Vlog 進度與影片 | `GET /api/VisitorVlog/Status/{story_id}` | ✅ 實測可用 |  |

### 商家端

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 敘事語氣選項 | `GET /api/MerchantVlog/Tones` | ✅ 實測可用 |  |
| 商家 VLOG 草稿 | `POST /api/MerchantVlog/Preview` | ⚠️ 卡在 AI 服務 | multipart/form-data；images 可重複放多張。AI 會先到它自己的 Neo4j 查店家，查不到就回「不進行幻覺補全」；AI 端 Neo4j 目前沒開 |
| 商家 VLOG 合成 | `POST /api/MerchantVlog/CreateFinal` | ✅ 實測可用 | 自動附上背景音樂 wwwroot/bgm/default_bgm.mp3 |
| 商家 VLOG 進度 | `GET /api/MerchantVlog/Status/{mm_id}` | ✅ 實測可用 |  |
| 已生成檔案 | `GET /api/Merchant/Files` | ✅ 實測可用 |  |
| 商家註冊 | `POST /api/merchant/register` | ✅ 實測可用 | 需要 Neo4j 的 playtaiwandb 資料庫（本機已建好：Place.uid、Version.version_id 唯一約束）。9/30 修正：Email 重複註冊失敗時不再在 Neo4j 留下孤兒景點 |
| 優惠券 | `GET /api/merchant/coupons` | ✅ 實測可用 |  |
| NFC 掃描 | `GET /api/nfc/scan/{nfcUid}` | ✅ 實測可用 | 需要 Neo4j 的 playtaiwandb 資料庫（本機已建好：Place.uid、Version.version_id 唯一約束）。本機資料庫的表叫 qrcode_coupon(qr_uid)，程式與 schema.sql 是 nfc_coupon(nfc_uid)，組內要先統一，否則本機會報「nfc_coupon 不存在」 |

## 分類清單

### 帳號（9）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Auth/ChangePassword` | 修改密碼 | 需要 | ✅ 實測可用 | JSON：OldPassword（string）、NewPassword（string） |
| `POST /api/Auth/ForgotPassword` | 忘記密碼 | 不用 | ✅ 實測可用 | JSON：Email（string） |
| `POST /api/Auth/Login` | 帳號登入 | 不用 | ✅ 實測可用 | JSON：auth_name（string）、auth_pswd（string） |
| `POST /api/Auth/Logout` | 帳號登出 | 需要 | ✅ 實測可用 |  |
| `GET /api/Auth/Profile` | 取得目前登入帳號的資訊 | 需要 | ✅ 實測可用 |  |
| `POST /api/Auth/Profile` | 更新目前登入帳號的名稱 | 需要 | ✅ 實測可用 | JSON：auth_name（string） |
| `POST /api/Auth/Register` | 帳號註冊 | 不用 | ✅ 實測可用 | JSON：Username（string）、Email（string）、Password（string）、Birthday（string）、Gender（string）、AccountType（integer）、ResolvedAccoun |
| `POST /api/Auth/ResetPassword` | 依重設連結 Token 完成密碼重設 | 不用 | ✅ 實測可用 | JSON：Token（string）、NewPassword（string） |
| `GET /api/Auth/VerifyEmail` | 信箱驗證與啟用帳號 | 不用 | ✅ 實測可用 | token（查詢，string） |

### 首頁（1）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `GET /api/Home/Overview` | 取得首頁目前總覽資訊 | 需要 | ✅ 實測可用 |  |

### 劇本生成（3）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Story/GenerateByText` | 接收使用者輸入的一句自然語言描述，由外部 AI 服務自動解析出城市/行政區/人數/偏好等條件， 並直接生成完整劇本、寫入 | 需要 | ⚠️ 卡在 AI 服務 | JSON：user_prompt（string） |
| `POST /api/Story/GenerateGameStory` | 依使用者位置與交通方式自動挑景點，由 AI 一次生成 3 份劇本（含每個景點的任務）讓使用者挑，並存入資料庫 | 需要 | ⚠️ 卡在 AI 服務 | JSON：lat（number）、lng（number）、city_name（string）、town_name（string）、party_size（integer）、transportation（string[]）、preference |
| `POST /api/Story/spin` | 接收前端傳入的語音文字、情緒標籤與城市/行政區， 後端先將城市/行政區轉換為經緯度，再透過 AI Agent 服務即時推 | 需要 | ⚠️ 卡在 AI 服務 | JSON：input_text（string）、emotion_label（string）、city_name（string）、town_name（string） |

### 劇本（4）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Story/Confirm` | 玩家確認選擇指定的劇本卷，準備進入探索地圖。會將此劇本標記為「正在遊玩中」（is_playing = 1）， 並自動把其 | 需要 | ✅ 實測可用 | JSON：story_id（integer） |
| `GET /api/Story/CurrentPlaying` | 查詢目前正在進行中的劇本是哪一個（is_playing = 1 的那筆） | 需要 | ✅ 實測可用 |  |
| `POST /api/Story/EndStory` | 玩家完成或退出劇本時呼叫，把該劇本的進行狀態改回未進行（is_playing = 0） | 需要 | ✅ 實測可用 | JSON：story_id（integer） |
| `GET /api/Story/{story_id}/Detail` | 取得指定劇本的詳細內容（含各節點地點名稱、任務提示、對應 NPC） | 需要 | ✅ 實測可用 | story_id（路徑，integer） |

### NPC 語音（2）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Npc/Prologue/{story_id}` | 劇情前傳語音：唸指定劇本的前傳 | 需要 | ✅ 實測可用 | story_id（路徑，integer）；voice（查詢，string） |
| `POST /api/Npc/Speak` | NPC 說話：任意文字轉語音（節點對話、任務說明等） | 需要 | ✅ 實測可用 | JSON：text（string）、voice（string） |

### 解謎地圖（7）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Map/Location` | 取得使用者現在所在位置 | 不用 | ✅ 實測可用 | JSON：lat（number）、lng（number）、accuracy（number） |
| `POST /api/Map/Navigate` | 取得前往指定節點的導航資訊 | 需要 | ✅ 實測可用 | JSON：node_id（integer） |
| `GET /api/Map/Node/{node_id}` | 取得指定節點的詳細內容 | 需要 | ✅ 實測可用 | node_id（路徑，integer） |
| `POST /api/Map/Node/{node_id}/Arrive` | 使用 GPS 座標確認探員已抵達指定節點 | 需要 | ✅ 實測可用 | node_id（路徑，integer）；lat（查詢，number）；lng（查詢，number） |
| `GET /api/Map/Node/{node_id}/Interact` | 取得指定節點的 NPC 隨機互動內容 | 需要 | ✅ 實測可用 | node_id（路徑，integer） |
| `GET /api/Map/{story_id}` | 取得指定劇本的地圖資訊 | 需要 | ✅ 實測可用 | story_id（路徑，integer） |
| `GET /api/Map/{story_id}/Nearby` | 取得指定劇本周邊的推薦去處 | 需要 | ✅ 實測可用 | story_id（路徑，integer）；category（查詢，string） |

### 交通與景點（11）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `GET /api/Bus/Nearby` | 查詢座標附近的公車站牌，以及每個站牌經過的路線（含台灣好行） | 需要 | ✅ 實測可用 | lat（查詢，number）；lng（查詢，number）；radius（查詢，integer） |
| `GET /api/Bus/Route/{br_id}` | 取得公車路線詳情：去程/返程的完整站牌、路線線形、起站發車時間 | 需要 | ✅ 實測可用 | br_id（路徑，integer） |
| `GET /api/Bus/Stop/{bs_id}/Arrivals` | 查詢站牌的即時到站時間（每條路線還有幾分鐘到） | 需要 | ✅ 實測可用 | bs_id（路徑，integer） |
| `GET /api/Bus/TaiwanTrip` | 取得全台營運中的台灣好行路線列表 | 需要 | ✅ 實測可用 |  |
| `GET /api/Metro/Lines` | 取得座標附近的捷運路線（含車站與線形），在地圖上畫捷運路網用 | 需要 | ✅ 實測可用 | lat（查詢，number）；lng（查詢，number）；radius_km（查詢，number） |
| `GET /api/Route/Availability` | 查詢各交通方式在這一帶能不能用（前端用來決定勾選框要不要反灰） | 需要 | ✅ 實測可用 | lat（查詢，number）；lng（查詢，number）；story_id（查詢，integer） |
| `POST /api/Route/Availability` | 查詢各交通方式在這些地點能不能用（自選地點、沒有劇本時用） | 需要 | ✅ 實測可用 |  |
| `POST /api/Route/Plan` | 規劃劇本交通路線：起點 → 依序經過每個節點（去程）→ 回到起點（返程） | 需要 | ✅ 實測可用 | JSON：story_id（integer）、start（RoutePoint）、points（RoutePoint[]）、transportation（string[]）、include_return（boolean） |
| `GET /api/Route/Stories` | 取得可以規劃交通路線的劇本（含節點座標與建議起點） | 需要 | ✅ 實測可用 |  |
| `GET /api/Story/NearbyAttractions` | 依使用者 GPS 座標，透過 Neo4j 查詢半徑範圍內的景點，依距離由近到遠排序 | 需要 | ✅ 實測可用 | lat（查詢，number）；lng（查詢，number）；radiusKm（查詢，number） |
| `POST /api/Story/ReachableAttractions` | 依中心點與交通方式，找出可到達的景點，並回傳每個景點的真實交通時間、距離、所在圈層 | 需要 | ✅ 實測可用 | JSON：lat（number）、lng（number）、city_name（string）、town_name（string）、transportation（string[]）、contour_minutes（integer[]）、inc |

### 任務（5）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Task/Answer` | 送出任務答案並取得答題結果 | 不用 | 🔧 隊友改寫中 | JSON：task_id（integer）、gps_lon（number）、gps_lat（number）、selected_option_key（string）、text_answer（string）、photo_url（string）、 |
| `POST /api/Task/Generate` | /api/Task/Generate | 不用 | 🔧 隊友改寫中 | JSON：story_id（string）、node_id（string）、player_count（integer） |
| `POST /api/Task/HiddenLevel/Check` | 依玩家目前 GPS 座標檢查是否觸發隱藏關卡 | 不用 | 🔧 隊友改寫中 | ep_id（查詢，string）；lat（查詢，number）；lng（查詢，number）；region_id（查詢，string） |
| `POST /api/Task/List` | /api/Task/List | 不用 | 🔧 隊友改寫中 | JSON：node_id（string）、gps_lon（number）、gps_lat（number）、player_index（integer） |
| `GET /api/Task/{task_id}/Hint` | 取得指定任務的提示內容 | 不用 | 🔧 隊友改寫中 | task_id（路徑，string）；ep_id（查詢，string） |

### 上傳（1）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Upload` | 上傳檔案（照片、影片、錄音），回傳檔案網址 | 需要 | ✅ 實測可用 | 表單：file（檔案） |

### 明信片（8）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `GET /api/PostcardCatalog` | 取得所有明信片主檔清單 | 需要 | ✅ 實測可用 |  |
| `POST /api/PostcardCatalog/GenerateAi` | 呼叫外部服務生成 AI 明信片並存入資料庫 | 需要 | ✅ 實測可用 | 表單：user_image（檔案）、spot_name（string）、user_prompt（string）、story_id（integer）、node_id（integer）、is_night_edition（boolean） |
| `POST /api/PostcardCatalog/Print` | 透過 postcard_id 送出明信片至 ibon 列印，取得取件碼 | 不用 | ✅ 實測可用 | JSON：postcard_id（integer） |
| `POST /api/PostcardCatalog/Share` | 紀錄使用者已將該劇本的明信片分享至社群平台 | 不用 | ✅ 實測可用 | JSON：story_id（integer）、platform（string） |
| `GET /api/PostcardCatalog/by-story/{storyId}` | 取得指定劇本 (story_id) 所擁有的「所有」明信片主檔 | 需要 | ✅ 實測可用 | storyId（路徑，integer） |
| `GET /api/PostcardCatalog/{id}` | 依明信片識別碼 (postcard_id) 取得單一明信片主檔資料 | 不用 | ✅ 實測可用 | id（路徑，integer） |
| `POST /api/PostcardCatalog/{id}/Delete` | 刪除明信片主檔 | 需要 | ✅ 實測可用 | id（路徑，integer） |
| `GET /api/PostcardCatalog/{id}/image` | 依 postcard_id 取得指定明信片的圖片，轉址 (302 Redirect) 至實際圖片網址 | 不用 | ✅ 實測可用 | id（路徑，integer） |

### 徽章（2）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Badge/Draw` | 完成劇本後抽一枚勳章，一個劇本只能抽一次 | 需要 | ✅ 實測可用 | JSON：story_id（integer） |
| `GET /api/Badge/Status` | 取得系統所有徽章，依系列分組，並標示當前探員是否已擁有該徽章 | 需要 | ✅ 實測可用 |  |

### 過往旅途（2）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `GET /api/History` | 取得目前探員的所有過往劇本清單 | 需要 | ✅ 實測可用 |  |
| `GET /api/History/{story_id}` | 取得單一過往劇本的詳細內容 (包含所有經歷過的景點清單) | 需要 | ✅ 實測可用 | story_id（路徑，integer） |

### 遊客 VLOG（3）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/VisitorVlog/CreateFinal` | 送出確認後的旁白，後端打包照片與景點資料送去合成影片 | 需要 | ✅ 實測可用 | JSON：story_id（integer）、final_script（string）、promo_copy（string）、seo_keywords（string[]） |
| `POST /api/VisitorVlog/Preview` | 遊戲結束後，依 story_id 產生 VLOG 旁白草稿 | 需要 | ⚠️ 卡在 AI 服務 | JSON：story_id（integer） |
| `GET /api/VisitorVlog/Status/{story_id}` | 查詢這個劇本的 VLOG（影片、旁白、宣傳文案） | 需要 | ✅ 實測可用 | story_id（路徑，integer）；task_id（查詢，string） |

### 商家 VLOG（6）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `GET /api/Merchant/Files` | 取得商家已生成的檔案清單 | 需要 | ✅ 實測可用 |  |
| `POST /api/Merchant/StoreName` | 修改登入商家的「店家名稱」 | 需要 | ✅ 實測可用 | JSON：store_name（string） |
| `POST /api/MerchantVlog/CreateFinal` | 商家確認旁白後，把專案照片打包送去合成影片 | 需要 | ✅ 實測可用 | JSON：mm_id（integer）、final_script（string）、caption（string）、hashtags（string[]） |
| `POST /api/MerchantVlog/Preview` | 輸入店家資訊與照片，產生 AI 旁白草稿、推薦配文、TAG | 需要 | ⚠️ 卡在 AI 服務 | 表單：mm_id（integer）、store_name（string）、open_time（string）、address（string）、nt_id（integer）、promo_text（string）、images（檔案[]） |
| `GET /api/MerchantVlog/Status/{mm_id}` | 查詢商家 VLOG 專案：影片、推薦配文、TAG | 需要 | ✅ 實測可用 | mm_id（路徑，integer） |
| `GET /api/MerchantVlog/Tones` | 取得可選的敘事語氣（例如幽默詼諧、質感專業、溫情走心） | 需要 | ✅ 實測可用 |  |

### 商家後台與優惠券（19）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/coupons/{couponId}/redeem` | 核銷優惠券 | 不用 | ✅ 實測可用 | couponId（路徑，integer）；JSON：au_id（integer） |
| `GET /api/merchant` | 查詢商家列表 | 不用 | ✅ 實測可用 | keyword（查詢，string）；page（查詢，integer）；page_size（查詢，integer） |
| `GET /api/merchant/coupons` | 查詢商家優惠券列表 | 不用 | ✅ 實測可用 | storeId（查詢，integer） |
| `POST /api/merchant/coupons` | 新增優惠券 | 不用 | ✅ 實測可用 | JSON：s_id（integer）、coupon_code（string）、coupon_name（string）、discount_commodity（string）、discount_type（string）、discount_val |
| `DELETE /api/merchant/coupons/{couponId}` | 刪除優惠券 | 不用 | ✅ 實測可用 | couponId（路徑，integer） |
| `GET /api/merchant/coupons/{couponId}` | 查詢單筆優惠券 | 不用 | ✅ 實測可用 | couponId（路徑，integer） |
| `PUT /api/merchant/coupons/{couponId}` | 修改優惠券 | 不用 | ✅ 實測可用 | couponId（路徑，integer）；JSON：coupon_code（string）、coupon_name（string）、discount_commodity（string）、discount_type（string）、disco |
| `PATCH /api/merchant/coupons/{couponId}/status` | 上下架優惠券 | 不用 | ✅ 實測可用 | couponId（路徑，integer）；JSON：status（string） |
| `POST /api/merchant/nfc/bind` | 綁定 NFC 貼紙與優惠券 | 不用 | ✅ 實測可用 | JSON：nfc_uid（string）、coupon_id（integer） |
| `GET /api/merchant/questions` | 查詢商家題庫列表 | 不用 | ✅ 實測可用 | storeId（查詢，integer） |
| `POST /api/merchant/questions` | 新增題目 | 不用 | ✅ 實測可用 | JSON：store_id（integer）、question_describe（string）、options（QuestionOptionInput[]） |
| `DELETE /api/merchant/questions/{questionId}` | 刪除題目 | 不用 | ✅ 實測可用 | questionId（路徑，integer） |
| `PUT /api/merchant/questions/{questionId}` | 修改題目與選項 | 不用 | ✅ 實測可用 | questionId（路徑，integer）；JSON：question_describe（string）、options（QuestionOptionInput[]） |
| `POST /api/merchant/register` | 註冊商家 | 不用 | ✅ 實測可用 | JSON：auth_name（string）、auth_email（string）、auth_pswd（string）、store_name（string）、store_dec（string）、store_address（string）、s |
| `GET /api/merchant/register/search-place` | 搜尋既有景點 | 不用 | ✅ 實測可用 | keyword（查詢，string） |
| `DELETE /api/merchant/{sId}` | 刪除商家帳號 | 不用 | ✅ 實測可用 | sId（路徑，integer） |
| `GET /api/merchant/{sId}` | 查詢商家資料 | 不用 | ✅ 實測可用 | sId（路徑，integer） |
| `PUT /api/merchant/{sId}` | 更新商家資料 | 不用 | ✅ 實測可用 | sId（路徑，integer）；JSON：store_name（string）、store_dec（string）、store_address（string）、store_city（string）、store_town（string） |
| `GET /api/nfc/scan/{nfcUid}` | 掃描 NFC 貼紙查詢商家與優惠券資訊 | 不用 | ✅ 實測可用 | nfcUid（路徑，string）；auId（查詢，integer） |

### 管理員（4）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Bus/PlaceStops/Build` | 重算景點附近的公車站牌（生成劇本規劃公車用） | 需要 | 🛠 管理員用 | scope（查詢，string） |
| `POST /api/Bus/Sync/City/{city}` | 從 TDX 同步某縣市的市區公車 | 需要 | 🛠 管理員用 | city（路徑，string） |
| `POST /api/Bus/Sync/TaiwanTrip` | 從 TDX 同步全台台灣好行 | 需要 | 🛠 管理員用 |  |
| `POST /api/Metro/Sync/{system}` | 從 TDX 同步一個捷運系統 | 需要 | 🛠 管理員用 | system（路徑，string） |
