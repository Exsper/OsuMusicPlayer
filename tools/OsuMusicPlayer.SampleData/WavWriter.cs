namespace OsuMusicPlayer.SampleData;

/// <summary>生成简单的 16 位 PCM WAV 音频（示例数据用，避免依赖任何编码器）。</summary>
public static class WavWriter
{
    /// <summary>写入一段指定频率的正弦波（带淡入淡出，避免爆音）。</summary>
    public static void WriteSine(
        string path,
        double frequency,
        TimeSpan duration,
        int sampleRate = 22050,
        double amplitude = 0.3)
    {
        int sampleCount = Math.Max(1, (int)(duration.TotalSeconds * sampleRate));
        short[] samples = new short[sampleCount];

        int fadeSamples = Math.Min(sampleCount / 4, sampleRate / 10);

        for (int i = 0; i < sampleCount; i++)
        {
            double t = (double)i / sampleRate;
            double value = Math.Sin(2 * Math.PI * frequency * t);

            double envelope = 1.0;

            if (fadeSamples > 0)
            {
                if (i < fadeSamples)
                {
                    envelope = (double)i / fadeSamples;
                }
                else if (i >= sampleCount - fadeSamples)
                {
                    envelope = (double)(sampleCount - i) / fadeSamples;
                }
            }

            samples[i] = (short)(value * envelope * amplitude * short.MaxValue);
        }

        WritePcm16Mono(path, samples, sampleRate);
    }

    public static void WritePcm16Mono(string path, short[] samples, int sampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
        using BinaryWriter writer = new(stream, System.Text.Encoding.ASCII);

        int dataLength = samples.Length * sizeof(short);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);                       // fmt 块大小
        writer.Write((short)1);                 // PCM
        writer.Write((short)1);                 // 单声道
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short)); // 字节率
        writer.Write((short)sizeof(short));       // 块对齐
        writer.Write((short)16);                // 位深

        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        foreach (short sample in samples)
        {
            writer.Write(sample);
        }
    }
}
