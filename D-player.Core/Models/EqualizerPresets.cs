namespace DPlayer.Models;

/// <summary>
/// 均衡器频段常量与内置预设（Phase 14）。纯数据，无副作用，可脱离 UI 单测。
/// 预设曲线来源：经典播放器听感（可在验收期微调）。
/// </summary>
public static class EqualizerPresets
{
    /// <summary>频段数（10 段图形均衡器）。</summary>
    public const int BandCount = 10;

    /// <summary>单段/preamp 增益下限（dB）。</summary>
    public const double MinGainDb = -12;
    /// <summary>单段/preamp 增益上限（dB）。</summary>
    public const double MaxGainDb = 12;

    /// <summary>峰值滤波器 Q 因子（约一个倍频程带宽，图形 EQ 常用值）。</summary>
    public const float Q = 1.1f;

    /// <summary>"平直"预设名。</summary>
    public const string Flat = "Flat";
    /// <summary>用户手动偏离任何预设后的状态名（不作为可选择的下拉项）。</summary>
    public const string Custom = "Custom";

    /// <summary>ISO 倍频程中心频率（Hz），固定常量、不持久化；顺序对应 BandGainsDb。</summary>
    public static readonly IReadOnlyList<double> CenterFrequencies =
        [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>内置预设（有序，供 UI 下拉展示；不含 Custom）。数组视为只读，勿原地修改。</summary>
    public static readonly IReadOnlyList<(string Name, double[] Gains)> All =
    [
        (Flat,          [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        ("Rock",        [5, 4, 3, 1, -1, -1, 0, 2, 3, 4]),
        ("Pop",         [-1, 1, 3, 4, 3, 0, -1, -1, 1, 2]),
        ("Jazz",        [3, 2, 1, 2, -2, -2, 0, 1, 2, 3]),
        ("Classical",   [4, 3, 2, 0, -1, -1, 0, 1, 2, 3]),
        ("Dance",       [6, 4, 1, 0, -2, -2, 0, 1, 3, 4]),
        ("Bass Boost",  [6, 5, 4, 2, 0, 0, 0, 0, 0, 0]),
        ("Treble Boost",[0, 0, 0, 0, 0, 0, 1, 3, 5, 6]),
        ("Vocal",       [-2, -1, 0, 2, 4, 4, 3, 1, 0, -1]),
    ];

    /// <summary>可选预设名（下拉项，不含 Custom）。</summary>
    public static IReadOnlyList<string> Names { get; } = All.Select(p => p.Name).ToList();

    /// <summary>按名称查找预设曲线（返回副本）；找不到返回 false 且 gains 为全 0。</summary>
    public static bool TryGet(string name, out double[] gains)
    {
        foreach (var (n, g) in All)
        {
            if (n == name) { gains = (double[])g.Clone(); return true; }
        }
        gains = new double[BandCount];
        return false;
    }

    /// <summary>给定一组增益，返回精确匹配的预设名；无匹配返回 Custom。</summary>
    public static string Match(IReadOnlyList<double> gains)
    {
        foreach (var (n, g) in All)
        {
            if (gains.Count == g.Length && gains.SequenceEqual(g))
                return n;
        }
        return Custom;
    }
}
