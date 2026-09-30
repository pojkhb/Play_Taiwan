// 迷霧圖影像處理（FogImageHelper）：景點照片 → 若隱若現的迷霧版
using backend.util;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TrafficSystem.Tests.Map;

public class FogImageHelperTests
{
    /// <summary>左半黑、右半白的照片，看得出邊界有沒有被霧化</summary>
    private static Image<Rgba32> HalfBlackHalfWhite(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(a =>
        {
            for (int y = 0; y < a.Height; y++)
            {
                Span<Rgba32> row = a.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = x < width / 2 ? new Rgba32(0, 0, 0) : new Rgba32(255, 255, 255);
            }
        });
        return image;
    }

    private static Image<Rgba32> Texture() => new Image<Rgba32>(64, 64, new Rgba32(240, 240, 240, 200));

    [Theory]
    [InlineData(1600, 900)]   // 橫的
    [InlineData(600, 1200)]   // 直的
    [InlineData(1, 1)]        // 極小
    public void 任何尺寸的照片都輸出480x360(int width, int height)
    {
        using var photo = HalfBlackHalfWhite(width, height);
        using var fog = FogImageHelper.CreateFoggedPhoto(photo, Texture());

        Assert.Equal(FogImageHelper.Width, fog.Width);
        Assert.Equal(FogImageHelper.Height, fog.Height);
    }

    [Fact]
    public void 照片被刷白_看不到純黑()
    {
        using var photo = HalfBlackHalfWhite(960, 720);
        using var fog = FogImageHelper.CreateFoggedPhoto(photo, Texture());

        Rgba32 darkSide = fog[40, 180];
        Assert.InRange(darkSide.R, 90, 200);   // 原本純黑，霧化後變灰
    }

    [Fact]
    public void 邊界被模糊_看得出明暗但沒有銳利的輪廓()
    {
        using var photo = HalfBlackHalfWhite(960, 720);
        using var fog = FogImageHelper.CreateFoggedPhoto(photo, Texture());

        int left = fog[200, 180].R, middle = fog[240, 180].R, right = fog[280, 180].R;
        Assert.True(left < middle && middle < right, $"{left} < {middle} < {right}");   // 仍看得出左暗右亮
        Assert.True(right - left < 160, $"差距 {right - left}");                           // 但邊界是漸層，不是一刀切開
    }
}
