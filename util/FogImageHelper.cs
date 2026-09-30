using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace backend.util
{
    /// <summary>
    /// 地圖迷霧圖：把景點照片霧化成「若隱若現」的樣子，看得出輪廓與色塊、但認不出是哪裡。
    /// 參數對應 docs/fog/迷霧效果比較.png 的「中霧」。
    /// </summary>
    public static class FogImageHelper
    {
        public const int Width = 480;
        public const int Height = 360;

        private const float Saturation = 0.4f;      // 褪色
        private const float BlurSigma = 9.3f;       // 模糊到認不出細節（原型 360 寬用 7，等比放大）
        private const float WashAmount = 0.42f;     // 刷白的比例
        private const float TextureOpacity = 0.65f; // 雲霧紋理的濃度
        private static readonly Rgba32 WashColor = new Rgba32(236, 240, 245);

        /// <summary>
        /// 景點照片 → 迷霧圖（480×360，置中裁切）。
        /// </summary>
        /// <param name="photo">原始景點照片</param>
        /// <param name="fogTexture">雲霧紋理（wwwroot/images/fog/default_fog.png，透明背景）</param>
        public static Image<Rgba32> CreateFoggedPhoto(Image<Rgba32> photo, Image<Rgba32> fogTexture)
        {
            Image<Rgba32> result = photo.Clone(x => x
                .Resize(new ResizeOptions { Size = new Size(Width, Height), Mode = ResizeMode.Crop })
                .Saturate(Saturation)
                .GaussianBlur(BlurSigma));

            // 刷白：每個像素往霧的顏色靠近
            result.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        Rgba32 p = row[x];
                        row[x] = new Rgba32(Lerp(p.R, WashColor.R), Lerp(p.G, WashColor.G), Lerp(p.B, WashColor.B), 255);
                    }
                }
            });

            // 蓋上雲霧紋理：放大到 1.5 倍寬後取中間，讓霧團的濃淡分布在整張圖上
            int size = (int)(Width * 1.5);
            using Image<Rgba32> texture = fogTexture.Clone(x => x
                .Resize(size, size)
                .Crop(new Rectangle((size - Width) / 2, (size - Height) / 2, Width, Height)));
            result.Mutate(x => x.DrawImage(texture, new Point(0, 0), TextureOpacity));

            return result;
        }

        private static byte Lerp(byte from, byte to) =>
            (byte)Math.Round(from + (to - from) * WashAmount);
    }
}
