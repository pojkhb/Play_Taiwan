# Valhalla 本機架設（交通等時圈 / 路線規劃）

後端用自架的 [Valhalla](https://github.com/valhalla/valhalla) 路網引擎算交通時間，用 Docker 跑在本機的 `http://localhost:8002`。
照這份做完，大約需要 **20 分鐘**（大部分是等它自己建圖資），之後每次開機 Docker Desktop 會自動把它帶起來。

---

## 為什麼要裝

Valhalla 負責三件事：

| 功能 | 說明 |
|---|---|
| 等時圈（isochrone） | 從某個點出發，步行 / 騎車 / 開車 N 分鐘內能到的範圍 |
| 交通時間矩陣（sources_to_targets） | 一個起點到多個景點的真實時間與距離 |
| 路線（route） | 依序經過多個點的實際路線 |

會用到它的 API：

| API | 沒開 Valhalla 時 |
|---|---|
| `POST /api/Story/GenerateGameStory`（劇本生成） | ❌ 直接失敗 |
| `POST /api/Story/ReachableAttractions` | ❌ 直接失敗 |
| `POST /api/Story/TravelRoute` | ❌ 直接失敗 |
| `/api/Route/Availability`、`/api/Route/Plan`（交通規劃） | ⚠️ 退回直線距離估算，時間不準 |
| 公車站牌對應、公車路線線形 | ⚠️ 退回直線 / 站牌連線 |

其他 API 不受影響。**要測劇本生成的人一定要裝。**

---

## 事前準備

- **Docker Desktop**（Windows 請用 WSL 2 模式）：<https://www.docker.com/products/docker-desktop/>
- 磁碟空間約 **2 GB**（映像檔 0.9 GB + 台灣圖資 0.9 GB）
- 第一次建圖資需要網路（會下載約 330 MB 的台灣 OpenStreetMap 資料）

裝好後打開 Docker Desktop，等左下角顯示 Engine running，再開 PowerShell 確認：

```powershell
docker version
```

有看到 `Server:` 那一段就代表 Docker 有在跑。
如果出現 `error during connect`，代表 Docker Desktop 還沒啟動。

---

## 步驟 1：建立圖資資料夾

圖資會存在這個資料夾，容器砍掉重建也不會不見。路徑請**不要有中文或空白**：

```powershell
New-Item -ItemType Directory -Force C:\valhalla-tw\custom_files
```

> 💡 **想跳過 15 分鐘的建置？** 跟已經架好的組員拿他的 `C:\valhalla-tw\custom_files` 資料夾（約 900 MB），
> 整包複製到你的 `C:\valhalla-tw\custom_files`，裡面至少要有 `valhalla_tiles.tar`。
> 有這個檔案，容器啟動時就會直接載入，不會重新下載和建置。

---

## 步驟 2：啟動容器

在 PowerShell 貼上這一行：

```powershell
docker run -dt --name valhalla_tw -p 8002:8002 -v C:/valhalla-tw/custom_files:/custom_files -e tile_urls=https://download.geofabrik.de/asia/taiwan-latest.osm.pbf -e server_threads=4 --restart unless-stopped ghcr.io/gis-ops/docker-valhalla/valhalla:latest
```

每個參數在做什麼：

| 參數 | 意思 |
|---|---|
| `--name valhalla_tw` | 容器名稱，之後的指令都用這個名字 |
| `-p 8002:8002` | 開在本機的 8002 port（後端預設就是連這裡） |
| `-v C:/valhalla-tw/custom_files:/custom_files` | 圖資存在步驟 1 的資料夾 |
| `-e tile_urls=...taiwan-latest.osm.pbf` | 從 Geofabrik 下載台灣的 OpenStreetMap 圖資 |
| `-e server_threads=4` | 用 4 個執行緒處理請求；電腦比較弱可以改成 2 |
| `--restart unless-stopped` | 開機或重開 Docker Desktop 時自動啟動 |

> Mac / Linux：把 `-v` 那段換成自己的路徑，例如 `-v ~/valhalla-tw/custom_files:/custom_files`，其他都一樣。

---

## 步驟 3：等它建圖資

第一次啟動會自己下載台灣圖資、建路網。整個過程**約 15 分鐘**，視電腦而定。看進度：

```powershell
docker logs -f valhalla_tw
```

- 看到 `INFO: Found config file. Starting valhalla service!` 就是好了。按 `Ctrl + C` 離開 log，容器會繼續跑。
- 中間出現 `traffic.tar No such file or directory` 的 WARN 是正常的（我們沒有即時路況資料），不用理它。

建完後 `C:\valhalla-tw\custom_files` 裡會有：

```
taiwan-latest.osm.pbf     ← 下載的台灣原始圖資（約 330 MB）
valhalla_tiles/           ← 建好的路網
valhalla_tiles.tar        ← 打包好的路網（約 300 MB，實際載入的是這個）
valhalla.json             ← Valhalla 設定檔
file_hashes.txt、duplicateways.txt
```

---

## 步驟 4：確認能用

```powershell
curl.exe http://localhost:8002/status
```

> PowerShell 裡的 `curl` 是別的指令，請打 `curl.exe`。

應該回傳：

```json
{"version":"3.5.1", ... "available_actions":["status","centroid","expansion", ... "isochrone","optimized_route","sources_to_targets","height","route","locate"]}
```

再試一次真的算路線（宮原眼科 → 臺中公園，步行）：

```powershell
curl.exe -X POST http://localhost:8002/route -H "Content-Type: application/json" -d '{\"locations\":[{\"lat\":24.1378,\"lon\":120.6836},{\"lat\":24.1441,\"lon\":120.6841}],\"costing\":\"pedestrian\"}'
```

回傳裡的 `"summary"` 有 `"time":766...`（約 13 分鐘）、`"length":1.069`（約 1 公里）就對了。

---

## 步驟 5：後端設定

`appsettings.json` 已經有這段，**用預設的 8002 port 就不用改**：

```json
"Valhalla": {
  "BaseUrl": "http://localhost:8002"
}
```

啟動後端後，打開 Swagger 測 `POST /api/Story/ReachableAttractions`：

```json
{ "lat": 24.1378, "lng": 120.6836, "transportation": ["步行"] }
```

有回傳 `attractions` 和每個景點的 `travel_minutes` 就代表整條串起來了。

---

## 日常操作

| 要做什麼 | 指令 |
|---|---|
| 看有沒有在跑 | `docker ps` |
| 停止 | `docker stop valhalla_tw` |
| 啟動 | `docker start valhalla_tw` |
| 看 log | `docker logs --tail 50 valhalla_tw` |

因為有設定 `--restart unless-stopped`，只要 Docker Desktop 有開，它就會自己啟動。
手動 `docker stop` 之後，要自己再 `docker start`。

---

## 更新圖資（想要最新的 OpenStreetMap 時）

平常不用更新。要更新時，把容器和舊圖資都刪掉，重新跑一次步驟 2：

```powershell
docker rm -f valhalla_tw
Remove-Item -Recurse -Force C:\valhalla-tw\custom_files\*
# 然後重新執行步驟 2 的 docker run，再等一次建置
```

---

## 常見問題

**`docker: error during connect` / `Cannot connect to the Docker daemon`**
Docker Desktop 沒開，打開它，等 Engine running 再試。

**`Bind for 0.0.0.0:8002 failed: port is already allocated`**
8002 被別的程式佔用了。改成 `-p 8012:8002`，並把 `appsettings.json` 的 `Valhalla:BaseUrl` 改成 `http://localhost:8012`。

**`Conflict. The container name "/valhalla_tw" is already in use`**
之前已經建過同名容器。直接 `docker start valhalla_tw`，或先 `docker rm -f valhalla_tw` 再重跑步驟 2。

**後端錯誤訊息出現 `Valhalla isochrone 失敗`，或連不到 `localhost:8002`**
容器沒在跑（用 `docker ps` 看），或還在建圖資（用 `docker logs -f valhalla_tw` 看進度）。

**建置到一半容器停掉 / 很慢**
通常是記憶體不夠。WSL 2 預設只能用電腦一半的記憶體，8 GB 的電腦只分到 4 GB 左右。
可以在 `%UserProfile%\.wslconfig` 加上下面這段，執行 `wsl --shutdown`，再重開 Docker Desktop：

```ini
[wsl2]
memory=6GB
```

然後 `docker rm -f valhalla_tw`，把 `custom_files` 清空，重跑步驟 2。

**公車、捷運的時間怎麼算？**
Valhalla 沒有匯入公車時刻表（GTFS），所以劇本生成挑景點時，只用步行 / 腳踏車 / 機車 / 汽車算交通圈。
公車路線另外由公車資料表（TDX 同步）處理，捷運由 `MetroService` 處理，都不靠 Valhalla。
