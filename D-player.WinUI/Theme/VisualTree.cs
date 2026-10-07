using Microsoft.UI.Xaml;

namespace DPlayer.WinUI.Theme;

/// <summary>
/// 可视化树查找：全壳唯一实现（原来散在 MainWindow.xaml.cs 里，拆成四个控件后多处都要用，
/// 所以搬到这里来，**搬家时不改一行逻辑**——TrackList 用它找 ListViewItem 容器、
/// PlayerBar 用它找 Slider 模板里的 Thumb 与轨道）。
/// </summary>
internal static class VisualTree
{
    public static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj is not null)
        {
            if (obj is T match) return match;
            obj = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0, n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>
    /// 全量后代（深度优先，迭代器）。与上面单命中的 <see cref="FindDescendant{T}"/> 并存、不改它。
    /// 用在左栏折叠：条目里的名称标签每行一份、住在 DataTemplate 里，声明式之后没有 namescope 可按名字取，
    /// 只能从根往下收再按 Tag 筛（调用方见 Views/NavRail.xaml.cs 的 CollectFadeTargets）。
    /// </summary>
    public static IEnumerable<DependencyObject> FindDescendants(DependencyObject root)
    {
        for (int i = 0, n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in FindDescendants(child)) yield return deeper;
        }
    }
}
