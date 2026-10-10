# Play Taiwan 後端 API 總覽

更新日期：2026-10-07 ・ 依 GitHub main（239bbc9） 產生 ・ 共 100 支 API ・ 詳細參數請看 Swagger：`http://<伺服器>:5501/`（後端首頁就是 Swagger）

完整清單在「API 清單」資料庫（同時匯入的 `API清單.csv`），可以依分類、狀態、對應畫面篩選。每支 API 的 Request／回傳 JSON 在最後的「每支 API 的 JSON」。

## 共用規則

| 項目 | 說明 |
|---|---|
| API 網址 | `http://<伺服器>:5501/api/...`（伺服器網址不含 `/api`） |
| 登入 | 先呼叫 `POST /api/Auth/Login` 取得 token，之後在標頭帶 `Authorization: Bearer <token>`。「需要登入」的 API 沒帶會回 401 |
| 回傳格式 | 全部包在 `{ "isSuccess": true/false, "message": "說明", "Result": 資料 }`；失敗時看 `message` |
| 欄位命名 | 跟 C# 屬性同名（`story_id`、`isSuccess`、`Result`），不會自動轉成小駝峰 |
| JSON 範例的值 | 只代表型別：`"string"` 是字串、`0` 是整數、`0.0` 是小數、`false` 是布林、陣列只放一筆示意；實際值依資料而定，可能是 null |
| 圖片網址 | `/` 開頭的（例如上傳的檔案）是後端上的檔案，要接在**伺服器網址**後面；`http` 開頭的（例如景點照片 `image_url`）直接用 |
| 很久才回應的 API | 劇本生成（`GenerateGameStory`、`GenerateByText`）可能要好幾分鐘，請把逾時設長並顯示等待畫面；VLOG 合成送出後用 Status 輪詢 |

## 狀態說明

| 狀態 | 意思 | 數量 |
|---|---|---|
| ✅ 實測可用 | 有自動化測試打過，或實際打過，正常 | 74 |
| ⚠️ 卡在 AI 服務 | 後端已用模擬的 AI 服務測過，正式環境卡在配璇的 AI 服務（10/6 AI 服務連不上，回 502） | 5 |
| ❌ 目前不能用 | 程式查的資料表不存在，呼叫會出錯 | 1 |
| 🗑 舊版（不用接） | 舊版或內部用，前端不用接 | 2 |
| 🛠 管理員用 | 管理員同步資料用 | 4 |
| ⬜ 未實測 | 有 API，還沒有自動化測試，也沒實際打過 | 14 |

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
| 現在揪出發（定位或指定地區） | `POST /api/Story/GenerateGameStory` | ⚠️ 卡在 AI 服務 | 一次生成 3 份劇本（劇本檔案館的 3 張車票）。會呼叫 AI，可能要好幾分鐘，前端請把逾時設長並顯示等待畫面。需要 Valhalla（Docker）。來點遊意思：前端轉盤抽出縣市後，用 city_name 呼叫這支。節點有 image_url（車票照片）與 location_codename（模糊線索）；story.npc 是這份劇本的 NPC（名稱、身分、圖片、聲線） |
| 來點遊意思（前端轉盤抽縣市後呼叫） | `POST /api/Story/GenerateGameStory` | ⚠️ 卡在 AI 服務 | 同上方說明 |
| 遊你說算 | `POST /api/Story/GenerateByText` | ⚠️ 卡在 AI 服務 | 遊你說算。AI 在背景生成，後端每 3 秒查一次進度，最多等 10 分鐘才回應；失敗時 message 會寫卡在哪個階段 |

### 三、組隊集結

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 人數、偏好、交通方式（party_size、preferences、transportation） | `POST /api/Story/GenerateGameStory` | ⚠️ 卡在 AI 服務 | 同上方說明 |
| 交通方式能不能選（反灰） | `GET /api/Route/Availability` | ✅ 實測可用 |  |
| 題型解鎖進度 | `GET /api/Task/Progress` | ⬜ 未實測 | 產生劇本前顯示：第一次到的鄉鎮市區不出 e人訪談型，locked_types 附解鎖條件 |
| 產生配對碼（隊長） | `POST /api/Pair/Code` | ⬜ 未實測 | 劇本擁有者產生四位數配對碼，有效期內重複呼叫回傳同一組；單人劇本或隊伍已滿回 409 |
| 輸入配對碼加入（隊員） | `POST /api/Pair/Join` | ⬜ 未實測 | 輸入配對碼加入，坐最小的空座位，同時把這份劇本設為自己進行中的劇本；配對碼錯誤或逾期 404、已滿 409 |
| 等待隊員、查看隊伍 | `GET /api/Pair/Story/{story_id}` | ⬜ 未實測 | 隊長等待隊員時輪詢這支：成員、座位、my_seat_no、配對碼剩餘秒數；還沒組隊時 Result 為 null |

### 四、劇本檔案館

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 三份劇本（標題、前傳、照片、模糊線索、預計獲得） | `POST /api/Story/GenerateGameStory` | ⚠️ 卡在 AI 服務 | 同上方說明 |
| 重新查詢劇本內容 | `GET /api/Story/{story_id}/Detail` | ✅ 實測可用 | npc：劇本的 NPC（name、role、intro、avatar_url、voice），沒指定時是預設的薯光；每站有 npc_name 與 task_type（任務類型）；is_night_mode 是日夜 |
| 加入 Google 行事曆 | `POST /api/Story/{story_id}/Calendar` | ✅ 實測可用 | 回傳 google_calendar_url，前端用 url_launcher 打開，使用者按「儲存」就加進自己的 Google 行事曆，不需要授權。body 可省略（預設明天 10:00，夜間劇本 19:00）。地點只放第一站，其他站在迷霧中不會出現 |
| 喜愛／取消喜愛 | `POST /api/Story/{story_id}/Favorite` | ✅ 實測可用 | body：{ "is_favorite": true/false }。只能設定自己的劇本，劇本詳情會帶 is_favorite |
| 我喜愛的劇本 | `GET /api/Story/Favorites` | ✅ 實測可用 | 每筆有標題、簡介、地區、站數、日夜、封面（第一站照片）、是否已玩完 |

### 五、劇情前傳

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 確認劇本、開始遊玩 | `POST /api/Story/Confirm` | ✅ 實測可用 |  |
| 前傳文字 | `GET /api/Story/{story_id}/Detail` | ✅ 實測可用 | 同上方說明 |
| NPC 語音 | `POST /api/Npc/Prologue/{story_id}` | ✅ 實測可用 | 回傳 audio_url（mp3），直接播放。voice 可省略，預設用劇本 NPC 的聲線 |

### 六、解謎地圖

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 地圖、收集進度、迷霧提示 | `GET /api/Map/{story_id}` | ✅ 實測可用 | 迷霧由後端處理，前端照欄位顯示：未解鎖的站 location_name 是地點代號、image_url 是這一站照片的霧化版（若隱若現，還沒做好時先給通用迷霧圖）、lat/lng 是迷霧中心，以它為中心畫半徑 fog_radius_m 公尺的迷霧；解鎖後才是真正的站名、照片與座標 |
| 目前位置 | `POST /api/Map/Location` | ✅ 實測可用 | 不用登入 |
| 抵達節點 | `POST /api/Map/Node/{node_id}/Arrive` | ✅ 實測可用 | 要照順序：還在迷霧中的站回傳 403；抵達後下一站的迷霧散開 |
| 節點詳情 | `GET /api/Map/Node/{node_id}` | ✅ 實測可用 | 還在迷霧中的站回傳 403。npc_name、npc_avatar_url 是這一站的 NPC |
| 開始導航 | `POST /api/Map/Navigate` | ✅ 實測可用 | 還在迷霧中的站回傳 403（避免洩漏精確位置） |
| NPC 互動 | `GET /api/Map/Node/{node_id}/Interact` | ✅ 實測可用 | 還在迷霧中的站回傳 403。npc_name、npc_role、npc_avatar_url、npc_voice 來自 NPC 名單（薯光、珍奶奶、阿達力、墨先生、霓霓、阿吉伯），台詞是這一站的開場白 |
| 交通規劃 | `POST /api/Route/Plan` | ✅ 實測可用 | 帶 story_id 依劇本節點規劃；不帶時用 points 規劃自選地點（原本的 Story/TravelRoute、Story/{story_id}/Transit 已合併進來） |
| 節點遊玩畫面（劇情、NPC、題目） | `GET /api/Task/Node/{node_id}` | ✅ 實測可用 | 取代舊的 Task/List。要先 Arrive 抵達這一站（否則 409）；npc 是說開場白、出題的 NPC（名稱、身分、圖片、聲線）；每題的 answer_mode 決定作答時帶哪個欄位 |
| 周邊好去處 | `GET /api/Map/{story_id}/Nearby` | ✅ 實測可用 |  |

### 七、任務完成與明信片

| 功能 | API | 狀態 | 備註 |
|---|---|---|---|
| 任務答題 | `POST /api/Task/Answer` | ⬜ 未實測 | GPS 要在任務地點 200 公尺內；要帶哪個作答欄位看 GET /api/Task/Node/{node_id} 每題的 answer_mode |
| 取得提示 | `GET /api/Task/{task_id}/Hint` | ✅ 實測可用 | 答錯次數達門檻才會給提示（門檻依題目難易度與玩家表現）；npc_name、npc_avatar_url 是給提示的 NPC |
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
| 劇本回顧（劇情、照片、收穫、旁白） | `GET /api/History/{story_id}/Recap` | ✅ 實測可用 | 只能看自己玩完的劇本（沒玩完回 400）。narration.text 直接顯示；narration.audio_url 為 null 時，按播放再呼叫 Recap/Narration |
| 回顧旁白語音 | `POST /api/History/{story_id}/Recap/Narration` | ✅ 實測可用 | 按播放時呼叫。第一次交給 AI 轉語音要等幾秒，之後直接回傳存好的 mp3。body 可省略，voice 可換聲音 |
| 旅程明信片 | `GET /api/PostcardCatalog/by-story/{storyId}` | ✅ 實測可用 |  |
| Vlog 旁白草稿 | `POST /api/VisitorVlog/Preview` | ⚠️ 卡在 AI 服務 | 後端已用模擬 AI 測過；AI 服務 10/6 連不上（502），9/30 時 AI 端是 CUDA 錯誤 |
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
| 商家註冊 | `POST /api/Merchant/Register` | ⬜ 未實測 | place_uid 與 new_place 二擇一；需要 Neo4j 的 playtaiwandb。Email 重複註冊失敗時不會在 Neo4j 留下孤兒景點 |
| 商家登入 | `POST /api/Merchant/Login` | ⬜ 未實測 | 商家專用登入（auth_type=2），Token 帶 s_id 與 Role=Merchant |
| 店家資料 | `GET /api/Merchant/Profile` | ⬜ 未實測 |  |
| 優惠券 | `GET /api/merchant/coupons` | ✅ 實測可用 |  |
| QR Code 綁定優惠券 | `POST /api/merchant/qrcode/bind` | ✅ 實測可用 | 優惠券要是自己店家的；同一個 QR Code 重複綁定回 409 |
| 遊客掃 QR Code 領券 | `GET /api/qrcode/scan/{qrUid}` | ⬜ 未實測 | 不用登入也能看；帶遊客 Token 時會同時領取優惠券（寫入 user_coupon） |
| 核銷優惠券 | `POST /api/coupons/{couponId}/redeem` | ✅ 實測可用 | 需要商家登入 |

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

### 劇本（7）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Story/Confirm` | 玩家確認選擇指定的劇本卷，準備進入探索地圖。會將此劇本標記為「正在遊玩中」（is_playing = 1）， 並自動把其 | 需要 | ✅ 實測可用 | JSON：story_id（integer） |
| `GET /api/Story/CurrentPlaying` | 查詢目前正在進行中的劇本是哪一個（is_playing = 1 的那筆） | 需要 | ✅ 實測可用 |  |
| `POST /api/Story/EndStory` | 玩家完成或退出劇本時呼叫，把自己在這份劇本的遊玩紀錄（story_session）標成完成 | 需要 | ✅ 實測可用 | JSON：story_id（integer） |
| `GET /api/Story/Favorites` | 我喜愛的劇本清單（最新建立的在前面） | 需要 | ✅ 實測可用 |  |
| `POST /api/Story/{story_id}/Calendar` | 把劇本加入 Google 行事曆：回傳 Google 行事曆的新增活動連結 | 需要 | ✅ 實測可用 | story_id（路徑，integer）；JSON：start_time（string）、duration_minutes（integer） |
| `GET /api/Story/{story_id}/Detail` | 取得指定劇本的詳細內容（含各節點地點名稱、任務提示、對應 NPC） | 需要 | ✅ 實測可用 | story_id（路徑，integer） |
| `POST /api/Story/{story_id}/Favorite` | 把劇本加入或取消喜愛 | 需要 | ✅ 實測可用 | story_id（路徑，integer）；JSON：is_favorite（boolean） |

### 組隊（5）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Pair/Cancel` | 取消協作隊伍（限劇本擁有者） | 需要 | ⬜ 未實測 | JSON：story_id（integer） |
| `POST /api/Pair/Code` | 產生配對碼（限劇本擁有者） | 需要 | ⬜ 未實測 | JSON：story_id（integer） |
| `POST /api/Pair/Join` | 輸入配對碼加入協作隊伍 | 需要 | ⬜ 未實測 | JSON：pair_code（string） |
| `POST /api/Pair/Leave` | 隊員退出協作隊伍 | 需要 | ⬜ 未實測 | JSON：story_id（integer） |
| `GET /api/Pair/Story/{story_id}` | 查詢劇本目前的協作隊伍（限擁有者與隊員） | 需要 | ⬜ 未實測 | story_id（路徑，integer） |

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

### 任務（7）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Task/Answer` | 送出任務答案並取得答題結果 | 需要 | ⬜ 未實測 | JSON：task_id（integer）、gps_lon（number）、gps_lat（number）、selected_option_key（string）、text_answer（string）、photo_url（string）、 |
| `POST /api/Task/Generate` | 測試用：直接對指定劇本或節點逐題呼叫 AI 生成任務 | 不用 | 🗑 舊版（不用接） | JSON：story_id（string）、node_id（string）、player_count（integer） |
| `POST /api/Task/HiddenLevel/Check` | 依玩家目前 GPS 座標檢查是否觸發隱藏關卡 | 不用 | ❌ 目前不能用 | ep_id（查詢，string）；lat（查詢，number）；lng（查詢，number）；region_id（查詢，string） |
| `POST /api/Task/List` | 取得節點的任務清單（已由 GET api/Task/Node/{node_id} 取代） | 需要 | 🗑 舊版（不用接） | JSON：node_id（string）、gps_lon（number）、gps_lat（number） |
| `GET /api/Task/Node/{node_id}` | 節點遊玩畫面：一次取得這一站的劇情、自己的座位、進度與每一題的作答方式與狀態 | 需要 | ✅ 實測可用 | node_id（路徑，integer） |
| `GET /api/Task/Progress` | 取得登入者在目前所在鄉鎮市區的題型解鎖進度（產生劇本前顯示用） | 需要 | ⬜ 未實測 | lat（查詢，number）；lng（查詢，number） |
| `GET /api/Task/{task_id}/Hint` | 取得指定任務的提示內容 | 需要 | ✅ 實測可用 | task_id（路徑，integer） |

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

### 過往旅途（4）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `GET /api/History` | 取得目前探員的所有過往劇本清單 | 需要 | ✅ 實測可用 |  |
| `GET /api/History/{story_id}` | 取得單一過往劇本的詳細內容 (包含所有經歷過的景點清單) | 需要 | ✅ 實測可用 | story_id（路徑，integer） |
| `GET /api/History/{story_id}/Recap` | 劇本回顧：玩完的劇本一次拿到劇情、每一站、收穫、統計與旁白 | 需要 | ✅ 實測可用 | story_id（路徑，integer） |
| `POST /api/History/{story_id}/Recap/Narration` | 產生回顧旁白的語音（mp3），回傳 audio_url | 需要 | ✅ 實測可用 | story_id（路徑，integer）；JSON：voice（string） |

### 遊客 VLOG（3）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/VisitorVlog/CreateFinal` | 送出確認後的旁白，後端打包照片與景點資料送去合成影片 | 需要 | ✅ 實測可用 | JSON：story_id（integer）、final_script（string）、promo_copy（string）、seo_keywords（string[]） |
| `POST /api/VisitorVlog/Preview` | 遊戲結束後，依 story_id 產生 VLOG 旁白草稿 | 需要 | ⚠️ 卡在 AI 服務 | JSON：story_id（integer） |
| `GET /api/VisitorVlog/Status/{story_id}` | 查詢這個劇本的 VLOG（影片、旁白、宣傳文案） | 需要 | ✅ 實測可用 | story_id（路徑，integer）；task_id（查詢，string） |

### 商家帳號（8）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `GET /api/Merchant/List` | 查詢商家列表（限管理員） | 需要 | ⬜ 未實測 | keyword（查詢，string）；page（查詢，integer）；page_size（查詢，integer） |
| `POST /api/Merchant/Login` | 商家登入，成功後回傳 JWT | 不用 | ⬜ 未實測 | JSON：auth_name（string）、auth_pswd（string） |
| `GET /api/Merchant/Profile` | 查詢登入商家的店家資料 | 需要 | ⬜ 未實測 |  |
| `PUT /api/Merchant/Profile` | 更新登入商家的店家資料 | 需要 | ✅ 實測可用 | JSON：store_name（string）、store_dec（string）、store_address（string）、store_city（string）、store_town（string）、lat（number）、lng（nu |
| `POST /api/Merchant/Register` | 註冊商家 | 不用 | ⬜ 未實測 | JSON：auth_name（string）、auth_email（string）、auth_pswd（string）、store_name（string）、store_dec（string）、store_address（string）、s |
| `GET /api/merchant/register/address-to-location` | 地址轉座標（自建景點表單自動帶出座標用） | 不用 | ⬜ 未實測 | store_city（查詢，string）；store_town（查詢，string）；store_address（查詢，string） |
| `GET /api/merchant/register/location-to-address` | 座標轉地址（自建景點表單自動帶出地址用） | 不用 | ⬜ 未實測 | lat（查詢，number）；lng（查詢，number） |
| `GET /api/merchant/register/search-place` | 搜尋既有景點 | 不用 | ✅ 實測可用 | keyword（查詢，string） |

### 商家 VLOG（5）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `GET /api/Merchant/Files` | 取得商家已生成的檔案清單 | 需要 | ✅ 實測可用 |  |
| `POST /api/MerchantVlog/CreateFinal` | 商家確認旁白後，把專案照片打包送去合成影片 | 需要 | ✅ 實測可用 | JSON：mm_id（integer）、final_script（string）、caption（string）、hashtags（string[]） |
| `POST /api/MerchantVlog/Preview` | 輸入店家資訊與照片，產生 AI 旁白草稿、推薦配文、TAG | 需要 | ⚠️ 卡在 AI 服務 | 表單：mm_id（integer）、store_name（string）、open_time（string）、address（string）、nt_id（integer）、promo_text（string）、images（檔案[]） |
| `GET /api/MerchantVlog/Status/{mm_id}` | 查詢商家 VLOG 專案：影片、推薦配文、TAG | 需要 | ✅ 實測可用 | mm_id（路徑，integer） |
| `GET /api/MerchantVlog/Tones` | 取得可選的敘事語氣（例如幽默詼諧、質感專業、溫情走心） | 需要 | ✅ 實測可用 |  |

### 商家後台與優惠券（13）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/coupons/{couponId}/redeem` | 核銷優惠券 | 需要 | ✅ 實測可用 | couponId（路徑，integer） |
| `GET /api/merchant/coupons` | 查詢登入商家的優惠券列表 | 需要 | ✅ 實測可用 |  |
| `POST /api/merchant/coupons` | 新增優惠券 | 需要 | ✅ 實測可用 | JSON：coupon_code（string）、coupon_name（string）、discount_commodity（string）、discount_type（string）、discount_value（number）、val |
| `DELETE /api/merchant/coupons/{couponId}` | 刪除優惠券 | 需要 | ✅ 實測可用 | couponId（路徑，integer） |
| `GET /api/merchant/coupons/{couponId}` | 查詢單筆優惠券 | 需要 | ✅ 實測可用 | couponId（路徑，integer） |
| `PUT /api/merchant/coupons/{couponId}` | 修改優惠券 | 需要 | ✅ 實測可用 | couponId（路徑，integer）；JSON：coupon_code（string）、coupon_name（string）、discount_commodity（string）、discount_type（string）、disco |
| `PATCH /api/merchant/coupons/{couponId}/status` | 上下架優惠券 | 需要 | ✅ 實測可用 | couponId（路徑，integer）；JSON：status（string） |
| `POST /api/merchant/qrcode/bind` | 綁定 QR Code 與優惠券 | 需要 | ✅ 實測可用 | JSON：qr_uid（string）、coupon_id（integer） |
| `GET /api/merchant/questions` | 查詢登入商家的題庫列表 | 需要 | ✅ 實測可用 |  |
| `POST /api/merchant/questions` | 新增題目 | 需要 | ✅ 實測可用 | JSON：question_describe（string）、options（QuestionOptionInput[]） |
| `DELETE /api/merchant/questions/{questionId}` | 刪除題目 | 需要 | ✅ 實測可用 | questionId（路徑，integer） |
| `PUT /api/merchant/questions/{questionId}` | 修改題目與選項 | 需要 | ✅ 實測可用 | questionId（路徑，integer）；JSON：question_describe（string）、options（QuestionOptionInput[]） |
| `GET /api/qrcode/scan/{qrUid}` | 掃描 QR Code 查詢商家與優惠券資訊 | 不用 | ⬜ 未實測 | qrUid（路徑，string） |

### 管理員（4）

| API | 名稱 | 登入 | 狀態 | 參數 |
|---|---|---|---|---|
| `POST /api/Bus/PlaceStops/Build` | 重算景點附近的公車站牌（生成劇本規劃公車用） | 需要 | 🛠 管理員用 | scope（查詢，string） |
| `POST /api/Bus/Sync/City/{city}` | 從 TDX 同步某縣市的市區公車 | 需要 | 🛠 管理員用 | city（路徑，string） |
| `POST /api/Bus/Sync/TaiwanTrip` | 從 TDX 同步全台台灣好行 | 需要 | 🛠 管理員用 |  |
| `POST /api/Metro/Sync/{system}` | 從 TDX 同步一個捷運系統 | 需要 | 🛠 管理員用 | system（路徑，string） |

## 每支 API 的 JSON

外層一律是 `{ "isSuccess": true, "message": "說明", "Result": … }`，下面的「回傳」是 `Result` 的內容。

### 帳號

#### `POST /api/Auth/ChangePassword`　修改密碼

參數：JSON：OldPassword（string）、NewPassword（string）

Request：

```json
{
  "OldPassword": "string",
  "NewPassword": "string"
}
```

回傳：

```json
"string"
```

#### `POST /api/Auth/ForgotPassword`　忘記密碼

參數：JSON：Email（string）

Request：

```json
{
  "Email": "string"
}
```

回傳：

```json
"string"
```

#### `POST /api/Auth/Login`　帳號登入

參數：JSON：auth_name（string）、auth_pswd（string）

Request：

```json
{
  "auth_name": "string",
  "auth_pswd": "string"
}
```

回傳：

```json
{
  "token": "string",
  "au_id": 0,
  "auth_name": "string",
  "auth_type": 0,
  "account_type_name": "string",
  "s_id": 0
}
```

#### `POST /api/Auth/Logout`　帳號登出

回傳：

```json
"string"
```

#### `GET /api/Auth/Profile`　取得目前登入帳號的資訊

回傳：

```json
{
  "token": "string",
  "au_id": 0,
  "auth_name": "string",
  "auth_type": 0,
  "account_type_name": "string",
  "s_id": 0
}
```

#### `POST /api/Auth/Profile`　更新目前登入帳號的名稱

參數：JSON：auth_name（string）

Request：

```json
{
  "auth_name": "string"
}
```

回傳：

```json
"string"
```

#### `POST /api/Auth/Register`　帳號註冊

參數：JSON：Username（string）、Email（string）、Password（string）、Birthday（string）、Gender（string）、AccountType（integer）、ResolvedAccountType（integer）

Request：

```json
{
  "Username": "string",
  "Email": "string",
  "Password": "string",
  "Birthday": "2026-10-07T12:00:00",
  "Gender": "string",
  "AccountType": 0,
  "ResolvedAccountType": 0
}
```

回傳：

```json
"string"
```

#### `POST /api/Auth/ResetPassword`　依重設連結 Token 完成密碼重設

參數：JSON：Token（string）、NewPassword（string）

Request：

```json
{
  "Token": "string",
  "NewPassword": "string"
}
```

回傳：

```json
"string"
```

#### `GET /api/Auth/VerifyEmail`　信箱驗證與啟用帳號

參數：token（查詢，string）

回傳：（回傳 HTML 頁面，不是 JSON：使用者點信裡的連結後直接在瀏覽器顯示）

### 首頁

#### `GET /api/Home/Overview`　取得首頁目前總覽資訊

回傳：

```json
{
  "ho_completed_story": 0,
  "ho_postcard_count": 0,
  "ho_badge_count": 0,
  "ho_vlog_count": 0,
  "ho_recent_cards": [
    {
      "hc_id": 0,
      "hc_type": "string",
      "hc_title": "string",
      "hc_image": "string"
    }
  ]
}
```

### 劇本生成

#### `POST /api/Story/GenerateByText`　接收使用者輸入的一句自然語言描述，由外部 AI 服務自動解析出城市/行政區/人數/偏好等條件， 並直接生成完整劇本、寫入

參數：JSON：user_prompt（string）

Request：

```json
{
  "user_prompt": "string"
}
```

回傳：

```json
{
  "story_id": 0,
  "detected_city": "string",
  "detected_town": "string",
  "parsed_intent": {
    "city_name": "string",
    "town_name": "string",
    "traveler_count": 0,
    "preferences": [
      "string"
    ],
    "transportation": [
      "string"
    ],
    "node_count": 0,
    "is_night": false
  },
  "data": {
    "title": "string",
    "preface": "string",
    "synopsis": "string",
    "is_night_mode": false,
    "npc": {
      "name": "string",
      "role": "string",
      "intro": "string"
    },
    "nodes": [
      {
        "node_order": 0,
        "place_name": "string",
        "location_codename": "string",
        "node_title": "string",
        "task_type": "string",
        "task_description": "string",
        "dialogues": {
          "opening": "string",
          "success": "string"
        }
      }
    ]
  }
}
```

#### `POST /api/Story/GenerateGameStory`　依使用者位置與交通方式自動挑景點，由 AI 一次生成 3 份劇本（含每個景點的任務）讓使用者挑，並存入資料庫

參數：JSON：lat（number）、lng（number）、city_name（string）、town_name（string）、party_size（integer）、transportation（string[]）、preferences（string[]）、is_night_mode（integer）

Request：

```json
{
  "lat": 0.0,
  "lng": 0.0,
  "city_name": "string",
  "town_name": "string",
  "party_size": 0,
  "transportation": [
    "string"
  ],
  "preferences": [
    "string"
  ],
  "is_night_mode": 0
}
```

回傳：

```json
[
  {
    "story_id": 0,
    "story_no": 0,
    "nt_name": "string",
    "story": {
      "city_name": "string",
      "district_name": "string",
      "party_size": 0,
      "sd_transport": [
        "string"
      ],
      "story_title": "string",
      "story_prologue": "string",
      "story_synopsis": "string",
      "story_badge": [
        "string"
      ],
      "story_postcards": 0,
      "is_night_mode": 0,
      "npc": {
        "npc_id": 0,
        "npc_name": "string",
        "npc_role": "string",
        "npc_intro": "string",
        "npc_avatar_url": "string",
        "npc_voice": "string"
      }
    },
    "nodes": [
      {
        "sn_id": 0,
        "place_id": "string",
        "p_name": "string",
        "npc_id": 0,
        "sn_order": 0,
        "sn_title": "string",
        "location_codename": "string",
        "image_url": "string",
        "sn_task_type": "string",
        "is_hidden": 0,
        "is_night_only": 0,
        "sn_opening_text": "string",
        "sn_success_text": "string",
        "tasks": [
          {
            "task_id": 0,
            "task_type": 0,
            "type_name": "string",
            "question_id": 0,
            "task_describe": "string",
            "task_hint": "string",
            "correct_answer": "string",
            "task_option": [
              {}
            ],
            "task_clue": [
              {}
            ]
          }
        ],
        "transit": [
          {
            "from_sn_id": 0,
            "to_sn_id": 0,
            "leg_order": 0,
            "route_name": "string",
            "sub_route_name": "string",
            "route_type": 0,
            "route_type_name": "string",
            "tripper_theme": "string",
            "headsign": "string",
            "board_stop_name": "string",
            "alight_stop_name": "string",
            "stop_count": 0,
            "est_ride_minutes": 0.0,
            "stops": [
              {}
            ],
            "coordinates": [
              [
                0.0
              ]
            ]
          }
        ]
      }
    ]
  }
]
```

#### `POST /api/Story/spin`　接收前端傳入的語音文字、情緒標籤與城市/行政區， 後端先將城市/行政區轉換為經緯度，再透過 AI Agent 服務即時推

參數：JSON：input_text（string）、emotion_label（string）、city_name（string）、town_name（string）

Request：

```json
{
  "input_text": "string",
  "emotion_label": "string",
  "city_name": "string",
  "town_name": "string"
}
```

回傳：

```json
{
  "recommendation_id": "string",
  "agent_result": {
    "phase_1_perception": {
      "voice_input": "string",
      "emotion_detected": "string"
    },
    "phase_2_cognition": {
      "agent_thought_process": "string",
      "extracted_tags": [
        "string"
      ]
    },
    "phase_3_graph_rag": {
      "recommended_spot": {
        "name": "string",
        "address": "string",
        "description": "string",
        "tags": [
          "string"
        ],
        "distance_m": 0.0
      }
    },
    "phase_4_action_and_tools": {
      "script_blueprint": {
        "theme_title": "string",
        "npc_dialogue": "string",
        "task_mission": "string",
        "preparation_tips": [
          "string"
        ]
      },
      "tool_1_calendar_sync": "string",
      "tool_2_social_share": "string"
    },
    "phase_5_next_step": "string"
  }
}
```

### 劇本

#### `POST /api/Story/Confirm`　玩家確認選擇指定的劇本卷，準備進入探索地圖。會將此劇本標記為「正在遊玩中」（is_playing = 1）， 並自動把其

參數：JSON：story_id（integer）

Request：

```json
{
  "story_id": 0
}
```

回傳：

```json
{
  "story_id": 0,
  "title": "string",
  "preface": "string",
  "synopsis": "string",
  "is_night_mode": false,
  "is_favorite": false,
  "npc": {
    "name": "string",
    "role": "string",
    "intro": "string",
    "avatar_url": "string",
    "voice": "string"
  },
  "nodes": [
    {
      "order": 0,
      "place_name": "string",
      "task_description": "string",
      "opening_dialogue": "string",
      "location_codename": "string",
      "opening": "string",
      "success": "string",
      "npc_name": "string",
      "task_type": "string",
      "image_url": "string"
    }
  ],
  "subtitle": "string",
  "route_nodes": [
    {
      "node_id": 0,
      "location_name": "string",
      "node_order": 0
    }
  ]
}
```

#### `GET /api/Story/CurrentPlaying`　查詢目前正在進行中的劇本是哪一個（is_playing = 1 的那筆）

回傳：

```json
{
  "story_id": 0,
  "title": "string",
  "current_order": 0
}
```

#### `POST /api/Story/EndStory`　玩家完成或退出劇本時呼叫，把自己在這份劇本的遊玩紀錄（story_session）標成完成

參數：JSON：story_id（integer）

Request：

```json
{
  "story_id": 0
}
```

回傳：

```json
"string"
```

#### `GET /api/Story/Favorites`　我喜愛的劇本清單（最新建立的在前面）

回傳：

```json
[
  {
    "story_id": 0,
    "title": "string",
    "synopsis": "string",
    "city_name": "string",
    "district_name": "string",
    "node_count": 0,
    "is_night_mode": false,
    "cover_image_url": "string",
    "is_completed": false,
    "created_at": "2026-10-07T12:00:00"
  }
]
```

#### `POST /api/Story/{story_id}/Calendar`　把劇本加入 Google 行事曆：回傳 Google 行事曆的新增活動連結

參數：story_id（路徑，integer）；JSON：start_time（string）、duration_minutes（integer）

Request：

```json
{
  "start_time": "2026-10-07T12:00:00",
  "duration_minutes": 0
}
```

回傳：

```json
{
  "google_calendar_url": "string",
  "title": "string",
  "start_time": "2026-10-07T12:00:00",
  "end_time": "2026-10-07T12:00:00",
  "location": "string",
  "details": "string"
}
```

#### `GET /api/Story/{story_id}/Detail`　取得指定劇本的詳細內容（含各節點地點名稱、任務提示、對應 NPC）

參數：story_id（路徑，integer）

回傳：

```json
{
  "story_id": 0,
  "title": "string",
  "preface": "string",
  "synopsis": "string",
  "is_night_mode": false,
  "is_favorite": false,
  "npc": {
    "name": "string",
    "role": "string",
    "intro": "string",
    "avatar_url": "string",
    "voice": "string"
  },
  "nodes": [
    {
      "order": 0,
      "place_name": "string",
      "task_description": "string",
      "opening_dialogue": "string",
      "location_codename": "string",
      "opening": "string",
      "success": "string",
      "npc_name": "string",
      "task_type": "string",
      "image_url": "string"
    }
  ],
  "subtitle": "string",
  "route_nodes": [
    {
      "node_id": 0,
      "location_name": "string",
      "node_order": 0
    }
  ]
}
```

#### `POST /api/Story/{story_id}/Favorite`　把劇本加入或取消喜愛

參數：story_id（路徑，integer）；JSON：is_favorite（boolean）

Request：

```json
{
  "is_favorite": false
}
```

回傳：

```json
null
```

### 組隊

#### `POST /api/Pair/Cancel`　取消協作隊伍（限劇本擁有者）

參數：JSON：story_id（integer）

Request：

```json
{
  "story_id": 0
}
```

回傳：null（Result 沒有內容，看 isSuccess 與 message 即可）

#### `POST /api/Pair/Code`　產生配對碼（限劇本擁有者）

參數：JSON：story_id（integer）

Request：

```json
{
  "story_id": 0
}
```

回傳：

```json
{
  "pair_id": 0,
  "story_id": 0,
  "story_title": "string",
  "pair_code": "string",
  "pair_status": "string",
  "expires_at": "2026-10-07T12:00:00",
  "expires_in_seconds": 0,
  "party_size": 0,
  "member_count": 0,
  "my_seat_no": 0,
  "is_host": false,
  "members": [
    {
      "au_id": 0,
      "auth_name": "string",
      "seat_no": 0,
      "is_host": false,
      "joined_at": "2026-10-07T12:00:00"
    }
  ]
}
```

#### `POST /api/Pair/Join`　輸入配對碼加入協作隊伍

參數：JSON：pair_code（string）

Request：

```json
{
  "pair_code": "string"
}
```

回傳：

```json
{
  "pair_id": 0,
  "story_id": 0,
  "story_title": "string",
  "pair_code": "string",
  "pair_status": "string",
  "expires_at": "2026-10-07T12:00:00",
  "expires_in_seconds": 0,
  "party_size": 0,
  "member_count": 0,
  "my_seat_no": 0,
  "is_host": false,
  "members": [
    {
      "au_id": 0,
      "auth_name": "string",
      "seat_no": 0,
      "is_host": false,
      "joined_at": "2026-10-07T12:00:00"
    }
  ]
}
```

#### `POST /api/Pair/Leave`　隊員退出協作隊伍

參數：JSON：story_id（integer）

Request：

```json
{
  "story_id": 0
}
```

回傳：null（Result 沒有內容，看 isSuccess 與 message 即可）

#### `GET /api/Pair/Story/{story_id}`　查詢劇本目前的協作隊伍（限擁有者與隊員）

參數：story_id（路徑，integer）

回傳：

```json
{
  "pair_id": 0,
  "story_id": 0,
  "story_title": "string",
  "pair_code": "string",
  "pair_status": "string",
  "expires_at": "2026-10-07T12:00:00",
  "expires_in_seconds": 0,
  "party_size": 0,
  "member_count": 0,
  "my_seat_no": 0,
  "is_host": false,
  "members": [
    {
      "au_id": 0,
      "auth_name": "string",
      "seat_no": 0,
      "is_host": false,
      "joined_at": "2026-10-07T12:00:00"
    }
  ]
}
```

### NPC 語音

#### `POST /api/Npc/Prologue/{story_id}`　劇情前傳語音：唸指定劇本的前傳

參數：story_id（路徑，integer）；voice（查詢，string）

回傳：

```json
{
  "text": "string",
  "voice": "string",
  "audio_url": "string"
}
```

#### `POST /api/Npc/Speak`　NPC 說話：任意文字轉語音（節點對話、任務說明等）

參數：JSON：text（string）、voice（string）

Request：

```json
{
  "text": "string",
  "voice": "string"
}
```

回傳：

```json
{
  "text": "string",
  "voice": "string",
  "audio_url": "string"
}
```

### 解謎地圖

#### `POST /api/Map/Location`　取得使用者現在所在位置

參數：JSON：lat（number）、lng（number）、accuracy（number）

Request：

```json
{
  "lat": 0.0,
  "lng": 0.0,
  "accuracy": 0.0
}
```

回傳：

```json
{
  "lat": 0.0,
  "lng": 0.0,
  "accuracy": 0.0,
  "city_name": "string",
  "district_name": "string",
  "received_at": "2026-10-07T12:00:00"
}
```

#### `POST /api/Map/Navigate`　取得前往指定節點的導航資訊

參數：JSON：node_id（integer）

Request：

```json
{
  "node_id": 0
}
```

回傳：

```json
{
  "maps_deeplink_url": "string"
}
```

#### `GET /api/Map/Node/{node_id}`　取得指定節點的詳細內容

參數：node_id（路徑，integer）

回傳：

```json
{
  "node_id": 0,
  "location_name": "string",
  "npc_name": "string",
  "npc_avatar_url": "string",
  "intro_story": "string",
  "opening_hours": "string",
  "nearby_food": [
    "string"
  ],
  "task_id": 0
}
```

#### `POST /api/Map/Node/{node_id}/Arrive`　使用 GPS 座標確認探員已抵達指定節點

參數：node_id（路徑，integer）；lat（查詢，number）；lng（查詢，number）

回傳：

```json
{
  "node_id": 0,
  "location_name": "string",
  "npc_name": "string",
  "npc_avatar_url": "string",
  "intro_story": "string",
  "opening_hours": "string",
  "nearby_food": [
    "string"
  ],
  "task_id": 0
}
```

#### `GET /api/Map/Node/{node_id}/Interact`　取得指定節點的 NPC 隨機互動內容

參數：node_id（路徑，integer）

回傳：

```json
{
  "node_id": 0,
  "location_name": "string",
  "location_subtitle": "string",
  "scene_image_url": "string",
  "npc_id": "string",
  "npc_name": "string",
  "npc_role": "string",
  "npc_avatar_url": "string",
  "npc_voice": "string",
  "npc_dialogue": "string",
  "emotion": "string",
  "skip_button_text": "string",
  "next_task_id": "string"
}
```

#### `GET /api/Map/{story_id}`　取得指定劇本的地圖資訊

參數：story_id（路徑，integer）

回傳：

```json
{
  "story_id": 0,
  "unlocked_node_count": 0,
  "total_node_count": 0,
  "postcard_unlocked_count": 0,
  "postcard_total_count": 0,
  "nodes": [
    {
      "node_id": 0,
      "location_name": "string",
      "lat": 0.0,
      "lng": 0.0,
      "is_unlocked": false,
      "is_completed": false,
      "is_night_only": false,
      "fog_hint": "string",
      "fog_radius_m": 0.0,
      "day_index": 0,
      "child_node_ids": [
        0
      ],
      "image_url": "string",
      "node_order": 0
    }
  ],
  "day_index": 0,
  "total_days": 0
}
```

#### `GET /api/Map/{story_id}/Nearby`　取得指定劇本周邊的推薦去處

參數：story_id（路徑，integer）；category（查詢，string）

回傳：

```json
[
  {
    "place_id": 0,
    "category": "string",
    "type": "string",
    "name": "string",
    "address": "string",
    "open_time": "string",
    "photo_urls": [
      "string"
    ],
    "maps_deeplink_url": "string",
    "lat": 0.0,
    "lng": 0.0
  }
]
```

### 交通與景點

#### `GET /api/Bus/Nearby`　查詢座標附近的公車站牌，以及每個站牌經過的路線（含台灣好行）

參數：lat（查詢，number）；lng（查詢，number）；radius（查詢，integer）

回傳：

```json
[
  {
    "bs_id": 0,
    "stop_name": "string",
    "lat": 0.0,
    "lng": 0.0,
    "distance_m": 0,
    "routes": [
      {
        "br_id": 0,
        "route_name": "string",
        "route_type": 0,
        "route_type_name": "string"
      }
    ]
  }
]
```

#### `GET /api/Bus/Route/{br_id}`　取得公車路線詳情：去程/返程的完整站牌、路線線形、起站發車時間

參數：br_id（路徑，integer）

回傳：

```json
{
  "br_id": 0,
  "route_uid": "string",
  "route_name": "string",
  "route_type": 0,
  "route_type_name": "string",
  "city_name": "string",
  "departure_stop_name": "string",
  "destination_stop_name": "string",
  "fare_desc": "string",
  "route_map_url": "string",
  "headway_desc": "string",
  "tripper": {
    "theme": "string",
    "introduction": "string",
    "cover_image": "string",
    "website_url": "string"
  },
  "patterns": [
    {
      "brp_id": 0,
      "sub_route_name": "string",
      "direction": 0,
      "direction_name": "string",
      "headsign": "string",
      "stops": [
        {
          "bs_id": 0,
          "stop_sequence": 0,
          "stop_name": "string",
          "lat": 0.0,
          "lng": 0.0
        }
      ],
      "shape": [
        [
          0.0
        ]
      ],
      "departures": [
        {
          "service_days": "string",
          "times": [
            "string"
          ]
        }
      ]
    }
  ]
}
```

#### `GET /api/Bus/Stop/{bs_id}/Arrivals`　查詢站牌的即時到站時間（每條路線還有幾分鐘到）

參數：bs_id（路徑，integer）

回傳：

```json
[
  {
    "route_name": "string",
    "sub_route_name": "string",
    "direction": 0,
    "estimate_minutes": 0,
    "status_text": "string",
    "next_bus_time": "string"
  }
]
```

#### `GET /api/Bus/TaiwanTrip`　取得全台營運中的台灣好行路線列表

回傳：

```json
[
  {
    "br_id": 0,
    "route_name": "string",
    "city_name": "string",
    "departure_stop_name": "string",
    "destination_stop_name": "string",
    "fare_desc": "string",
    "theme": "string",
    "cover_image": "string",
    "stop_count": 0
  }
]
```

#### `GET /api/Metro/Lines`　取得座標附近的捷運路線（含車站與線形），在地圖上畫捷運路網用

參數：lat（查詢，number）；lng（查詢，number）；radius_km（查詢，number）

回傳：

```json
[
  {
    "ml_id": 0,
    "system_name": "string",
    "line_no": "string",
    "line_name": "string",
    "line_color": "string",
    "stations": [
      {
        "mst_id": 0,
        "station_code": "string",
        "station_name": "string",
        "lat": 0.0,
        "lng": 0.0
      }
    ],
    "shape": [
      [
        [
          0.0
        ]
      ]
    ]
  }
]
```

#### `GET /api/Route/Availability`　查詢各交通方式在這一帶能不能用（前端用來決定勾選框要不要反灰）

參數：lat（查詢，number）；lng（查詢，number）；story_id（查詢，integer）

回傳：

```json
[
  {
    "mode": "string",
    "enabled": false,
    "reason": "string",
    "nearest_name": "string",
    "nearest_distance_m": 0,
    "color": "string"
  }
]
```

#### `POST /api/Route/Availability`　查詢各交通方式在這些地點能不能用（自選地點、沒有劇本時用）

Request：

```json
[
  {
    "lat": 0.0,
    "lng": 0.0,
    "name": "string"
  }
]
```

回傳：

```json
[
  {
    "mode": "string",
    "enabled": false,
    "reason": "string",
    "nearest_name": "string",
    "nearest_distance_m": 0,
    "color": "string"
  }
]
```

#### `POST /api/Route/Plan`　規劃劇本交通路線：起點 → 依序經過每個節點（去程）→ 回到起點（返程）

參數：JSON：story_id（integer）、start（RoutePoint）、points（RoutePoint[]）、transportation（string[]）、include_return（boolean）

Request：

```json
{
  "story_id": 0,
  "start": {
    "lat": 0.0,
    "lng": 0.0,
    "name": "string"
  },
  "points": [
    {
      "lat": 0.0,
      "lng": 0.0,
      "name": "string"
    }
  ],
  "transportation": [
    "string"
  ],
  "include_return": false
}
```

回傳：

```json
{
  "story_id": 0,
  "story_title": "string",
  "start": {
    "lat": 0.0,
    "lng": 0.0,
    "name": "string"
  },
  "stops": [
    {
      "sn_id": 0,
      "order": 0,
      "name": "string",
      "title": "string",
      "lat": 0.0,
      "lng": 0.0
    }
  ],
  "legs": [
    {
      "leg_order": 0,
      "direction": "string",
      "from_name": "string",
      "to_name": "string",
      "mode": "string",
      "minutes": 0.0,
      "distance_km": 0.0,
      "summary": "string",
      "segments": [
        {
          "mode": "string",
          "color": "string",
          "minutes": 0.0,
          "wait_minutes": 0.0,
          "distance_km": 0.0,
          "coordinates": [
            [
              0.0
            ]
          ],
          "route_name": "string",
          "route_type_name": "string",
          "headsign": "string",
          "board_name": "string",
          "alight_name": "string",
          "stop_count": 0,
          "stops": [
            {}
          ],
          "instruction": "string"
        }
      ],
      "options": [
        {
          "mode": "string",
          "minutes": 0.0,
          "distance_km": 0.0,
          "summary": "string",
          "is_chosen": false,
          "segments": [
            {}
          ]
        }
      ]
    }
  ],
  "outbound_minutes": 0.0,
  "return_minutes": 0.0,
  "total_minutes": 0.0,
  "total_distance_km": 0.0,
  "modes_used": [
    "string"
  ],
  "warnings": [
    "string"
  ]
}
```

#### `GET /api/Route/Stories`　取得可以規劃交通路線的劇本（含節點座標與建議起點）

回傳：

```json
[
  {
    "story_id": 0,
    "story_title": "string",
    "city_name": "string",
    "district_name": "string",
    "node_count": 0,
    "suggested_start": {
      "lat": 0.0,
      "lng": 0.0,
      "name": "string"
    },
    "nodes": [
      {
        "sn_id": 0,
        "order": 0,
        "name": "string",
        "title": "string",
        "lat": 0.0,
        "lng": 0.0
      }
    ]
  }
]
```

#### `GET /api/Story/NearbyAttractions`　依使用者 GPS 座標，透過 Neo4j 查詢半徑範圍內的景點，依距離由近到遠排序

參數：lat（查詢，number）；lng（查詢，number）；radiusKm（查詢，number）

回傳：

```json
[
  {
    "name": "string",
    "lat": 0.0,
    "lon": 0.0,
    "distance_m": 0.0
  }
]
```

#### `POST /api/Story/ReachableAttractions`　依中心點與交通方式，找出可到達的景點，並回傳每個景點的真實交通時間、距離、所在圈層

參數：JSON：lat（number）、lng（number）、city_name（string）、town_name（string）、transportation（string[]）、contour_minutes（integer[]）、include_polygons（boolean）

Request：

```json
{
  "lat": 0.0,
  "lng": 0.0,
  "city_name": "string",
  "town_name": "string",
  "transportation": [
    "string"
  ],
  "contour_minutes": [
    0
  ],
  "include_polygons": false
}
```

回傳：

```json
{
  "center_lat": 0.0,
  "center_lng": 0.0,
  "contour_minutes": [
    0
  ],
  "total_reachable": 0,
  "attractions": [
    {
      "uid": "string",
      "name": "string",
      "lat": 0.0,
      "lon": 0.0,
      "distance_m": 0.0,
      "travel_minutes": 0.0,
      "travel_distance_km": 0.0,
      "reachable_by": "string",
      "ring": 0,
      "reachable_minutes": 0
    }
  ],
  "isochrones": [
    {
      "transport": "string",
      "costing": "string",
      "minutes": 0,
      "polygons": [
        [
          [
            [
              0.0
            ]
          ]
        ]
      ]
    }
  ]
}
```

### 任務

#### `POST /api/Task/Answer`　送出任務答案並取得答題結果

參數：JSON：task_id（integer）、gps_lon（number）、gps_lat（number）、selected_option_key（string）、text_answer（string）、photo_url（string）、video_url（string）、audio_url（string）

Request：

```json
{
  "task_id": 0,
  "gps_lon": 0.0,
  "gps_lat": 0.0,
  "selected_option_key": "string",
  "text_answer": "string",
  "photo_url": "string",
  "video_url": "string",
  "audio_url": "string"
}
```

回傳：

```json
{
  "is_correct": false,
  "pass": 0,
  "is_pending_review": false,
  "feedback_message": "string",
  "node_progress": {
    "story_id": 0,
    "node_order": 0,
    "current_order": 0
  },
  "node_completed": false,
  "story_completed": false
}
```

#### `POST /api/Task/Generate`　測試用：直接對指定劇本或節點逐題呼叫 AI 生成任務

參數：JSON：story_id（string）、node_id（string）、player_count（integer）

Request：

```json
{
  "story_id": "string",
  "node_id": "string",
  "player_count": 0
}
```

回傳：

```json
[
  {
    "task_id": 0,
    "story_id": "string",
    "node_id": "string",
    "task_place_id": "string",
    "type_id": 0,
    "task_type": "string",
    "task_describe": "string",
    "clue_text": "string",
    "pass": 0,
    "options": [
      {
        "option_key": "string",
        "option_text": "string",
        "option_url": "string"
      }
    ],
    "media_urls": [
      "string"
    ]
  }
]
```

#### `POST /api/Task/HiddenLevel/Check`　依玩家目前 GPS 座標檢查是否觸發隱藏關卡

參數：ep_id（查詢，string）；lat（查詢，number）；lng（查詢，number）；region_id（查詢，string）

回傳：

```json
{
  "triggered": false,
  "hidden_level_id": "string",
  "title": "string",
  "cultural_background": "string",
  "content": "string",
  "reward_badge_id": "string",
  "reward_postcard_id": "string"
}
```

#### `POST /api/Task/List`　取得節點的任務清單（已由 GET api/Task/Node/{node_id} 取代）

參數：JSON：node_id（string）、gps_lon（number）、gps_lat（number）

Request：

```json
{
  "node_id": "string",
  "gps_lon": 0.0,
  "gps_lat": 0.0
}
```

回傳：

```json
[
  {
    "task_id": 0,
    "story_id": "string",
    "node_id": "string",
    "task_place_id": "string",
    "type_id": 0,
    "task_type": "string",
    "task_describe": "string",
    "clue_text": "string",
    "pass": 0,
    "options": [
      {
        "option_key": "string",
        "option_text": "string",
        "option_url": "string"
      }
    ],
    "media_urls": [
      "string"
    ]
  }
]
```

#### `GET /api/Task/Node/{node_id}`　節點遊玩畫面：一次取得這一站的劇情、自己的座位、進度與每一題的作答方式與狀態

參數：node_id（路徑，integer）

回傳：

```json
{
  "node": {
    "node_id": 0,
    "story_id": 0,
    "node_order": 0,
    "title": "string",
    "location_codename": "string",
    "opening_text": "string",
    "success_text": "string"
  },
  "npc": {
    "npc_id": 0,
    "npc_name": "string",
    "npc_role": "string",
    "npc_avatar_url": "string",
    "npc_voice": "string"
  },
  "my_seat_no": 0,
  "progress": {
    "passed": 0,
    "total": 0,
    "all_passed": false
  },
  "tasks": [
    {
      "task_id": 0,
      "type_id": 0,
      "type_name": "string",
      "answer_mode": "string",
      "task_describe": "string",
      "clue_text": "string",
      "options": [
        {
          "option_key": "string",
          "option_text": "string",
          "option_url": "string"
        }
      ],
      "pass": 0,
      "wrong_count": 0,
      "hint_available": false
    }
  ]
}
```

#### `GET /api/Task/Progress`　取得登入者在目前所在鄉鎮市區的題型解鎖進度（產生劇本前顯示用）

參數：lat（查詢，number）；lng（查詢，number）

回傳：

```json
{
  "city_name": "string",
  "district_name": "string",
  "is_first_visit": false,
  "unlocked_types": [
    {
      "type_id": 0,
      "type_name": "string",
      "unlock_hint": "string"
    }
  ],
  "locked_types": [
    {
      "type_id": 0,
      "type_name": "string",
      "unlock_hint": "string"
    }
  ]
}
```

#### `GET /api/Task/{task_id}/Hint`　取得指定任務的提示內容

參數：task_id（路徑，integer）

回傳：

```json
{
  "task_id": "string",
  "npc_name": "string",
  "npc_avatar_url": "string",
  "hint_text": "string",
  "is_available": false
}
```

### 上傳

#### `POST /api/Upload`　上傳檔案（照片、影片、錄音），回傳檔案網址

參數：表單：file（檔案）

回傳：

```json
"string"
```

### 明信片

#### `GET /api/PostcardCatalog`　取得所有明信片主檔清單

回傳：

```json
[
  {
    "PostcardId": 0,
    "StoryId": 0,
    "NodeId": 0,
    "PostcardName": "string",
    "Summary": "string",
    "ImageUrl": "string",
    "IsNightEdition": false,
    "CreatedAt": "2026-10-07T12:00:00",
    "UpdatedAt": "2026-10-07T12:00:00"
  }
]
```

#### `POST /api/PostcardCatalog/GenerateAi`　呼叫外部服務生成 AI 明信片並存入資料庫

參數：表單：user_image（檔案）、spot_name（string）、user_prompt（string）、story_id（integer）、node_id（integer）、is_night_edition（boolean）

回傳：

```json
{
  "p_id": 0,
  "au_id": 0,
  "s_id": 0,
  "sn_id": 0,
  "p_name": "string",
  "p_summary": "string",
  "p_imag_url": "string",
  "is_night": 0,
  "created_at": "2026-10-07T12:00:00",
  "updated_at": "2026-10-07T12:00:00"
}
```

#### `POST /api/PostcardCatalog/Print`　透過 postcard_id 送出明信片至 ibon 列印，取得取件碼

參數：JSON：postcard_id（integer）

Request：

```json
{
  "postcard_id": 0
}
```

回傳：

```json
{
  "ibon_pickup_code": "string",
  "pdf_url": "string",
  "deadline": "string",
  "qrcode_base64": "string"
}
```

#### `POST /api/PostcardCatalog/Share`　紀錄使用者已將該劇本的明信片分享至社群平台

參數：JSON：story_id（integer）、platform（string）

Request：

```json
{
  "story_id": 0,
  "platform": "string"
}
```

回傳：

```json
"string"
```

#### `GET /api/PostcardCatalog/by-story/{storyId}`　取得指定劇本 (story_id) 所擁有的「所有」明信片主檔

參數：storyId（路徑，integer）

回傳：

```json
[
  {
    "PostcardId": 0,
    "StoryId": 0,
    "NodeId": 0,
    "PostcardName": "string",
    "Summary": "string",
    "ImageUrl": "string",
    "IsNightEdition": false,
    "CreatedAt": "2026-10-07T12:00:00",
    "UpdatedAt": "2026-10-07T12:00:00"
  }
]
```

#### `GET /api/PostcardCatalog/{id}`　依明信片識別碼 (postcard_id) 取得單一明信片主檔資料

參數：id（路徑，integer）

回傳：

```json
{
  "PostcardId": 0,
  "StoryId": 0,
  "NodeId": 0,
  "PostcardName": "string",
  "Summary": "string",
  "ImageUrl": "string",
  "IsNightEdition": false,
  "CreatedAt": "2026-10-07T12:00:00",
  "UpdatedAt": "2026-10-07T12:00:00"
}
```

#### `POST /api/PostcardCatalog/{id}/Delete`　刪除明信片主檔

參數：id（路徑，integer）

回傳：

```json
false
```

#### `GET /api/PostcardCatalog/{id}/image`　依 postcard_id 取得指定明信片的圖片，轉址 (302 Redirect) 至實際圖片網址

參數：id（路徑，integer）

回傳：（302 轉址到圖片網址，不是 JSON：直接當 <img> 的網址）

### 徽章

#### `POST /api/Badge/Draw`　完成劇本後抽一枚勳章，一個劇本只能抽一次

參數：JSON：story_id（integer）

Request：

```json
{
  "story_id": 0
}
```

回傳：

```json
{
  "story_id": 0,
  "categories": [
    "string"
  ],
  "is_new": false,
  "badge": {
    "b_id": 0,
    "b_name": "string",
    "b_fication": "string",
    "b_thing": "string",
    "b_image": "string"
  }
}
```

#### `GET /api/Badge/Status`　取得系統所有徽章，依系列分組，並標示當前探員是否已擁有該徽章

回傳：

```json
[
  {
    "series_name": "string",
    "badges": [
      {
        "b_id": 0,
        "b_name": "string",
        "b_thing": "string",
        "b_image": "string",
        "is_owned": false,
        "obtained_at": "2026-10-07T12:00:00"
      }
    ]
  }
]
```

### 過往旅途

#### `GET /api/History`　取得目前探員的所有過往劇本清單

回傳：

```json
[
  {
    "story_id": 0,
    "title": "string",
    "synopsis": "string",
    "completed_date": "2026-10-07T12:00:00",
    "region": "string",
    "vlog_id": 0,
    "spots": [
      "string"
    ]
  }
]
```

#### `GET /api/History/{story_id}`　取得單一過往劇本的詳細內容 (包含所有經歷過的景點清單)

參數：story_id（路徑，integer）

回傳：

```json
{
  "story_id": 0,
  "title": "string",
  "synopsis": "string",
  "completed_date": "2026-10-07T12:00:00",
  "region": "string",
  "vlog_id": 0,
  "spots": [
    "string"
  ]
}
```

#### `GET /api/History/{story_id}/Recap`　劇本回顧：玩完的劇本一次拿到劇情、每一站、收穫、統計與旁白

參數：story_id（路徑，integer）

回傳：

```json
{
  "story_id": 0,
  "title": "string",
  "prologue": "string",
  "synopsis": "string",
  "city_name": "string",
  "district_name": "string",
  "is_night_mode": false,
  "started_at": "2026-10-07T12:00:00",
  "completed_at": "2026-10-07T12:00:00",
  "play_minutes": 0,
  "stats": {
    "node_count": 0,
    "tasks_answered": 0,
    "tasks_correct": 0,
    "photo_count": 0,
    "postcard_count": 0
  },
  "nodes": [
    {
      "node_id": 0,
      "order": 0,
      "chapter_title": "string",
      "location_codename": "string",
      "place_name": "string",
      "place_image_url": "string",
      "opening_text": "string",
      "success_text": "string",
      "photos": [
        "string"
      ],
      "tasks": [
        {
          "task_id": 0,
          "type_name": "string",
          "question": "string",
          "answered": false,
          "is_correct": false
        }
      ]
    }
  ],
  "postcards": [
    {
      "postcard_id": 0,
      "name": "string",
      "image_url": "string",
      "is_night_edition": false
    }
  ],
  "badge": {
    "badge_id": 0,
    "name": "string",
    "series": "string",
    "image_url": "string"
  },
  "vlog": {
    "vlog_id": 0,
    "status": 0,
    "video_url": "string",
    "thumbnail_url": "string"
  },
  "narration": {
    "source": "string",
    "text": "string",
    "audio_url": "string"
  }
}
```

#### `POST /api/History/{story_id}/Recap/Narration`　產生回顧旁白的語音（mp3），回傳 audio_url

參數：story_id（路徑，integer）；JSON：voice（string）

Request：

```json
{
  "voice": "string"
}
```

回傳：

```json
{
  "source": "string",
  "text": "string",
  "audio_url": "string"
}
```

### 遊客 VLOG

#### `POST /api/VisitorVlog/CreateFinal`　送出確認後的旁白，後端打包照片與景點資料送去合成影片

參數：JSON：story_id（integer）、final_script（string）、promo_copy（string）、seo_keywords（string[]）

Request：

```json
{
  "story_id": 0,
  "final_script": "string",
  "promo_copy": "string",
  "seo_keywords": [
    "string"
  ]
}
```

回傳：

```json
{
  "av_id": 0,
  "story_id": 0,
  "task_id": "string",
  "photo_count": 0,
  "status": 0,
  "status_text": "string"
}
```

#### `POST /api/VisitorVlog/Preview`　遊戲結束後，依 story_id 產生 VLOG 旁白草稿

參數：JSON：story_id（integer）

Request：

```json
{
  "story_id": 0
}
```

回傳：

```json
{
  "av_id": 0,
  "story_id": 0,
  "story_title": "string",
  "play_time": "string",
  "photo_count": 0,
  "spots": [
    {
      "order": 0,
      "spot_name": "string",
      "location_codename": "string",
      "node_title": "string",
      "address": "string",
      "lat": 0.0,
      "lng": 0.0,
      "visit_time": "string",
      "images": [
        "string"
      ],
      "photo_count": 0,
      "uses_place_photo": false
    }
  ],
  "script": "string",
  "promo_copy": "string",
  "seo_keywords": [
    "string"
  ],
  "itinerary": [
    {
      "spot_name": "string",
      "location_codename": "string",
      "visit_time": "string",
      "db_description": "string"
    }
  ]
}
```

#### `GET /api/VisitorVlog/Status/{story_id}`　查詢這個劇本的 VLOG（影片、旁白、宣傳文案）

參數：story_id（路徑，integer）；task_id（查詢，string）

回傳：

```json
{
  "av_id": 0,
  "story_id": 0,
  "status": 0,
  "status_text": "string",
  "title": "string",
  "final_script": "string",
  "promo_copy": "string",
  "seo_keywords": [
    "string"
  ],
  "video_url": "string",
  "thumbnail": "string",
  "error_message": "string",
  "updated_at": "2026-10-07T12:00:00"
}
```

### 商家帳號

#### `GET /api/Merchant/List`　查詢商家列表（限管理員）

參數：keyword（查詢，string）；page（查詢，integer）；page_size（查詢，integer）

回傳：

```json
{
  "items": [
    {
      "s_id": 0,
      "au_id": 0,
      "store_name": "string",
      "store_dec": "string",
      "store_address": "string",
      "store_city": "string",
      "store_town": "string",
      "store_uid": "string",
      "auth_name": "string",
      "auth_email": "string",
      "is_active": false
    }
  ],
  "total": 0,
  "page": 0,
  "page_size": 0
}
```

#### `POST /api/Merchant/Login`　商家登入，成功後回傳 JWT

參數：JSON：auth_name（string）、auth_pswd（string）

Request：

```json
{
  "auth_name": "string",
  "auth_pswd": "string"
}
```

回傳：

```json
{
  "token": "string",
  "au_id": 0,
  "auth_name": "string",
  "auth_type": 0,
  "account_type_name": "string",
  "s_id": 0
}
```

#### `GET /api/Merchant/Profile`　查詢登入商家的店家資料

回傳：

```json
{
  "s_id": 0,
  "au_id": 0,
  "store_name": "string",
  "store_dec": "string",
  "store_address": "string",
  "store_city": "string",
  "store_town": "string",
  "store_uid": "string",
  "auth_name": "string",
  "auth_email": "string",
  "is_active": false
}
```

#### `PUT /api/Merchant/Profile`　更新登入商家的店家資料

參數：JSON：store_name（string）、store_dec（string）、store_address（string）、store_city（string）、store_town（string）、lat（number）、lng（number）

Request：

```json
{
  "store_name": "string",
  "store_dec": "string",
  "store_address": "string",
  "store_city": "string",
  "store_town": "string",
  "lat": 0.0,
  "lng": 0.0
}
```

回傳：null（Result 沒有內容，看 isSuccess 與 message 即可）

#### `POST /api/Merchant/Register`　註冊商家

參數：JSON：auth_name（string）、auth_email（string）、auth_pswd（string）、store_name（string）、store_dec（string）、store_address（string）、store_city（string）、store_town（string）、place_uid（string）、new_place（MerchantNewPlace）

Request：

```json
{
  "auth_name": "string",
  "auth_email": "string",
  "auth_pswd": "string",
  "store_name": "string",
  "store_dec": "string",
  "store_address": "string",
  "store_city": "string",
  "store_town": "string",
  "place_uid": "string",
  "new_place": {
    "name": "string",
    "address": "string",
    "description": "string",
    "phone": "string",
    "website": "string",
    "opening_hours": "string",
    "lat": 0.0,
    "lng": 0.0,
    "category": "string"
  }
}
```

回傳：

```json
{
  "au_id": 0,
  "s_id": 0,
  "store_uid": "string"
}
```

#### `GET /api/merchant/register/address-to-location`　地址轉座標（自建景點表單自動帶出座標用）

參數：store_city（查詢，string）；store_town（查詢，string）；store_address（查詢，string）

回傳：

```json
{
  "store_city": "string",
  "store_town": "string",
  "store_address": "string",
  "full_address": "string",
  "lat": 0.0,
  "lng": 0.0
}
```

#### `GET /api/merchant/register/location-to-address`　座標轉地址（自建景點表單自動帶出地址用）

參數：lat（查詢，number）；lng（查詢，number）

回傳：

```json
{
  "store_city": "string",
  "store_town": "string",
  "store_address": "string",
  "full_address": "string",
  "lat": 0.0,
  "lng": 0.0
}
```

#### `GET /api/merchant/register/search-place`　搜尋既有景點

參數：keyword（查詢，string）

回傳：

```json
[
  {
    "uid": "string",
    "name": "string",
    "address": "string",
    "type": "string"
  }
]
```

### 商家 VLOG

#### `GET /api/Merchant/Files`　取得商家已生成的檔案清單

回傳：

```json
[
  {
    "mm_id": 0,
    "mm_title": "string",
    "mm_video_url": "string",
    "mm_status": 0,
    "updated_at": "2026-10-07T12:00:00"
  }
]
```

#### `POST /api/MerchantVlog/CreateFinal`　商家確認旁白後，把專案照片打包送去合成影片

參數：JSON：mm_id（integer）、final_script（string）、caption（string）、hashtags（string[]）

Request：

```json
{
  "mm_id": 0,
  "final_script": "string",
  "caption": "string",
  "hashtags": [
    "string"
  ]
}
```

回傳：

```json
{
  "mm_id": 0,
  "task_id": "string",
  "status": 0,
  "status_text": "string"
}
```

#### `POST /api/MerchantVlog/Preview`　輸入店家資訊與照片，產生 AI 旁白草稿、推薦配文、TAG

參數：表單：mm_id（integer）、store_name（string）、open_time（string）、address（string）、nt_id（integer）、promo_text（string）、images（檔案[]）

回傳：

```json
{
  "mm_id": 0,
  "store_name": "string",
  "open_time": "string",
  "address": "string",
  "tone_name": "string",
  "script": "string",
  "caption": "string",
  "hashtags": [
    "string"
  ],
  "target_audience": [
    "string"
  ],
  "image_urls": [
    "string"
  ]
}
```

#### `GET /api/MerchantVlog/Status/{mm_id}`　查詢商家 VLOG 專案：影片、推薦配文、TAG

參數：mm_id（路徑，integer）

回傳：

```json
{
  "mm_id": 0,
  "status": 0,
  "status_text": "string",
  "title": "string",
  "caption": "string",
  "hashtags": [
    "string"
  ],
  "video_url": "string",
  "thumbnail": "string",
  "error_message": "string",
  "updated_at": "2026-10-07T12:00:00"
}
```

#### `GET /api/MerchantVlog/Tones`　取得可選的敘事語氣（例如幽默詼諧、質感專業、溫情走心）

回傳：

```json
[
  {
    "nt_id": 0,
    "nt_name": "string",
    "nt_prompt": "string"
  }
]
```

### 商家後台與優惠券

#### `POST /api/coupons/{couponId}/redeem`　核銷優惠券

參數：couponId（路徑，integer）

回傳：null（Result 沒有內容，看 isSuccess 與 message 即可）

#### `GET /api/merchant/coupons`　查詢登入商家的優惠券列表

回傳：

```json
[
  {
    "coupon_id": 0,
    "s_id": 0,
    "coupon_code": "string",
    "coupon_name": "string",
    "discount_commodity": "string",
    "discount_type": "string",
    "discount_value": 0.0,
    "valid_from": "2026-10-07T12:00:00",
    "valid_to": "2026-10-07T12:00:00",
    "status": "string"
  }
]
```

#### `POST /api/merchant/coupons`　新增優惠券

參數：JSON：coupon_code（string）、coupon_name（string）、discount_commodity（string）、discount_type（string）、discount_value（number）、valid_from（string）、valid_to（string）

Request：

```json
{
  "coupon_code": "string",
  "coupon_name": "string",
  "discount_commodity": "string",
  "discount_type": "string",
  "discount_value": 0.0,
  "valid_from": "2026-10-07T12:00:00",
  "valid_to": "2026-10-07T12:00:00"
}
```

回傳：

```json
{
  "coupon_id": 0
}
```

#### `DELETE /api/merchant/coupons/{couponId}`　刪除優惠券

參數：couponId（路徑，integer）

#### `GET /api/merchant/coupons/{couponId}`　查詢單筆優惠券

參數：couponId（路徑，integer）

回傳：

```json
{
  "coupon_id": 0,
  "s_id": 0,
  "coupon_code": "string",
  "coupon_name": "string",
  "discount_commodity": "string",
  "discount_type": "string",
  "discount_value": 0.0,
  "valid_from": "2026-10-07T12:00:00",
  "valid_to": "2026-10-07T12:00:00",
  "status": "string"
}
```

#### `PUT /api/merchant/coupons/{couponId}`　修改優惠券

參數：couponId（路徑，integer）；JSON：coupon_code（string）、coupon_name（string）、discount_commodity（string）、discount_type（string）、discount_value（number）、valid_from（string）、valid_to（string）

Request：

```json
{
  "coupon_code": "string",
  "coupon_name": "string",
  "discount_commodity": "string",
  "discount_type": "string",
  "discount_value": 0.0,
  "valid_from": "2026-10-07T12:00:00",
  "valid_to": "2026-10-07T12:00:00"
}
```

回傳：null（Result 沒有內容，看 isSuccess 與 message 即可）

#### `PATCH /api/merchant/coupons/{couponId}/status`　上下架優惠券

參數：couponId（路徑，integer）；JSON：status（string）

Request：

```json
{
  "status": "string"
}
```

回傳：null（Result 沒有內容，看 isSuccess 與 message 即可）

#### `POST /api/merchant/qrcode/bind`　綁定 QR Code 與優惠券

參數：JSON：qr_uid（string）、coupon_id（integer）

Request：

```json
{
  "qr_uid": "string",
  "coupon_id": 0
}
```

回傳：null（Result 沒有內容，看 isSuccess 與 message 即可）

#### `GET /api/merchant/questions`　查詢登入商家的題庫列表

回傳：

```json
[
  {
    "question_id": 0,
    "store_id": 0,
    "question_describe": "string",
    "options": [
      {
        "option_id": 0,
        "option_context": "string",
        "option_url": "string",
        "is_correct": false,
        "option_key": "string"
      }
    ]
  }
]
```

#### `POST /api/merchant/questions`　新增題目

參數：JSON：question_describe（string）、options（QuestionOptionInput[]）

Request：

```json
{
  "question_describe": "string",
  "options": [
    {
      "option_context": "string",
      "option_url": "string",
      "is_correct": false,
      "option_key": "string"
    }
  ]
}
```

回傳：

```json
{
  "question_id": 0
}
```

#### `DELETE /api/merchant/questions/{questionId}`　刪除題目

參數：questionId（路徑，integer）

#### `PUT /api/merchant/questions/{questionId}`　修改題目與選項

參數：questionId（路徑，integer）；JSON：question_describe（string）、options（QuestionOptionInput[]）

Request：

```json
{
  "question_describe": "string",
  "options": [
    {
      "option_context": "string",
      "option_url": "string",
      "is_correct": false,
      "option_key": "string"
    }
  ]
}
```

回傳：null（Result 沒有內容，看 isSuccess 與 message 即可）

#### `GET /api/qrcode/scan/{qrUid}`　掃描 QR Code 查詢商家與優惠券資訊

參數：qrUid（路徑，string）

回傳：

```json
{
  "coupon": {
    "coupon_id": 0,
    "s_id": 0,
    "coupon_code": "string",
    "coupon_name": "string",
    "discount_commodity": "string",
    "discount_type": "string",
    "discount_value": 0.0,
    "valid_from": "2026-10-07T12:00:00",
    "valid_to": "2026-10-07T12:00:00",
    "status": "string"
  },
  "merchant_place": {
    "uid": "string",
    "identity_labels": [
      "string"
    ],
    "gov_name": "string",
    "gov_description": "string",
    "gov_address": "string",
    "gov_lat": 0.0,
    "gov_lon": 0.0,
    "gov_status": "string",
    "gov_phone": "string",
    "gov_website": "string",
    "gov_ticket_info": "string",
    "gov_travel_info": "string",
    "categories": [
      "string"
    ],
    "city": "string",
    "town": "string",
    "images": [
      {
        "url": "string",
        "description": "string"
      }
    ],
    "operating_hours": [
      {
        "day_of_week": "string",
        "open_time": "string",
        "close_time": "string"
      }
    ],
    "hotel_classes": [
      "string"
    ],
    "merchant_override": {
      "key": null
    }
  },
  "is_newly_claimed": false
}
```

### 管理員

#### `POST /api/Bus/PlaceStops/Build`　重算景點附近的公車站牌（生成劇本規劃公車用）

參數：scope（查詢，string）

回傳：

```json
{
  "scope": "string",
  "place_count": 0,
  "place_with_stop_count": 0,
  "pair_count": 0,
  "used_valhalla": false,
  "elapsed_seconds": 0.0
}
```

#### `POST /api/Bus/Sync/City/{city}`　從 TDX 同步某縣市的市區公車

參數：city（路徑，string）

回傳：

```json
{
  "source": "string",
  "route_count": 0,
  "sub_route_count": 0,
  "pattern_count": 0,
  "rebuilt_pattern_count": 0,
  "stop_count": 0,
  "trip_count": 0,
  "deactivated_route_count": 0,
  "elapsed_seconds": 0.0
}
```

#### `POST /api/Bus/Sync/TaiwanTrip`　從 TDX 同步全台台灣好行

回傳：

```json
{
  "source": "string",
  "route_count": 0,
  "sub_route_count": 0,
  "pattern_count": 0,
  "rebuilt_pattern_count": 0,
  "stop_count": 0,
  "trip_count": 0,
  "deactivated_route_count": 0,
  "elapsed_seconds": 0.0
}
```

#### `POST /api/Metro/Sync/{system}`　從 TDX 同步一個捷運系統

參數：system（路徑，string）

回傳：

```json
{
  "rail_system": "string",
  "system_name": "string",
  "line_count": 0,
  "station_count": 0,
  "link_count": 0,
  "shape_count": 0,
  "estimated_line_count": 0,
  "elapsed_seconds": 0.0
}
```
