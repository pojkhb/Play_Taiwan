using System;
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
        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "未提供檔案" });

            try
            {
                // 確保 uploads 資料夾存在
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                // 產生唯一檔名
                var fileExt = Path.GetExtension(file.FileName);
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
    }
}
