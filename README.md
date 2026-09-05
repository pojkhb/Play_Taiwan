# Play_Taiwan
📬 AI 明信片與 ibon 列印系統 (Play-Taiwan Backend)
本專案為 ASP.NET Core 後端 API，整合了 AI 圖像生成、資料庫無痕明信片渲染，以及透過 Cloudflare Tunnel 串接 Python 爬蟲微服務實現自動化 ibon 實體列印的整合系統。

🛠 系統架構簡介
AI 明信片生成：前端上傳照片至後端，後端轉發至外部 AI 服務 (vlog.angelalala.com)，將產出的圖片轉換為 Base64 字串安全儲存於 MySQL (md_postcard)。

無痕圖片顯示：透過專屬 API (GET /api/PostcardCatalog/Story/{story_id}/Image) 動態將 Base64 解碼為二進位圖片串流回傳，完美解決前端網址過長與載入效能問題。

ibon 雲端列印：透過 story_id 自動抓取該劇本最新的一張明信片，發送至專屬的 Python 爬蟲微服務取得真實的 10 碼取件碼。

🚀 快速啟動步驟
第一步：準備資料庫
確保你的 MySQL 資料庫已建立，並將 md_postcard 資料表中的 image_url 欄位型態調整為 LONGTEXT（以容納高畫質的 Base64 圖片編碼）：

SQL
ALTER TABLE md_postcard MODIFY COLUMN image_url LONGTEXT;
第二步：設定後端連線 (appsettings.json)
檢查你的 C# 專案設定檔，確保資料庫連線正確，且 ibon 微服務的 API 路徑已指向正確的 Cloudflare 網址：

JSON
{
  "ConnectionStrings": {
    "DefaultConnection": "你的資料庫連線字串"
  },
  "IbonPrinterSettings": {
    "ApiUrl": "https://ibon.angelalala.com/upload"
  }
}
第三步：啟動 Python 爬蟲微服務與 Cloudflare 隧道 (僅限主機維護者)
如果你是負責運行 ibon 爬蟲的主機維護者，請確保背景服務正常運作：

確保本機 Python 爬蟲服務已在背景執行（例如監聽 127.0.0.1:9000）。

啟動 Cloudflare 專屬通道，將外部流量導回本機：

Bash
cloudflared tunnel run play-taiwan
第四步：啟動 C# 後端 API
切換至 C# 專案根目錄，執行以下指令啟動專案：

Bash
dotnet run
📡 核心 API 快速參考
生成 AI 明信片

POST /api/PostcardCatalog/GenerateAi (需帶 Token, multipart/form-data)

顯示明信片圖片 (供 <img> 標籤使用)

GET /api/PostcardCatalog/Story/{story_id}/Image (自動抓取該劇本最新的一張)

請求 ibon 列印

POST /api/PostcardCatalog/Print (Request Body: { "story_id": "你的劇本ID" })

紀錄社群分享

POST /api/PostcardCatalog/Share (Request Body: { "story_id": "你的劇本ID", "platform": "IG" })
