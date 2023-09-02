using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Vision.Recognition.Controls
{
    public class RotateThumb : Thumb
    {
        private double initialAngle;
        private RotateTransform rotateTransform;
        private Vector startVector;
        private Point centerPoint;
        private ContentControl designerItem;
        private Canvas canvas;

        public RotateThumb()
        {
            DragDelta += new DragDeltaEventHandler(this.RotateThumb_DragDelta);
            DragStarted += new DragStartedEventHandler(this.RotateThumb_DragStarted);
        }

        private void RotateThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            this.designerItem = DataContext as ContentControl;

            if (this.designerItem != null)
            {
                this.canvas = VisualTreeHelper.GetParent(this.designerItem) as Canvas;

                if (this.canvas != null)
                {
                    this.centerPoint = this.designerItem.TranslatePoint(
                        new Point(this.designerItem.Width * this.designerItem.RenderTransformOrigin.X,
                                  this.designerItem.Height * this.designerItem.RenderTransformOrigin.Y),
                                  this.canvas);

                    Point startPoint = Mouse.GetPosition(this.canvas);
                    this.startVector = Point.Subtract(startPoint, this.centerPoint);

                    this.rotateTransform = this.designerItem.RenderTransform as RotateTransform;
                    if (this.rotateTransform == null)
                    {
                        this.designerItem.RenderTransform = new RotateTransform(0);
                        this.initialAngle = 0;
                    }
                    else
                    {
                        this.initialAngle = this.rotateTransform.Angle;
                    }
                }
            }
        }

        private void RotateThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (this.designerItem != null && this.canvas != null)
            {
                Point currentPoint = Mouse.GetPosition(this.canvas);
                Vector deltaVector = Point.Subtract(currentPoint, this.centerPoint);

                double angle = Vector.AngleBetween(this.startVector, deltaVector);

                RotateTransform rotateTransform = this.designerItem.RenderTransform as RotateTransform;
                rotateTransform.Angle = this.initialAngle + Math.Round(angle, 0);
                this.designerItem.InvalidateMeasure();
            }
        }
    }
}
