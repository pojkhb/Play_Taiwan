// 檔案路徑：System\Services\Fog\FogService.cs
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using backend.dao;
using backend.util;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace backend.Services
{
    /// <summary>一個劇本產生迷霧圖的結果</summary>
    public class FogGenerateResult
    {
        public int story_id { get; set; }

        /// <summary>這次新做的迷霧圖數量</summary>
        public int generated { get; set; }

        /// <summary>之前做過、直接沿用的數量（同一張照片跨劇本共用）</summary>
        public int reused { get; set; }

        /// <summary>照片下載或處理失敗的數量（這些站會先顯示通用迷霧圖）</summary>
        public int failed { get; set; }
    }

    /// <summary>
    /// 把劇本每一站的景點照片做成迷霧圖（中霧），存成 wwwroot/images/fog/generated/{照片網址的 SHA-1}.jpg，
    /// 並記錄在 fog 資料表。同一張照片只做一次。
    /// </summary>
    public class FogService
    {
        public const string GeneratedFolder = "/images/fog/generated/";

        private readonly FogDao _dao;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FogService> _logger;
        private readonly string _webRoot;
        private readonly string _outputRoot;

        public FogService(FogDao dao, IHttpClientFactory httpClientFactory, IWebHostEnvironment environment,
                          IConfiguration configuration, ILogger<FogService> logger)
        {
            _dao = dao;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _webRoot = environment.WebRootPath;
            // 迷霧圖存放的根目錄，預設就是 wwwroot（測試時改到暫存資料夾）
            _outputRoot = string.IsNullOrWhiteSpace(configuration["Fog:GeneratedRoot"]) ? _webRoot : configuration["Fog:GeneratedRoot"];
        }

        /// <summary>照片網址的 SHA-1（小寫十六進位），跟 MySQL 的 SHA1() 算出來的一樣</summary>
        public static string SourceKey(string photoUrl) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(photoUrl))).ToLowerInvariant();

        /// <summary>迷霧圖實際存放的檔案路徑</summary>
        public string PhysicalPath(string sourceKey) =>
            Path.Combine(_outputRoot, "images", "fog", "generated", sourceKey + ".jpg");

        public async Task<FogGenerateResult> GenerateForStoryAsync(int storyId)
        {
            var result = new FogGenerateResult { story_id = storyId };
            var photos = await _dao.GetStoryPlacePhotosAsync(storyId);
            if (photos.Count == 0) return result;

            using Image<Rgba32> texture = await Image.LoadAsync<Rgba32>(Path.Combine(_webRoot, "images", "fog", "default_fog.png"));

            foreach (FogDao.PlacePhoto photo in photos)
            {
                string key = SourceKey(photo.photo_url);
                string physical = PhysicalPath(key);

                if (await _dao.FindFogImageAsync(key) != null && File.Exists(physical))
                {
                    result.reused++;
                    continue;
                }

                try
                {
                    byte[] bytes = await DownloadAsync(photo.photo_url);
                    using Image<Rgba32> source = Image.Load<Rgba32>(bytes);
                    using Image<Rgba32> fogged = FogImageHelper.CreateFoggedPhoto(source, texture);

                    Directory.CreateDirectory(Path.GetDirectoryName(physical));
                    await fogged.SaveAsJpegAsync(physical, new JpegEncoder { Quality = 80 });
                    await _dao.UpsertAsync(photo.place_name, GeneratedFolder + key + ".jpg", photo.photo_url, key);
                    result.generated++;
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "景點 {Place} 的迷霧圖產生失敗：{Url}", photo.place_name, photo.photo_url);
                    result.failed++;
                }
            }

            return result;
        }

        private async Task<byte[]> DownloadAsync(string url)
        {
            HttpClient client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("PlayTaiwan/1.0 (fog)");   // Wikimedia 會擋沒有 User-Agent 的請求
            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
    }
}
