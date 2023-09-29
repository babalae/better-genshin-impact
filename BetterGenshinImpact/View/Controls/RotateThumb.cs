using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls;

public class RotateThumb : Thumb
{
    private double initialAngle;
    private RotateTransform? rotateTransform;
    private Vector startVector;
    private Point centerPoint;
    private ContentControl? designerItem;
    private Canvas? canvas;

    public RotateThumb()
    {
        DragDelta += OnRotateThumbDragDelta;
        DragStarted += OnRotateThumbDragStarted;
    }

    private void OnRotateThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        designerItem = DataContext as ContentControl;

        if (designerItem is not null)
        {
            canvas = VisualTreeHelper.GetParent(designerItem) as Canvas;

            if (canvas is not null)
            {
                centerPoint = designerItem.TranslatePoint(
                    new Point(designerItem.Width * designerItem.RenderTransformOrigin.X,
                              designerItem.Height * designerItem.RenderTransformOrigin.Y),
                              canvas);

                Point startPoint = Mouse.GetPosition(canvas);
                startVector = Point.Subtract(startPoint, centerPoint);

                rotateTransform = designerItem.RenderTransform as RotateTransform;
                if (rotateTransform is null)
                {
                    designerItem.RenderTransform = new RotateTransform(0);
                    initialAngle = 0;
                }
                else
                {
                    initialAngle = rotateTransform.Angle;
                }
            }
        }
    }

    private void OnRotateThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (designerItem is not null && canvas is not null)
        {
            Point currentPoint = Mouse.GetPosition(canvas);
            Vector deltaVector = Point.Subtract(currentPoint, centerPoint);

            double angle = Vector.AngleBetween(startVector, deltaVector);

            RotateTransform? rotateTransform = designerItem.RenderTransform as RotateTransform;
            rotateTransform!.Angle = initialAngle + Math.Round(angle, 0);
            designerItem.InvalidateMeasure();
        }
    }
}
