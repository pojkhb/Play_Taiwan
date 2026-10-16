# 商家描述 embedding 服務

商家註冊建立新景點、或修改店名／介紹時，後端會呼叫這個服務把介紹轉成向量，寫進 Neo4j 景點節點的 `description_embedding`。
模型與寫法跟資料清洗（`D:\play_taiwan_data_cleaning` 的 `process_*.py`）相同：`intfloat/multilingual-e5-large`、1024 維、
直接編碼介紹原文、正規化成長度 1，所以商家景點可以跟政府資料的景點放在一起做語意搜尋。

服務沒開時商家註冊、修改資料照常成功，只是先不寫向量（後端記一筆 warning）；之後商家改店名或介紹、或還沒有向量時再修改一次資料就會補上。

## 安裝（第一次）

```powershell
cd tools\embedding_service
py -3.11 -m venv .venv
.venv\Scripts\pip install torch --index-url https://download.pytorch.org/whl/cpu
.venv\Scripts\pip install -r requirements.txt
```

## 啟動

```powershell
cd tools\embedding_service
.venv\Scripts\python embedding_server.py            # 預設 http://127.0.0.1:8003
.venv\Scripts\python embedding_server.py --port 8010 --device cuda
```

第一次啟動會從 Hugging Face 下載模型（約 2.2GB，存在 `%USERPROFILE%\.cache\huggingface`），之後啟動約 10～20 秒。
後端用 `appsettings.json` 的 `EmbeddingService:BaseUrl` 連過來，改了 port 要一起改。

## API

| 方法 | 路徑 | 說明 |
|---|---|---|
| GET | `/health` | `{"status": "ok", "model": "intfloat/multilingual-e5-large", "dimension": 1024}` |
| POST | `/embed` | 請求 `{"texts": ["..."]}`（最多 64 筆），回應 `{"model": "...", "dimension": 1024, "embeddings": [[...]]}` |
