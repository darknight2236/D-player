using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 队列内拖拽重排时，在 ListBox AdornerLayer 上画 1px 插入线（Phase 5）。
///
/// 颜色 = AccentPrimary（紫色，与主题一致），左右 4px 留白。
/// 位置由 _insertIndex 决定：
///   - insertIndex == Queue.Count → 画在最后一项底部
///   - 否则                       → 画在 Queue[insertIndex] 项顶部
///
/// 生命周期由 PlaylistView code-behind 集中管理 —— DragOver 时 Update + AdornerLayer.Add，
/// DragLeave/Drop/QueryContinueDrag 取消时 Hide + AdornerLayer.Remove。
/// </summary>
public sealed class DropInsertionAdorner : Adorner
{
    private readonly ListBox _listBox;
    private int _insertIndex;
    private readonly Pen _pen;

    public DropInsertionAdorner(ListBox listBox) : base(listBox)
    {
        _listBox = listBox;
        IsHitTestVisible = false; // 不拦截鼠标事件
        var brush = (Brush)Application.Current.FindResource("AccentPrimary");
        _pen = new Pen(brush, 2.0);
        _pen.Freeze();
    }

    /// <summary>更新插入位置；触发 InvalidateVisual 重画。</summary>
    public void Update(int insertIndex)
    {
        if (_insertIndex == insertIndex) return;
        _insertIndex = insertIndex;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_listBox.Items.Count == 0) return;

        double y;
        if (_insertIndex >= _listBox.Items.Count)
        {
            // 最后一项底部
            var lastIdx = _listBox.Items.Count - 1;
            if (_listBox.ItemContainerGenerator.ContainerFromIndex(lastIdx) is not ListBoxItem last) return;
            var rect = GetContainerRect(last);
            y = rect.Bottom;
        }
        else
        {
            if (_listBox.ItemContainerGenerator.ContainerFromIndex(_insertIndex) is not ListBoxItem container) return;
            var rect = GetContainerRect(container);
            y = rect.Top;
        }

        double left = 4;
        double right = _listBox.ActualWidth - 4;
        drawingContext.DrawLine(_pen, new Point(left, y), new Point(right, y));
    }

    private Rect GetContainerRect(ListBoxItem item)
    {
        // item 在 ListBox 坐标系下的 bounds
        var transform = item.TransformToAncestor(_listBox);
        var topLeft = transform.Transform(new Point(0, 0));
        return new Rect(topLeft, new Size(item.ActualWidth, item.ActualHeight));
    }
}
