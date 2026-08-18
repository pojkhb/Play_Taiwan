using System;
using System.Collections.Generic;
using System.IO;
using backend.dao;
using backend.Models;
using backend.util;
using Microsoft.AspNetCore.Hosting;

namespace backend.Services
{
    public class SilhouetteService
    {
        private readonly SilhouetteDao _dao;
        private readonly IWebHostEnvironment _environment;

        public SilhouetteService(
            SilhouetteDao dao,
            IWebHostEnvironment environment)
        {
            _dao = dao;
            _environment = environment;
        }

        #region 取得剪影清單

        public List<Silhouette> GetSilhouettes()
        {
            return _dao.GetSilhouettes();
        }

        #endregion

        #region 取得單一剪影

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

        #endregion

        #region 產生亮度閾值剪影

        public string GenerateSilhouette(string silhouetteId, int threshold)
        {
            if (threshold < 0 || threshold > 255)
            {
                throw new ArgumentException("threshold 必須介於 0 到 255 之間。");
            }

            Silhouette silhouette = GetSilhouetteById(silhouetteId);

            if (string.IsNullOrWhiteSpace(silhouette.image_url))
            {
                throw new InvalidOperationException("此剪影尚未設定圖片路徑。");
            }

            string webRootPath = _environment.WebRootPath;

            if (string.IsNullOrWhiteSpace(webRootPath))
            {
                webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");
            }

            string relativeImagePath = silhouette.image_url
                .Trim()
                .TrimStart('/', '\\')
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            string inputPath = Path.Combine(webRootPath, relativeImagePath);

            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException(
                    "找不到原始圖片，請先將圖片放到 wwwroot 對應資料夾。",
                    inputPath);
            }

            string fileName = Path.GetFileNameWithoutExtension(inputPath);
            string outputDirectory = Path.Combine(
                webRootPath,
                "images",
                "silhouettes",
                "generated");
            string outputFileName = $"{fileName}-silhouette.png";
            string outputPath = Path.Combine(outputDirectory, outputFileName);

            SilhouetteImageHelper.CreateThresholdSilhouette(
                inputPath,
                outputPath,
                (byte)threshold);

            return $"/images/silhouettes/generated/{outputFileName}";
        }

        #endregion
    }
}
