// 檔案路徑：System\Services\SilhouetteService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.util;
using Microsoft.AspNetCore.Hosting;

namespace backend.Services
{
    public class SilhouetteService
    {
        private readonly SilhouetteDao _dao;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWebHostEnvironment _environment;

        public SilhouetteService(
            SilhouetteDao dao,
            IHttpClientFactory httpClientFactory,
            IWebHostEnvironment environment)
        {
            _dao = dao;
            _httpClientFactory = httpClientFactory;
            _environment = environment;
        }

        public List<Silhouette> GetSilhouettes()
        {
            return _dao.GetSilhouettes();
        }

        public Silhouette GetSilhouetteById(string silhouetteId)
        {
            if (string.IsNullOrWhiteSpace(silhouetteId))
            {
                throw new ArgumentException("silhouette_id 不可為空白。");
            }

            Silhouette result = _dao.GetSilhouetteById(silhouetteId);
            if (result == null)
            {
                throw new KeyNotFoundException("找不到指定剪影。");
            }
            return result;
        }

        /// <summary>
        /// 透過代號取得圖片的二進位資料 (Base64 轉 byte[])
        /// </summary>
        public byte[] GetSilhouetteImageBytes(string silhouetteId)
        {
            Silhouette silhouette = GetSilhouetteById(silhouetteId);
            if (string.IsNullOrWhiteSpace(silhouette.image_url)) return null;

            string base64Str = silhouette.image_url.Trim();
            if (base64Str.Contains(","))
            {
                base64Str = base64Str.Split(',')[1];
            }

            try
            {
                return Convert.FromBase64String(base64Str);
            }
            catch
            {
                return null;
            }
        }

        #region 從 Neo4j 圖譜動態抓取地點圖片並轉為剪影

        /// <summary>
        /// 接收地點名稱，向 Neo4j 圖譜查詢圖片並即時轉為剪影位元組串流
        /// </summary>
        public async Task<byte[]> GenerateSilhouetteFromNeo4jAsync(string placeName, int threshold)
        {
            var client = _httpClientFactory.CreateClient();

            // 1. 透過 Cypher 向 Neo4j 查詢該地點的圖片網址
            var cypherRequest = new
            {
                query = "MATCH (a:Attraction) WHERE a.name = $place_name RETURN a.name AS name, a.image_url AS image_url LIMIT 1",
                parameters = new { place_name = placeName }
            };

            var response = await client.PostAsJsonAsync("https://vlog.angelalala.com/api/neo4j/cypher", cypherRequest);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("無法從 Neo4j 圖譜服務取得地點資料。");
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonString);
            var root = document.RootElement;

            string imageUrl = null;
            if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array && dataProp.GetArrayLength() > 0)
            {
                var firstNode = dataProp[0];
                foreach (var prop in firstNode.EnumerateObject())
                {
                    if (prop.Name.ToLower().Contains("image"))
                    {
                        imageUrl = prop.Value.GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(imageUrl))
            {
                throw new Exception($"在圖譜中找不到地點 [{placeName}] 的對應圖片網址。");
            }

            // 2. 下載圖譜中的圖片至暫存區進行剪影轉換
            byte[] downloadedBytes = await client.GetByteArrayAsync(imageUrl);
            
            string tempInputPath = Path.GetTempFileName();
            string tempOutputPath = Path.GetTempFileName() + ".png";

            try
            {
                await File.WriteAllBytesAsync(tempInputPath, downloadedBytes);

                // 3. 調用共用工具進行閾值剪影轉換
                SilhouetteImageHelper.CreateThresholdSilhouette(
                    tempInputPath,
                    tempOutputPath,
                    (byte)threshold);

                return await File.ReadAllBytesAsync(tempOutputPath);
            }
            finally
            {
                // 清理暫存檔
                if (File.Exists(tempInputPath)) File.Delete(tempInputPath);
                if (File.Exists(tempOutputPath)) File.Delete(tempOutputPath);
            }
        }

        #endregion
    }
}