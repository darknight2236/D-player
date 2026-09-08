namespace DPlayer.Models;

/// <summary>
/// 均衡器运行时配置（Phase 14）。不可变 record，通过 `with` 更新。
/// 默认 Enabled=false（透明旁路），保证不影响存量用户既有听感。
/// </summary>
public sealed record EqualizerConfig
{
    /// <summary>启用 EQ；false 时 EqualizerSampleProvider 原样透传（零成本旁路）。</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>前置放大 dB，Clamp 到 [-12, 12]；多段提升时留余量防削波。</summary>
    public double PreampDb { get; init; } = 0;

    /// <summary>10 段增益 dB（顺序对应 EqualizerPresets.CenterFrequencies），各 Clamp 到 [-12, 12]。</summary>
    public IReadOnlyList<double> BandGainsDb { get; init; } = new double[EqualizerPresets.BandCount];

    /// <summary>当前预设名；手动偏离即 EqualizerPresets.Custom。</summary>
    public string Preset { get; init; } = EqualizerPresets.Flat;

    /// <summary>preamp 的线性增益系数（10^(dB/20)）。</summary>
    public float PreampLinearGain => (float)Math.Pow(10, PreampDb / 20.0);

    /// <summary>
    /// 工厂：把 preamp 与各段增益 Clamp 到 [-12, 12]，并规整段数（不足补 0，超出截断）。
    /// 用于把持久化/外部输入安全转为运行时配置。
    /// </summary>
    public static EqualizerConfig Create(
        bool enabled, double preampDb, IReadOnlyList<double>? bandGainsDb, string? preset)
    {
        var bands = new double[EqualizerPresets.BandCount];
        if (bandGainsDb != null)
        {
            for (int i = 0; i < bands.Length && i < bandGainsDb.Count; i++)
                bands[i] = Math.Clamp(bandGainsDb[i], EqualizerPresets.MinGainDb, EqualizerPresets.MaxGainDb);
        }
        return new EqualizerConfig
        {
            Enabled = enabled,
            PreampDb = Math.Clamp(preampDb, EqualizerPresets.MinGainDb, EqualizerPresets.MaxGainDb),
            BandGainsDb = bands,
            Preset = string.IsNullOrWhiteSpace(preset) ? EqualizerPresets.Flat : preset!
        };
    }
}
