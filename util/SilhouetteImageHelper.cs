using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace backend.util
{
    public static class SilhouetteImageHelper
    {
        public static void CreateThresholdSilhouette(
            string inputPath,
            string outputPath,
            byte threshold)
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(inputPath);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);

                    for (int x = 0; x < row.Length; x++)
                    {
                        Rgba32 pixel = row[x];

                        if (pixel.A == 0)
                        {
                            continue;
                        }

                        byte brightness = (byte)Math.Clamp(
                            (int)Math.Round(
                                pixel.R * 0.299 +
                                pixel.G * 0.587 +
                                pixel.B * 0.114),
                            0,
                            255);

                        row[x] = brightness < threshold
                            ? new Rgba32(0, 0, 0, pixel.A)
                            : new Rgba32(255, 255, 255, pixel.A);
                    }
                }
            });

            string directory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            image.SaveAsPng(outputPath);
        }
    }
}
