namespace OsuMusicPlayer.SampleData;

/// <summary>生成未压缩的 24 位 BMP 图片（示例数据的谱面背景，避免依赖图像库）。</summary>
public static class BmpWriter
{
    public static void WriteGradient(string path, int width, int height, byte baseRed, byte baseGreen, byte baseBlue)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        int rowSize = ((width * 3) + 3) & ~3;
        int pixelDataSize = rowSize * height;
        int fileSize = 54 + pixelDataSize;

        using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
        using BinaryWriter writer = new(stream, System.Text.Encoding.ASCII);

        // BITMAPFILEHEADER
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(54);

        // BITMAPINFOHEADER
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelDataSize);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        byte[] row = new byte[rowSize];

        for (int y = height - 1; y >= 0; y--)
        {
            Array.Clear(row);

            for (int x = 0; x < width; x++)
            {
                int offset = x * 3;
                double factor = 0.45 + (0.55 * y / Math.Max(1, height - 1));
                double diagonal = 0.75 + (0.25 * x / Math.Max(1, width - 1));

                row[offset] = Clamp(baseBlue * factor * diagonal);
                row[offset + 1] = Clamp(baseGreen * factor * diagonal);
                row[offset + 2] = Clamp(baseRed * factor * diagonal);
            }

            writer.Write(row);
        }
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
