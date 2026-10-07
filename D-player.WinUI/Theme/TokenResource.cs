using Microsoft.UI.Xaml;

namespace DPlayer.WinUI.Theme;

/// <summary>
/// 读 Theme/Tokens.xaml 里的数值令牌。
///
/// 为什么要有这个方法：折叠动画的端点宽度、自动收起阈值、封面解码边长都必须和 XAML 用**同一个数**，
/// 否则改了 Tokens.xaml 只有静态布局跟着变、代码里的端点留在旧值（硬约束"尺寸集中在 Tokens"的另一半）。
/// 而 WinUI 不进任何门禁、键名拼错编译期发现不了，所以把读取收在一处、缺键时给出能读懂的异常，
/// 而不是让 <c>(double)Application.Current.Resources[key]</c> 在运行期抛一个 NullReferenceException。
/// </summary>
internal static class TokenResource
{
    public static double Double(string key)
        => Application.Current.Resources.TryGetValue(key, out var value)
            ? (double)value
            : throw new InvalidOperationException($"Theme/Tokens.xaml 里没有数值令牌 '{key}'");
}
