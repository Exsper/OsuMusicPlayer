namespace OsuMusicPlayer.Core.Services;

using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;

/// <summary>搜索范围。</summary>
public enum SearchField
{
    /// <summary>标题、艺术家、谱师、标签、来源、文件夹、难度名等全部字段。</summary>
    All = 0,
    Title = 1,
    Artist = 2,
    Creator = 3,
    Tags = 4,
    Difficulty = 5,
}

/// <summary>一次搜索的条件。</summary>
public sealed record TrackSearchQuery
{
    /// <summary>关键词，多个关键词用空格分隔（全部满足）；引号内视为整体；<c>-词</c> 表示排除。</summary>
    public string Text { get; init; } = string.Empty;

    public SearchField Field { get; init; } = SearchField.All;

    /// <summary>只保留音频文件存在的曲目。</summary>
    public bool OnlyPlayable { get; init; }

    public PlayMode? PlayMode { get; init; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && !OnlyPlayable && PlayMode is null;
}

/// <summary>把关键词解析出的一个检索片段。</summary>
public sealed record SearchToken(string Value, bool Negated, SearchField? Field, bool Exact = false);

/// <summary>音乐库的关键词搜索。</summary>
public static class TrackSearcher
{
    private static readonly Dictionary<string, SearchField> _fieldPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = SearchField.Title,
        ["t"] = SearchField.Title,
        ["artist"] = SearchField.Artist,
        ["a"] = SearchField.Artist,
        ["creator"] = SearchField.Creator,
        ["mapper"] = SearchField.Creator,
        ["c"] = SearchField.Creator,
        ["tag"] = SearchField.Tags,
        ["tags"] = SearchField.Tags,
        ["source"] = SearchField.Tags,
        ["diff"] = SearchField.Difficulty,
        ["difficulty"] = SearchField.Difficulty,
        ["d"] = SearchField.Difficulty,
    };

    public static IReadOnlyList<MusicTrack> Search(IEnumerable<MusicTrack> source, TrackSearchQuery query)
    {
        if (query.IsEmpty)
        {
            return source as IReadOnlyList<MusicTrack> ?? [.. source];
        }

        IReadOnlyList<SearchToken> tokens = ParseTokens(query.Text);
        List<MusicTrack> result = [];

        foreach (MusicTrack track in source)
        {
            if (query.OnlyPlayable && !track.AudioFileExists)
            {
                continue;
            }

            if (query.PlayMode is { } mode && track.PlayMode != mode)
            {
                continue;
            }

            if (Matches(track, tokens, query.Field))
            {
                result.Add(track);
            }
        }

        return result;
    }

    public static bool Matches(MusicTrack track, IEnumerable<SearchToken> tokens, SearchField defaultField = SearchField.All)
    {
        foreach (SearchToken token in tokens)
        {
            bool hit = token.Exact
                ? MatchesIdentifier(track, token.Value)
                : GetFieldText(track, token.Field ?? defaultField).Contains(token.Value, StringComparison.OrdinalIgnoreCase);

            if (hit == token.Negated)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesIdentifier(MusicTrack track, string value)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int id))
        {
            return false;
        }

        return track.MapSetId == id || track.PrimaryMapId == id || track.BeatmapIds.Contains(id);
    }

    /// <summary>解析关键词：支持引号、<c>字段:值</c> 前缀与 <c>-排除</c>。</summary>
    public static IReadOnlyList<SearchToken> ParseTokens(string? text)
    {
        List<SearchToken> tokens = [];

        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        foreach (string raw in SplitRespectingQuotes(text))
        {
            if (raw.Length == 0)
            {
                continue;
            }

            bool negated = raw[0] == '-' && raw.Length > 1;
            string token = negated ? raw[1..] : raw;

            int separator = token.IndexOf(':');

            if (separator > 0 && separator < token.Length - 1)
            {
                string prefix = token[..separator];

                if (_fieldPrefixes.TryGetValue(prefix, out SearchField field))
                {
                    tokens.Add(new SearchToken(token[(separator + 1)..], negated, field));
                    continue;
                }

                if (prefix.Equals("set", StringComparison.OrdinalIgnoreCase) || prefix.Equals("id", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new SearchToken(token[(separator + 1)..], negated, null, Exact: true));
                    continue;
                }
            }

            tokens.Add(new SearchToken(token, negated, null));
        }

        return tokens;
    }

    /// <summary>按空格切分，双引号内的空格保留（引号本身被去掉）。</summary>
    internal static IEnumerable<string> SplitRespectingQuotes(string text)
    {
        System.Text.StringBuilder builder = new();
        bool inQuotes = false;

        foreach (char c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }

                continue;
            }

            builder.Append(c);
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    /// <summary>取某个检索范围的文本（除“全部”外都不含文件夹名，避免误命中）。</summary>
    public static string GetFieldText(MusicTrack track, SearchField field) => field switch
    {
        SearchField.Title => track.Title + "\n" + track.TitleUnicode,
        SearchField.Artist => track.Artist + "\n" + track.ArtistUnicode,
        SearchField.Creator => track.Creator,
        SearchField.Tags => track.Tags + "\n" + track.Source,
        SearchField.Difficulty => string.Join('\n', track.Difficulties),
        _ => track.SearchIndex,
    };
}
