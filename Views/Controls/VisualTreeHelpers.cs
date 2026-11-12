using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DPlayer.Views.Controls;

internal static class VisualTreeHelpers
{
    internal static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj is not null)
        {
            if (obj is T match) return match;

            // e.OriginalSource 可能是 Run/TextElement 等 ContentElement，不是 Visual；
            // 对它们调用 VisualTreeHelper.GetParent 会抛 InvalidOperationException。
            obj = obj switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(obj),
                ContentElement content => ContentOperations.GetParent(content)
                    ?? (content as FrameworkContentElement)?.Parent,
                _ => null,
            };
        }
        return null;
    }

    internal static T? FindChildByName<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        if (parent is T match && match.Name == name)
            return match;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (FindChildByName<T>(VisualTreeHelper.GetChild(parent, i), name)
                is { } found)
                return found;
        }
        return null;
    }

    internal static int ComputeInsertIndex(ListBox listBox, Point posInListBox)
    {
        for (int i = 0; i < listBox.Items.Count; i++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i)
                is not ListBoxItem container) continue;
            var transform = container.TransformToAncestor(listBox);
            var topLeft = transform.Transform(new Point(0, 0));
            var rect = new Rect(topLeft,
                new Size(container.ActualWidth, container.ActualHeight));
            if (posInListBox.Y >= rect.Top && posInListBox.Y < rect.Bottom)
            {
                return posInListBox.Y < rect.Top + rect.Height / 2
                    ? i : i + 1;
            }
        }
        return listBox.Items.Count;
    }

    internal static void ShowAdorner(
        ListBox listBox, ref DropInsertionAdorner? adorner, int insertIndex)
    {
        var layer = AdornerLayer.GetAdornerLayer(listBox);
        if (layer is null) return;
        if (adorner is null)
        {
            adorner = new DropInsertionAdorner(listBox);
            layer.Add(adorner);
        }
        adorner.Update(insertIndex);
    }

    internal static void HideAdorner(
        ListBox listBox, ref DropInsertionAdorner? adorner)
    {
        if (adorner is null) return;
        var layer = AdornerLayer.GetAdornerLayer(listBox);
        layer?.Remove(adorner);
        adorner = null;
    }
}
