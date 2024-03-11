using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using BetterGenshinImpact.View.Controls.Adorners;

namespace BetterGenshinImpact.View.Controls;

public class ResizeThumb : Thumb
{
    private RotateTransform? rotateTransform;
    private double angle;
    private Adorner? adorner;
    private Point transformOrigin;
    private ContentControl? designerItem;
    private Canvas? canvas;

    public ResizeThumb()
    {
        DragStarted += OnResizeThumbDragStarted;
        DragDelta += OnResizeThumbDragDelta;
        DragCompleted += OnResizeThumbDragCompleted;
    }

    private void OnResizeThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        designerItem = DataContext as ContentControl;

        if (designerItem is not null)
        {
            canvas = VisualTreeHelper.GetParent(designerItem) as Canvas;

            if (canvas is not null)
            {
                transformOrigin = designerItem.RenderTransformOrigin;

                rotateTransform = designerItem.RenderTransform as RotateTransform;
                if (rotateTransform is not null)
                {
                    angle = rotateTransform.Angle * Math.PI / 180.0;
                }
                else
                {
                    angle = 0.0d;
                }

                AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(canvas);
                if (adornerLayer is not null)
                {
                    adorner = new SizeAdorner(designerItem);
                    adornerLayer.Add(adorner);
                }
            }
        }
    }

    private void OnResizeThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (designerItem is not null)
        {
            double deltaVertical, deltaHorizontal;

            switch (VerticalAlignment)
            {
                case VerticalAlignment.Bottom:
                    deltaVertical = Math.Min(-e.VerticalChange, designerItem.ActualHeight - designerItem.MinHeight);
                    Canvas.SetTop(designerItem, Canvas.GetTop(designerItem) + (transformOrigin.Y * deltaVertical * (1 - Math.Cos(-angle))));
                    Canvas.SetLeft(designerItem, Canvas.GetLeft(designerItem) - deltaVertical * transformOrigin.Y * Math.Sin(-angle));
                    designerItem.Height -= deltaVertical;
                    break;
                case VerticalAlignment.Top:
                    deltaVertical = Math.Min(e.VerticalChange, designerItem.ActualHeight - designerItem.MinHeight);
                    Canvas.SetTop(designerItem, Canvas.GetTop(designerItem) + deltaVertical * Math.Cos(-angle) + (transformOrigin.Y * deltaVertical * (1 - Math.Cos(-angle))));
                    Canvas.SetLeft(designerItem, Canvas.GetLeft(designerItem) + deltaVertical * Math.Sin(-angle) - (transformOrigin.Y * deltaVertical * Math.Sin(-angle)));
                    designerItem.Height -= deltaVertical;
                    break;
                default:
                    break;
            }

            switch (HorizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    deltaHorizontal = Math.Min(e.HorizontalChange, designerItem.ActualWidth - designerItem.MinWidth);
                    Canvas.SetTop(designerItem, Canvas.GetTop(designerItem) + deltaHorizontal * Math.Sin(angle) - transformOrigin.X * deltaHorizontal * Math.Sin(angle));
                    Canvas.SetLeft(designerItem, Canvas.GetLeft(designerItem) + deltaHorizontal * Math.Cos(angle) + (transformOrigin.X * deltaHorizontal * (1 - Math.Cos(angle))));
                    designerItem.Width -= deltaHorizontal;
                    break;
                case HorizontalAlignment.Right:
                    deltaHorizontal = Math.Min(-e.HorizontalChange, designerItem.ActualWidth - designerItem.MinWidth);
                    Canvas.SetTop(designerItem, Canvas.GetTop(designerItem) - transformOrigin.X * deltaHorizontal * Math.Sin(angle));
                    Canvas.SetLeft(designerItem, Canvas.GetLeft(designerItem) + (deltaHorizontal * transformOrigin.X * (1 - Math.Cos(angle))));
                    designerItem.Width -= deltaHorizontal;
                    break;
                default:
                    break;
            }
        }

        e.Handled = true;
    }

    private void OnResizeThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (adorner is not null)
        {
            AdornerLayer.GetAdornerLayer(canvas)?.Remove(adorner);
            adorner = null;
        }
    }
}
