using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace DPlayer.WinUI.Theme;

/// <summary>
/// 动效令牌：只有三档时长（spec §4），任何动画 ≤250ms 且一次性、无循环。
/// 用 C# 常量而非 XAML 资源——WinUI 的 XAML 没有可靠的 TimeSpan 资源类型，
/// Storyboard.Duration 从字符串资源转换会失败（实测路线见 Task 1 Step 1 报告）。
/// </summary>
internal static class MotionTokens
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(200);

    /// <summary>Fluent 标准缓动（与系统过渡同源）。</summary>
    public static readonly EasingFunctionBase StandardEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static Duration D(TimeSpan t) => new(t);
}
