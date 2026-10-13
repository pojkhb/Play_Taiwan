// 明信片圖片：依檔案內容判斷圖片格式、下載時存檔的檔名（純函式）
using Allure.Net.Commons.Attributes;
using backend.Services;

namespace TrafficSystem.Tests.Postcard;

[AllureSuiteHierarchy("明信片與上傳", "單元測試")]
[AllureBddHierarchy("明信片與上傳", "單元測試")]
public class PostcardImageTests
{
    [Fact]
    public void 依檔案開頭判斷圖片格式_不是圖片回傳null()
    {
        Assert.Equal("image/png", PostcardCatalogService.ContentTypeOf(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }));
        Assert.Equal("image/jpeg", PostcardCatalogService.ContentTypeOf(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }));
        Assert.Equal("image/gif", PostcardCatalogService.ContentTypeOf("GIF89a"u8.ToArray()));
        Assert.Equal("image/webp", PostcardCatalogService.ContentTypeOf("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()));

        Assert.Null(PostcardCatalogService.ContentTypeOf("<html>找不到檔案</html>"u8.ToArray()));
        Assert.Null(PostcardCatalogService.ContentTypeOf("RIFF\0\0\0\0WAVEfmt "u8.ToArray()));   // 同樣是 RIFF 開頭的音訊檔
        Assert.Null(PostcardCatalogService.ContentTypeOf(new byte[] { 0x89, 0x50 }));
        Assert.Null(PostcardCatalogService.ContentTypeOf(null));
    }

    [Theory]
    [InlineData("台北101 專屬明信片", "image/png", "台北101 專屬明信片.png")]
    [InlineData("夜色明信片", "image/jpeg", "夜色明信片.jpg")]
    [InlineData("海邊", "image/webp", "海邊.webp")]
    [InlineData("  臺中/文學館：\"夏日\"?  ", "image/png", "臺中文學館：夏日.png")]   // 不能當檔名的字元拿掉（全形冒號可以留）
    [InlineData("...", "image/png", "postcard-21.png")]
    [InlineData("", "image/gif", "postcard-21.gif")]
    [InlineData(null, null, "postcard-21.png")]
    public void 下載的檔名是明信片名稱加副檔名_沒有名稱時用代號(string name, string contentType, string expected)
    {
        Assert.Equal(expected, PostcardCatalogService.DownloadFileName(name, 21, contentType));
    }
}
