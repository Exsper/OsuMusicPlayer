namespace OsuMusicPlayer.Core.IO;

using System.Text;

/// <summary>
/// osu! 的二进制写入器：字符串写 0x0b 前缀 + 7 位变长长度。
/// 参考 CollectionManager（MIT）的 OsuBinaryWriter 改写。
/// </summary>
public class OsuBinaryWriter : BinaryWriter
{
    public OsuBinaryWriter(Stream output) : base(output)
    {
    }

    public OsuBinaryWriter(Stream output, Encoding encoding) : base(output, encoding)
    {
    }

    public OsuBinaryWriter(Stream output, Encoding encoding, bool leaveOpen) : base(output, encoding, leaveOpen)
    {
    }

    public override void Write(string value)
    {
        if (value is null)
        {
            Write((byte)0);
            return;
        }

        Write((byte)11);
        base.Write(value);
    }

    public void Write(DateTime value) => Write(value.Ticks);

    public void Write(DateTimeOffset? value) => Write(value?.UtcDateTime.Ticks ?? 0L);
}
