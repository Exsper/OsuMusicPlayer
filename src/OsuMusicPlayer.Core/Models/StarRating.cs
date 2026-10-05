namespace OsuMusicPlayer.Core.Models;

/// <summary>
/// 一组 mod 组合下的星数（键为 osu! 的 mod 位掩码）。
/// 参考 CollectionManager（MIT）的 StarRating 实现改写。
/// </summary>
public sealed class StarRating
{
    private readonly SortedDictionary<int, float> _values = [];

    public int Count => _values.Count;

    public IEnumerable<KeyValuePair<int, float>> Entries => _values;

    public bool ContainsKey(int mods) => _values.ContainsKey(mods);

    /// <summary>不存在时返回 -1（与 osu! 客户端的“无数据”约定一致）。</summary>
    public float this[int mods]
    {
        get => _values.TryGetValue(mods, out float value) ? value : -1f;
        set => _values[mods] = value;
    }

    public void Add(int mods, float stars) => this[mods] = stars;
}
