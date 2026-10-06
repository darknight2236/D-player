namespace DPlayer.Models;

/// <summary>
/// 频谱分析配置
/// </summary>
public record SpectrumConfig
{
    /// <summary>启用频谱分析</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>频谱柱数量</summary>
    public int BarCount { get; init; } = 32;

    /// <summary>灵敏度 (0.5 ~ 2.0)</summary>
    public double Sensitivity { get; init; } = 1.0;

    /// <summary>平滑度 (0.0 ~ 0.95)</summary>
    public double Smoothing { get; init; } = 0.8;

    /// <summary>FFT 大小（采样点数）</summary>
    public int FftSize { get; init; } = 8192;
}
