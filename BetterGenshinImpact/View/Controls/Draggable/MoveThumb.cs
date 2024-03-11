using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls;

public class MoveThumb : Thumb
{
    private RotateTransform? rotateTransform;
    private ContentControl? designerItem;

    public MoveThumb()
    {
        DragStarted += OnMoveThumbDragStarted;
        DragDelta += OnMoveThumbDragDelta;
    }

    private void OnMoveThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        designerItem = DataContext as ContentControl;

        if (designerItem != null)
        {
            rotateTransform = designerItem.RenderTransform as RotateTransform;
        }
    }

    private void OnMoveThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (designerItem is not null)
        {
            Point dragDelta = new(e.HorizontalChange, e.VerticalChange);

            if (rotateTransform is not null)
            {
                dragDelta = rotateTransform.Transform(dragDelta);
            }

            Canvas.SetLeft(designerItem, Canvas.GetLeft(designerItem) + dragDelta.X);
            Canvas.SetTop(designerItem, Canvas.GetTop(designerItem) + dragDelta.Y);
        }
    }
}
