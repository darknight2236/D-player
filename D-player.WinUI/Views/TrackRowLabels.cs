namespace DPlayer.WinUI.Views;

/// <summary>
/// 曲目行 UIA 名称构造（spec §7 C5）：把标题、艺术家、时长拼成屏幕阅读器可读的文本。
/// null/空白字段回落成中文占位（"未知曲目"/"未知艺术家"），时长走 mm:ss 格式。
/// 供 TrackList 的 ContainerContentChanging 回调使用（x:Bind 静态函数绑定在 WinUI 3 的
/// XamlCompiler 上会报 WMC 错误，实测不可用，故走代码后置赋值路线）。
/// </summary>
internal static class TrackRowLabels
{
    public static string Build(string? title, string? artist, TimeSpan duration)
    {
        var t = string.IsNullOrWhiteSpace(title) ? "未知曲目" : title;
        var a = string.IsNullOrWhiteSpace(artist) ? "未知艺术家" : artist;
        return $"{t}，{a}，{duration:mm\\:ss}";
    }
}
