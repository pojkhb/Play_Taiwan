// 檔案路徑：System\Services\Silhouette\SilhouetteService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using backend.dao;
using backend.utils;
using backend.Models;
using backend.util;
using Microsoft.AspNetCore.Hosting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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

        #region 劇本節點剪影：用景點照片做去背實心剪影，存成靜態檔

        /// <summary>剪影檔放在 wwwroot 底下這個資料夾，前端直接用網址讀</summary>
        public const string GeneratedFolder = "/images/silhouettes/generated/";

        /// <summary>
        /// 為劇本每個節點產生剪影：
        /// 景點照片（place.p_image，地圖解鎖後顯示的同一張）→ 去背實心剪影 → 存成 PNG → 寫入 silhouette 並連到節點。
        /// 同一張照片的剪影跨劇本重用；節點已經有剪影就略過，可以重複呼叫。
        /// </summary>
        public async Task<SilhouetteGenerateResult> GenerateForStoryAsync(int storyId)
        {
            List<SilhouetteDao.NodePhotoRow> nodes = await _dao.GetStoryNodePhotosAsync(storyId);
            if (nodes.Count == 0)
                throw new KeyNotFoundException($"找不到劇本 story_id={storyId} 或劇本沒有節點");

            var result = new SilhouetteGenerateResult { story_id = storyId };

            foreach (SilhouetteDao.NodePhotoRow node in nodes)
            {
                var item = new SilhouetteNodeResult
                {
                    sn_id = node.sn_id,
                    sn_order = node.sn_order,
                    place_name = node.place_name,
                    silhouette_image_url = node.silhouette_image
                };
                result.nodes.Add(item);

                if (node.si_id.HasValue)
                {
                    item.status = "already_linked";
                    continue;
                }
                if (string.IsNullOrWhiteSpace(node.photo_url))
                {
                    item.status = "no_photo";
                    continue;
                }

                try
                {
                    Silhouette existing = await _dao.FindBySourcePhotoAsync(node.photo_url);
                    int siId;

                    if (existing != null && File.Exists(PhysicalPath(existing.si_silhouette_image)))
                    {
                        siId = existing.si_id;
                        item.silhouette_image_url = existing.si_silhouette_image;
                        item.status = "reused";
                        result.reused++;
                    }
                    else
                    {
                        (item.silhouette_image_url, item.area_ratio) = await CreateSilhouetteFileAsync(node.photo_url);
                        string name = $"{node.place_name ?? node.location_codename ?? "景點"}剪影";

                        if (existing != null)
                        {
                            siId = existing.si_id;                        // 資料還在但檔案不見了，重做檔案
                            await _dao.UpdateImageAsync(siId, item.silhouette_image_url);
                        }
                        else
                        {
                            siId = await _dao.InsertAsync(name, item.silhouette_image_url, node.photo_url, node.location_codename);
                        }
                        item.status = "generated";
                        result.generated++;
                    }

                    await _dao.LinkAsync(node.sn_id, siId, node.place_id, node.location_codename);
                }
                catch (Exception e)
                {
                    item.status = "failed";
                    item.message = e.Message;
                }
            }

            return result;
        }

        /// <summary>下載照片、做剪影、存檔；檔名用照片網址的雜湊，同一張照片永遠對到同一個檔案</summary>
        private async Task<(string url, double areaRatio)> CreateSilhouetteFileAsync(string photoUrl)
        {
            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            using var request = new HttpRequestMessage(HttpMethod.Get, photoUrl);
            request.Headers.UserAgent.ParseAdd("PlayTaiwan/1.0 (silhouette generator)");   // Wikimedia 等圖庫要求帶 User-Agent

            using HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"下載景點照片失敗（HTTP {(int)response.StatusCode}）");
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();

            using Image<Rgba32> photo = Image.Load<Rgba32>(bytes);
            SkySilhouetteResult silhouette = SilhouetteImageHelper.CreateSkySilhouette(photo);
            using (silhouette.Image)
            {
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(photoUrl)))[..16].ToLowerInvariant();
                string url = $"{GeneratedFolder}{hash}.png";
                string path = PhysicalPath(url);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                await silhouette.Image.SaveAsPngAsync(path);
                return (url, Math.Round(silhouette.AreaRatio, 3));
            }
        }

        /// <summary>網址（/images/...）對應到 wwwroot 底下的實體路徑</summary>
        public string PhysicalPath(string url) =>
            string.IsNullOrWhiteSpace(url)
                ? ""
                : Path.Combine(_environment.WebRootPath, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        #endregion
    }
}