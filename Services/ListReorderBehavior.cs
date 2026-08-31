using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BijouHub.Services;

/// <summary>
/// Adds mouse drag-to-reorder to a ListBox bound to an ObservableCollection, with a
/// floating insertion-line indicator showing where the dragged item will land.
/// </summary>
public static class ListReorderBehavior
{
    private class DropIndicatorAdorner : Adorner
    {
        private double _y;
        public DropIndicatorAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        public void SetY(double y)
        {
            _y = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var width = AdornedElement.RenderSize.Width;
            var lineBrush = new SolidColorBrush(Color.FromRgb(0x35, 0xC1, 0xF0));
            var dotBrush = new SolidColorBrush(Color.FromRgb(0x35, 0xC1, 0xF0));

            dc.DrawEllipse(dotBrush, null, new Point(6, _y), 4, 4);
            dc.DrawRectangle(lineBrush, null, new Rect(14, _y - 2, Math.Max(0, width - 20), 4));
            dc.DrawEllipse(dotBrush, null, new Point(width - 6, _y), 4, 4);
        }
    }

    public static void Enable<T>(ListBox listBox, ObservableCollection<T> items) where T : class
    {
        Point? dragStart = null;
        T? draggedItem = null;
        DropIndicatorAdorner? adorner = null;
        AdornerLayer? adornerLayer = null;

        listBox.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(listBox);
            draggedItem = GetContainerAtPoint(listBox, dragStart.Value)?.DataContext as T;
        };

        listBox.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragStart == null || draggedItem == null)
                return;

            var pos = e.GetPosition(listBox);
            if (Math.Abs(pos.Y - dragStart.Value.Y) < 6 && Math.Abs(pos.X - dragStart.Value.X) < 6)
                return;

            var toDrag = draggedItem;
            dragStart = null;
            draggedItem = null;
            DragDrop.DoDragDrop(listBox, toDrag!, DragDropEffects.Move);
        };

        void EnsureAdorner()
        {
            if (adornerLayer != null) return;
            adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
            if (adornerLayer == null) return;
            adorner = new DropIndicatorAdorner(listBox);
            adornerLayer.Add(adorner);
        }

        void RemoveAdorner()
        {
            if (adorner != null && adornerLayer != null)
                adornerLayer.Remove(adorner);
            adorner = null;
        }

        listBox.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(typeof(T))) return;
            e.Effects = DragDropEffects.Move;

            EnsureAdorner();
            if (adorner == null) return;

            var pos = e.GetPosition(listBox);
            var container = GetContainerAtPoint(listBox, pos);
            double y;
            if (container != null)
            {
                var top = container.TranslatePoint(new Point(0, 0), listBox).Y;
                var relY = e.GetPosition(container).Y;
                y = relY < container.ActualHeight / 2 ? top : top + container.ActualHeight;
            }
            else
            {
                y = listBox.ActualHeight;
            }
            adorner.SetY(y);
            e.Handled = true;
        };

        listBox.DragLeave += (_, _) => RemoveAdorner();

        listBox.Drop += (_, e) =>
        {
            RemoveAdorner();
            if (e.Data.GetData(typeof(T)) is not T dropped) return;

            var pos = e.GetPosition(listBox);
            var container = GetContainerAtPoint(listBox, pos);
            int targetIndex;

            if (container?.DataContext is T targetItem)
            {
                targetIndex = items.IndexOf(targetItem);
                var relY = e.GetPosition(container).Y;
                if (relY >= container.ActualHeight / 2)
                    targetIndex++;
            }
            else
            {
                targetIndex = items.Count;
            }

            int oldIndex = items.IndexOf(dropped);
            if (oldIndex < 0) return;
            if (targetIndex > oldIndex) targetIndex--;
            targetIndex = Math.Clamp(targetIndex, 0, items.Count - 1);

            items.Move(oldIndex, targetIndex);
            e.Handled = true;
        };

        listBox.AllowDrop = true;
    }

    private static ListBoxItem? GetContainerAtPoint(ListBox listBox, Point p)
    {
        var element = listBox.InputHitTest(p) as DependencyObject;
        while (element != null && element is not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);
        return element as ListBoxItem;
    }
}
