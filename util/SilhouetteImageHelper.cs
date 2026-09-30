using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace backend.util
{
    /// <summary>去背實心剪影的結果</summary>
    public class SkySilhouetteResult
    {
        /// <summary>剪影圖：背景透明，建築物與地面填成單一顏色</summary>
        public Image<Rgba32> Image { get; set; }

        /// <summary>剪影佔整張圖的比例；接近 1 代表照片幾乎沒有天空（例如空拍），剪影會是一整塊</summary>
        public double AreaRatio { get; set; }
    }

    public static class SilhouetteImageHelper
    {
        /// <summary>剪影顏色（深棕黑），前端可以再用 BlendMode.srcIn 換成其他顏色</summary>
        public static readonly Rgba32 Ink = new Rgba32(43, 35, 32, 255);

        // 相鄰像素顏色差多少以內算同一片天空；整體跟天空平均色差多少以內才算天空（避免沿著漸層一路吃進建築物）
        private const int LocalDistance = 14;
        private const int SkyDistance = 90;

        // 小於整張圖這個比例的碎塊（飛鳥、電線、雜點）不算剪影
        private const double MinPartRatio = 0.004;

        /// <summary>
        /// 去背實心剪影：從照片上緣與左右兩側上半部的天空做區域成長找出背景，
        /// 背景透明、其餘（建築物與地面）填成 <see cref="Ink"/>。
        /// 建築物裡的窗戶就算顏色跟天空很像，只要沒有跟外面的天空連在一起就不會變成洞。
        /// 適合背景有天空的地標照片；沒有天空的照片（例如空拍）會整張變成實心，看 AreaRatio 判斷。
        /// </summary>
        /// <param name="photo">原始照片</param>
        /// <param name="width">輸出寬度（等比縮放），同時也讓大照片處理得比較快</param>
        public static SkySilhouetteResult CreateSkySilhouette(Image<Rgba32> photo, int width = 480)
        {
            // 先模糊再判斷，雲、雜訊比較不會擋住天空的成長
            using Image<Rgba32> small = photo.Clone(x => x.Resize(width, 0).GaussianBlur(1.2f));
            int w = small.Width, h = small.Height;
            var px = new Rgba32[w * h];
            small.CopyPixelDataTo(px);

            bool[] background = FindSky(px, w, h);
            bool[] foreground = new bool[w * h];
            for (int i = 0; i < foreground.Length; i++) foreground[i] = !background[i];
            RemoveSmallParts(foreground, w, h, (int)(MinPartRatio * w * h));

            var result = new Image<Rgba32>(w, h);   // 預設全透明
            int filled = 0;
            result.ProcessPixelRows(rows =>
            {
                for (int y = 0; y < h; y++)
                {
                    Span<Rgba32> row = rows.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        if (!foreground[y * w + x]) continue;
                        row[x] = Ink;
                        filled++;
                    }
                }
            });

            return new SkySilhouetteResult { Image = result, AreaRatio = (double)filled / (w * h) };
        }

        /// <summary>從上緣整排、左右兩側上半部開始往內長，跟鄰居像、又還像天空的都算背景</summary>
        private static bool[] FindSky(Rgba32[] px, int w, int h)
        {
            var seeds = new List<int>();
            for (int x = 0; x < w; x++) seeds.Add(x);
            for (int y = 0; y < (int)(h * 0.55); y++)
            {
                seeds.Add(y * w);
                seeds.Add(y * w + w - 1);
            }

            double mr = 0, mg = 0, mb = 0;
            foreach (int i in seeds) { mr += px[i].R; mg += px[i].G; mb += px[i].B; }
            var sky = new Rgba32((byte)(mr / seeds.Count), (byte)(mg / seeds.Count), (byte)(mb / seeds.Count));

            int local2 = LocalDistance * LocalDistance, sky2 = SkyDistance * SkyDistance;
            var background = new bool[w * h];
            var queue = new Queue<int>();
            foreach (int i in seeds)
            {
                if (background[i] || Distance2(px[i], sky) >= sky2) continue;
                background[i] = true;
                queue.Enqueue(i);
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                foreach (int n in Neighbors(x, y, w, h))
                {
                    if (background[n]) continue;
                    if (Distance2(px[i], px[n]) < local2 && Distance2(px[n], sky) < sky2)
                    {
                        background[n] = true;
                        queue.Enqueue(n);
                    }
                }
            }
            return background;
        }

        /// <summary>把小於 minSize 像素的前景碎塊拿掉</summary>
        private static void RemoveSmallParts(bool[] foreground, int w, int h, int minSize)
        {
            var seen = new bool[w * h];
            var part = new List<int>();
            var queue = new Queue<int>();

            for (int start = 0; start < foreground.Length; start++)
            {
                if (!foreground[start] || seen[start]) continue;

                part.Clear();
                seen[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    part.Add(i);
                    foreach (int n in Neighbors(i % w, i / w, w, h))
                    {
                        if (!foreground[n] || seen[n]) continue;
                        seen[n] = true;
                        queue.Enqueue(n);
                    }
                }

                if (part.Count < minSize)
                    foreach (int i in part) foreground[i] = false;
            }
        }

        private static IEnumerable<int> Neighbors(int x, int y, int w, int h)
        {
            if (x > 0) yield return y * w + x - 1;
            if (x < w - 1) yield return y * w + x + 1;
            if (y > 0) yield return (y - 1) * w + x;
            if (y < h - 1) yield return (y + 1) * w + x;
        }

        private static int Distance2(Rgba32 a, Rgba32 b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return dr * dr + dg * dg + db * db;
        }
    }
}
