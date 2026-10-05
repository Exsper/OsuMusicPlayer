namespace OsuMusicPlayer.Core.IO;

using System.Text;

/// <summary>
/// osu! 的二进制读取器：字符串带 0x0b 前缀标记，长度使用 7 位变长编码。
/// 参考 CollectionManager（MIT）的 OsuBinaryReader 改写。
/// </summary>
public class OsuBinaryReader : BinaryReader
{
    public OsuBinaryReader(Stream input) : base(input)
    {
    }

    public OsuBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
    {
    }

    public OsuBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen)
    {
    }

    /// <summary>读取 osu! 字符串：标记字节非 11 时返回空字符串。</summary>
    public override string ReadString() => ReadRawOsuString() ?? string.Empty;

    /// <summary>读取 osu! 字符串：标记字节非 11 时返回 null（写入回读模式需要区分）。</summary>
    protected string? ReadRawOsuString() => ReadByte() == 11 ? base.ReadString() : null;

    /// <summary>读取 osu! 保存的 .NET ticks 时间戳，越界时返回 <see cref="DateTime.MinValue"/>。</summary>
    public DateTime ReadDateTime()
    {
        long ticks = ReadInt64();

        return ticks < 0L || ticks > DateTime.MaxValue.Ticks
            ? DateTime.MinValue
            : new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>读取带类型标记的变体值（osu!.db 中的星数数组使用）。</summary>
    public object? ReadConditionalValue()
    {
        switch (ReadByte())
        {
            case 1: return ReadBoolean();
            case 2: return ReadByte();
            case 3: return ReadUInt16();
            case 4: return ReadUInt32();
            case 5: return ReadUInt64();
            case 6: return ReadSByte();
            case 7: return ReadInt16();
            case 8: return ReadInt32();
            case 9: return ReadInt64();
            case 10: return ReadChar();
            case 11: return ReadString();
            case 12: return ReadSingle();
            case 13: return ReadDouble();
            case 14: return ReadDecimal();
            case 15: return ReadDateTime();
            case 16:
            {
                int length = ReadInt32();
                return length > 0 ? ReadBytes(length) : length < 0 ? null : Array.Empty<byte>();
            }

            case 17:
            {
                int length = ReadInt32();
                return length > 0 ? ReadChars(length) : length < 0 ? null : (object)Array.Empty<char>();
            }

            default: return null;
        }
    }
}
