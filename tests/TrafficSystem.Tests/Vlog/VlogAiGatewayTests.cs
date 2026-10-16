// 商家 / 遊客 VLOG 共用的判斷（VlogAiGateway）：照片篩選、TAG 整理、合成狀態、檔案路徑安全
using System.Text.Json;
using backend.Services;
using backend.utils;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Vlog;

[AllureSuiteHierarchy("VLOG 與回顧", "單元測試")]
[AllureBddHierarchy("VLOG 與回顧", "單元測試")]
public class VlogAiGatewayTests
{
    #region 照片篩選

    [Theory]
    [InlineData("https://cdn.example.com/a.jpg", true)]
    [InlineData("/uploads/a.PNG", true)]
    [InlineData("https://cdn.example.com/a.heic?sig=123", true)]
    [InlineData("https://cdn.example.com/photo", true)]      // 沒有副檔名當作照片
    [InlineData("https://cdn.example.com/clip.mp4", false)]  // 影片不能放進 image_zip
    [InlineData("/uploads/voice.m4a", false)]                // 錄音
    public void 只有照片會被打包進影片(string url, bool expected)
    {
        Assert.Equal(expected, VlogAiGateway.LooksLikeImage(url));
    }

    [Theory]
    [InlineData("https://cdn.example.com/a.jpg", true)]
    [InlineData("/uploads/a.WEBP", true)]
    [InlineData("https://cdn.example.com/photo", true)]          // 沒有副檔名當作 jpg
    [InlineData("https://cdn.example.com/a.heic?sig=123", false)] // AI 的 image_zip 不收 HEIC
    [InlineData("https://cdn.example.com/clip.mp4", false)]
    public void 只有AI吃得下的照片格式能放進影片合成(string url, bool expected)
    {
        Assert.Equal(expected, VlogAiGateway.CanCompose(url));
    }

    [Theory]
    [InlineData("https://cdn.example.com/a.JPG", ".jpg")]
    [InlineData("a.webp", ".webp")]
    [InlineData("https://cdn.example.com/a.png?w=100", ".png")]
    [InlineData("https://cdn.example.com/a.gif", ".jpg")]   // 不支援的格式一律當 .jpg
    [InlineData("https://cdn.example.com/a", ".jpg")]
    public void 照片副檔名統一小寫_不認得的當jpg(string url, string expected)
    {
        Assert.Equal(expected, VlogAiGateway.ImageExtension(url));
    }

    #endregion

    #region TAG 與逗號清單

    [Fact]
    public void TAG會補井號_去空白_去重複_略過空值()
    {
        var tags = VlogAiGateway.ToHashtags(new[] { " 台中美食 ", "#芒果冰", "台中美食", "", null, "宮原 眼科" });

        Assert.Equal(new[] { "#台中美食", "#芒果冰", "#宮原眼科" }, tags);
    }

    [Fact]
    public void 沒有關鍵字時TAG是空清單()
    {
        Assert.Empty(VlogAiGateway.ToHashtags(null));
    }

    [Fact]
    public void 資料庫的逗號字串拆回清單()
    {
        Assert.Equal(new[] { "#a", "#b", "#c" }, VlogAiGateway.SplitList("#a, #b,,#c "));
        Assert.Empty(VlogAiGateway.SplitList(null));
    }

    [Theory]
    [InlineData(1, "草稿")]
    [InlineData(2, "處理中")]
    [InlineData(3, "已完成")]
    [InlineData(4, "失敗")]
    [InlineData(9, "未知")]
    public void 狀態碼對應中文(int status, string expected)
    {
        Assert.Equal(expected, VlogAiGateway.StatusText(status));
    }

    #endregion

    #region AI 合成進度

    [Theory]
    [InlineData("queued", null, VlogTaskState.Processing)]
    [InlineData("running", null, VlogTaskState.Processing)]
    [InlineData("done", "https://ai/a.mp4", VlogTaskState.Ready)]
    [InlineData("done", null, VlogTaskState.Failed)]                   // 說完成卻沒給網址
    [InlineData("failed", null, VlogTaskState.Failed)]
    [InlineData("ERROR", "https://ai/a.mp4", VlogTaskState.Failed)]    // 說失敗就算失敗，即使有網址
    [InlineData(VlogJobApiResponse.NotFound, null, VlogTaskState.Failed)] // AI 查不到任務（重啟過）
    [InlineData(null, null, VlogTaskState.Processing)]
    public void 判斷AI影片合成的進度(string status, string downloadUrl, VlogTaskState expected)
    {
        var response = new VlogJobApiResponse
        {
            status = status,
            result = downloadUrl == null ? null : new VlogJobResult { download_url = downloadUrl }
        };

        Assert.Equal(expected, VlogAiGateway.ParseState(response));
    }

    [Fact]
    public void AI沒有回應時當作處理中()
    {
        Assert.Equal(VlogTaskState.Processing, VlogAiGateway.ParseState(null));
    }

    [Fact]
    public void 影片網址是相對路徑時補上AI服務網址()
    {
        Assert.Equal("https://cdn.example.com/a.mp4", VlogAiGateway.ResolveUrl("https://cdn.example.com/a.mp4"));
        Assert.Equal($"{AiServiceConfig.BaseUrl}/download/a.mp4", VlogAiGateway.ResolveUrl("/download/a.mp4"));
        Assert.Null(VlogAiGateway.ResolveUrl(null));
    }

    [Theory]
    [InlineData("\"照片太少\"", "AI 影片合成失敗：照片太少")]                       // error 是字串
    [InlineData("{\"message\":\"TTS 403\"}", "AI 影片合成失敗：TTS 403")]          // error 是物件
    [InlineData("null", "AI 影片合成失敗（status=failed）")]                        // 沒有說明
    public void 失敗訊息優先用AI給的說明(string errorJson, string expected)
    {
        var response = JsonSerializer.Deserialize<VlogJobApiResponse>($"{{\"status\":\"failed\",\"error\":{errorJson}}}");

        Assert.Equal(expected, VlogAiGateway.FailureMessage(response));
    }

    [Fact]
    public void 下拉選單的每個選項送出時都會通過檢查()
    {
        foreach (VlogOptionItem mood in VlogAiGateway.BgmMoodOptions)
            Assert.Equal(mood.value, VlogAiGateway.ParseOptions(null, mood.value, null).bgm_mood);
        foreach (VlogOptionItem orientation in VlogAiGateway.VideoOrientationOptions)
            Assert.Equal(orientation.value, VlogAiGateway.ParseOptions(null, null, orientation.value).video_orientation);
    }

    [Fact]
    public void 沒帶合成選項時都不送_AI用預設值()
    {
        VlogCreateOptions options = VlogAiGateway.ParseOptions(" ", null, "");

        Assert.Null(options.user_memory_text);
        Assert.Null(options.bgm_mood);
        Assert.Null(options.video_orientation);
    }

    [Fact]
    public void 感想字數跟AI一樣算Unicode字元_emoji算一個字()
    {
        string emoji3000 = string.Concat(Enumerable.Repeat("😀", 3000));   // C# Length 是 6000

        Assert.Equal(emoji3000, VlogAiGateway.ParseOptions(emoji3000, null, null).user_memory_text);
        Assert.Throws<Exception>(() => VlogAiGateway.ParseOptions(emoji3000 + "😀", null, null));
    }

    [Fact]
    public void AI退回請求時列出對不上的照片()
    {
        string body = "{\"detail\":{\"message\":\"圖片與地點資料無法完整配對\",\"metadata_file_not_found\":[\"01_01.heic\"],\"images_without_metadata\":[]}}";

        Assert.Equal("圖片與地點資料無法完整配對（metadata_file_not_found: [\"01_01.heic\"]；images_without_metadata: []）",
            VlogAiGateway.AiErrorMessage(body));
        Assert.Equal("旁白不可為空白", VlogAiGateway.AiErrorMessage("{\"detail\":\"旁白不可為空白\"}"));
    }

    [Fact]
    public void AI查不到任務時提示重新送出()
    {
        string message = VlogAiGateway.FailureMessage(new VlogJobApiResponse { status = VlogJobApiResponse.NotFound });

        Assert.Equal("AI 服務找不到這個合成任務（可能已過期或 AI 服務重啟過），請重新送出", message);
    }

    [Fact]
    public void 同一支影片同時只能送出一次()
    {
        string key = $"test:{Guid.NewGuid():N}";
        using (VlogAiGateway.BeginSubmit(key))
        {
            Exception e = Assert.Throws<Exception>(() => VlogAiGateway.BeginSubmit(key));
            Assert.Contains("正在送出合成", e.Message);
        }

        using (VlogAiGateway.BeginSubmit(key)) { }   // 前一個結束後就可以再送
    }

    #endregion

    #region 本機上傳檔案路徑（安全性）

    [Theory]
    [InlineData("/uploads/../appsettings.json")]        // 想跳出 uploads 資料夾
    [InlineData("/uploads/..%2Fappsettings.json")]      // 編碼過的 ../
    [InlineData("https://cdn.example.com/other/a.jpg")] // 不在 uploads 底下
    [InlineData("/uploads/not-exist-12345.jpg")]        // 檔案不存在
    [InlineData(null)]
    public void 不在uploads裡或不存在的檔案不會被讀取(string url)
    {
        Assert.Null(VlogAiGateway.LocalUploadPath(url));
    }

    [Fact]
    public void uploads裡存在的檔案回傳實體路徑()
    {
        string folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"test-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(file, new byte[] { 1 });

        try
        {
            Assert.Equal(file, VlogAiGateway.LocalUploadPath($"https://host/uploads/{Path.GetFileName(file)}"), ignoreCase: true);
        }
        finally
        {
            File.Delete(file);
        }
    }

    #endregion
}
