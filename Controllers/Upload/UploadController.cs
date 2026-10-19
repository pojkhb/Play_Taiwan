using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        // 任務答題（拍照/影片/錄音）與其他功能共用的檔案上傳限制。
        // 影片是目前最大宗的媒體類型（e人訪談型），200MB 大約可容納幾分鐘的中等畫質影片。
        private const long MaxUploadBytes = 200 * 1024 * 1024; // 200MB

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // 照片
            ".jpg", ".jpeg", ".png", ".webp", ".heic",
            // 影片
            ".mp4", ".mov", ".webm",
            // 音訊
            ".mp3", ".m4a", ".wav", ".aac"
        };

        private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "image/webp", "image/heic",
            "video/mp4", "video/quicktime", "video/webm",
            "audio/mpeg", "audio/mp4", "audio/wav", "audio/aac", "audio/x-wav"
        };

        // 商家註冊時還沒有帳號，要先上傳店家照片才拿得到 image_url，所以沒登入也能上傳；
        // 沒登入只收照片、上限 20MB，影片、錄音（任務作答用）仍要登入。
        private const long AnonymousMaxUploadBytes = 20 * 1024 * 1024; // 20MB

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".heic"
        };

        private static readonly Dictionary<string, string> ImageExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/heic"] = ".heic"
        };

        /// <summary>
        /// 上傳檔案（照片、影片、錄音），回傳檔案網址。
        /// </summary>
        /// <remarks>
        /// 使用 multipart/form-data，欄位名稱為 file，上限 200MB。
        /// 支援照片（jpg/png/webp/heic）、影片（mp4/mov/webm）、音訊（mp3/m4a/wav/aac）。
        /// 不用登入也能上傳照片（商家註冊畫面上傳店家照片用），但沒登入只收照片、上限 20MB；影片、錄音要登入。
        /// </remarks>
        /// <response code="200">Result = 上傳後的檔案網址</response>
        [AllowAnonymous]
        [HttpPost]
        [RequestSizeLimit(MaxUploadBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "未提供檔案" });

            if (file.Length > MaxUploadBytes)
                return BadRequest(new ResultViewModel<string> { isSuccess = false, message = $"檔案過大，上限為 {MaxUploadBytes / 1024 / 1024}MB" });

            var fileExt = Path.GetExtension(file.FileName);
            bool extOk = !string.IsNullOrEmpty(fileExt) && AllowedExtensions.Contains(fileExt);
            bool contentTypeOk = !string.IsNullOrEmpty(file.ContentType) && AllowedContentTypes.Contains(file.ContentType);

            // 副檔名與 Content-Type 只要有一項在允許清單內就放行；避免瀏覽器/裝置對某些格式回報不一致的 MIME type 誤擋合法檔案，
            // 但兩者都不在允許清單內（例如 .exe、.zip、.html）一律拒絕。
            if (!extOk && !contentTypeOk)
            {
                return BadRequest(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = "不支援的檔案類型，僅接受照片（jpg/png/webp/heic）、影片（mp4/mov/webm）或音訊（mp3/m4a/wav/aac）"
                });
            }

            if (User.Identity?.IsAuthenticated != true)
            {
                // 存檔的副檔名一定要是照片的，避免有人把 .html 標成 image/jpeg 傳上來、由後端網域對外提供
                string imageExt = AnonymousImageExtension(fileExt, file.ContentType);
                if (imageExt == null)
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "沒登入只能上傳照片（jpg/png/webp/heic），影片、錄音請先登入" });

                if (file.Length > AnonymousMaxUploadBytes)
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = $"沒登入上傳照片上限為 {AnonymousMaxUploadBytes / 1024 / 1024}MB" });

                fileExt = imageExt;
            }

            try
            {
                // 確保 uploads 資料夾存在
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                // 產生唯一檔名（副檔名沿用前面已驗證過的 fileExt）
                var uniqueFileName = Guid.NewGuid().ToString() + fileExt;
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // 回傳網址 (假設您的伺服器對外提供 wwwroot 的靜態檔案)
                var fileUrl = $"{Request.Scheme}://{Request.Host}/uploads/{uniqueFileName}";

                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "上傳成功",
                    Result = fileUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = ex.Message });
            }
        }

        /// <summary>沒登入上傳時要存的照片副檔名：原檔名是照片就沿用，不然看 Content-Type；都不是照片回 null。</summary>
        private static string AnonymousImageExtension(string fileExt, string contentType)
        {
            if (!string.IsNullOrEmpty(fileExt) && ImageExtensions.Contains(fileExt))
                return fileExt;

            if (!string.IsNullOrEmpty(contentType) && ImageExtensionByContentType.TryGetValue(contentType, out string ext))
                return ext;

            return null;
        }
    }
}
