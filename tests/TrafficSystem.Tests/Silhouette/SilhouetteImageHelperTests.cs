// 去背實心剪影演算法（SilhouetteImageHelper.CreateSkySilhouette）：用程式畫的測試照片驗證
using backend.util;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TrafficSystem.Tests.Silhouette;

public class SilhouetteImageHelperTests
{
    /// <summary>
    /// 120×100 的測試照片：上方漸層天空、中間一棟深色高塔（塔身有一扇顏色像天空的窗戶）、
    /// 下方綠色地面、天空裡一隻 2×2 的小鳥
    /// </summary>
    public static Image<Rgba32> TowerPhoto(int width = 120, int height = 100)
    {
        var img = new Image<Rgba32>(width, height);
        float sx = width / 120f, sy = height / 100f;
        img.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    float ox = x / sx, oy = y / sy;
                    float t = Math.Min(oy / 80f, 1f);
                    Rgba32 c = new((byte)(135 + 40 * t), (byte)(190 + 25 * t), (byte)(235 + 10 * t));   // 天空
                    if (oy >= 80) c = new(40, 90, 40);                                                  // 地面
                    if (ox >= 50 && ox < 70 && oy >= 20) c = new(60, 60, 70);                           // 高塔
                    if (ox >= 57 && ox < 63 && oy >= 40 && oy < 46) c = new(150, 200, 240);             // 窗戶（像天空的顏色）
                    if (ox >= 15 && ox < 17 && oy >= 10 && oy < 12) c = new(30, 30, 30);                // 小鳥
                    row[x] = c;
                }
            }
        });
        return img;
    }

    private static Rgba32 At(Image<Rgba32> img, int x, int y) => img[x, y];

    [Fact]
    public void 天空變透明_高塔與地面變成實心剪影()
    {
        using var photo = TowerPhoto();
        SkySilhouetteResult result = SilhouetteImageHelper.CreateSkySilhouette(photo, width: 120);
        using var sil = result.Image;

        Assert.Equal(0, At(sil, 10, 5).A);                                  // 天空
        Assert.Equal(0, At(sil, 100, 60).A);                                // 塔旁邊的天空
        Assert.Equal(SilhouetteImageHelper.Ink, At(sil, 60, 30));           // 高塔
        Assert.Equal(SilhouetteImageHelper.Ink, At(sil, 5, 90));            // 地面
        Assert.InRange(result.AreaRatio, 0.27, 0.33);                       // 塔 20×60 + 地面 120×20 = 30%
    }

    [Fact]
    public void 塔身上顏色像天空的窗戶不會變成破洞()
    {
        using var photo = TowerPhoto();
        using var sil = SilhouetteImageHelper.CreateSkySilhouette(photo, width: 120).Image;

        Assert.Equal(SilhouetteImageHelper.Ink, At(sil, 59, 42));
    }

    [Fact]
    public void 天空裡的小雜點會被清掉()
    {
        using var photo = TowerPhoto();
        using var sil = SilhouetteImageHelper.CreateSkySilhouette(photo, width: 120).Image;

        Assert.Equal(0, At(sil, 15, 10).A);
        Assert.Equal(0, At(sil, 16, 11).A);
    }

    [Fact]
    public void 照片沒有天空時剪影幾乎佔滿整張()
    {
        // 像空拍照：整張都是高對比的屋頂格子，沒有可以長的天空
        using var photo = new Image<Rgba32>(120, 100);
        photo.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < 100; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < 120; x++)
                    row[x] = ((x / 6) + (y / 6)) % 2 == 0 ? new Rgba32(200, 60, 40) : new Rgba32(40, 40, 40);
            }
        });

        Assert.True(SilhouetteImageHelper.CreateSkySilhouette(photo, width: 120).AreaRatio > 0.8);
    }

    [Fact]
    public void 輸出圖片等比縮放到指定寬度()
    {
        using var photo = TowerPhoto(960, 640);
        using var sil = SilhouetteImageHelper.CreateSkySilhouette(photo).Image;

        Assert.Equal((480, 320), (sil.Width, sil.Height));
    }
}
