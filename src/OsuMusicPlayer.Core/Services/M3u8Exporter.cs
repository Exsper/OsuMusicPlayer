namespace OsuMusicPlayer.Core.Services;

using System.Globalization;
using System.Text;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;

/// <summary>把曲目列表导出为 M3U8 播放列表（可被 foobar2000、VLC 等播放器直接打开）。</summary>
public static class M3u8Exporter
{
    public static string Build(IEnumerable<MusicTrack> tracks, TrackNameDisplay nameDisplay = TrackNameDisplay.Romanized)
    {
        StringBuilder builder = new();
        builder.AppendLine("#EXTM3U");

        foreach (MusicTrack track in tracks)
        {
            int seconds = track.Duration is { } duration && duration > TimeSpan.Zero
                ? (int)Math.Round(duration.TotalSeconds)
                : track.TotalTime > 0 ? (int)Math.Round(track.TotalTime / 1000d) : -1;

            builder.Append("#EXTINF:")
                .Append(seconds.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(track.GetDisplayArtist(nameDisplay))
                .Append(" - ")
                .Append(track.GetDisplayTitle(nameDisplay))
                .AppendLine();

            builder.AppendLine(track.AudioFilePath);
        }

        return builder.ToString();
    }

    public static void Write(IEnumerable<MusicTrack> tracks, string path, TrackNameDisplay nameDisplay = TrackNameDisplay.Romanized)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Build(tracks, nameDisplay), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
