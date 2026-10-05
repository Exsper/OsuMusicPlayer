namespace OsuMusicPlayer.Core.IO;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>JSON 文件读写（原子写入：先写临时文件再替换）。</summary>
public static class JsonFile
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>读取 JSON；文件不存在时返回 null，内容损坏时抛出 <see cref="InvalidDataException"/>。</summary>
    public static T? Load<T>(string path) where T : class
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);

            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"配置文件 \"{path}\" 内容损坏：{ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidDataException($"无法读取配置文件 \"{path}\"：{ex.Message}", ex);
        }
    }

    public static void Save<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(value, Options);
        string temporaryPath = path + ".tmp";

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
