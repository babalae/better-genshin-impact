using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;

namespace Vision.Recognition.Controls
{
    ///
    /// ----------------------------------------------------------------
    /// Copyright @BigWang 2023 All rights reserved
    /// Author      : BigWang
    /// Created Time: 2023/6/22 21:19:04
    /// Description :
    /// ----------------------------------------------------------------
    /// Version      Modified Time              Modified By     Modified Content
    /// V1.0.0.0     2023/6/22 21:19:04                     BigWang         首次编写         
    ///
    public class ElementAdorner : Adorner
    {
        // 委托事件
        public delegate void DelegateEventHandle(bool isActive);
        public event DelegateEventHandle OnUpdateEvent;

        private const double RotateThumbSize = 20;
        private const double AdonerThumbSize = 0.5 * RotateThumbSize;
        private const double ElementMiniSize = RotateThumbSize;

        private readonly Thumb tMove;
        private readonly Thumb tLine;
        private readonly Thumb tBorder;
        private readonly Thumb tLeftUp;
        private readonly Thumb tRightUp;
        private readonly Thumb tLeftBottom;
        private readonly Thumb tRightBottom;
        private readonly Thumb tLeft;
        private readonly Thumb tRight;
        private readonly Thumb tUp;
        private readonly Thumb tBottom;
        private readonly VisualCollection visualCollection;

        public double StrokeThickness { get; set; } = 2;
        public bool IsActive { get; set; } = false;


        private readonly Thumb tRotate;
        private Canvas canvas;
        private Point centerPoint;
        private FrameworkElement designerItem;
        private double initialAngle;
        private RotateTransform rotateTransform;
        private Vector startVector;

        public ElementAdorner(UIElement adornedElement) : base(adornedElement)
        {
            visualCollection = new VisualCollection(this)
            {
                (tMove = CreateMoveThumb()),
                (tLine = CreateLineThumb()),
                (tBorder = CreateBorderThumb()),
                ( tRotate = CreateRotateThumb()),

                (tLeftUp = CreateResizeThumb(Cursors.SizeNWSE, HorizontalAlignment.Left, VerticalAlignment.Top)),
                (tRightUp = CreateResizeThumb(Cursors.SizeNESW, HorizontalAlignment.Right, VerticalAlignment.Top)),
                (tLeftBottom = CreateResizeThumb(Cursors.SizeNESW, HorizontalAlignment.Left, VerticalAlignment.Bottom)),
                (tRightBottom = CreateResizeThumb(Cursors.SizeNWSE, HorizontalAlignment.Right, VerticalAlignment.Bottom)),

                (tLeft = CreateResizeThumb(Cursors.SizeWE, HorizontalAlignment.Left, VerticalAlignment.Stretch)),
                (tUp = CreateResizeThumb(Cursors.SizeNS, HorizontalAlignment.Stretch, VerticalAlignment.Top)),
                (tRight = CreateResizeThumb(Cursors.SizeWE, HorizontalAlignment.Right, VerticalAlignment.Stretch)),
                (tBottom = CreateResizeThumb(Cursors.SizeNS, HorizontalAlignment.Stretch, VerticalAlignment.Bottom)),
            };
            SetThumbVisibility(Visibility.Collapsed);
        }

        public void SetThumbVisibility(Visibility visibility)
        {
            tLine.Visibility = visibility;
            tBorder.Visibility = visibility;
            tRotate.Visibility = visibility;
            tLeftUp.Visibility = visibility;
            tRightUp.Visibility = visibility;
            tLeftBottom.Visibility = visibility;
            tRightBottom.Visibility = visibility;
            tLeft.Visibility = visibility;
            tRight.Visibility = visibility;
            tUp.Visibility = visibility;
            tBottom.Visibility = visibility;
        }

        protected override int VisualChildrenCount => visualCollection.Count;

        protected override Visual GetVisualChild(int index) => visualCollection[index];

        protected override void OnRender(DrawingContext drawingContext)
        {
            double offset = AdonerThumbSize / 2;
            // 考虑线宽
            double d = 0.5 * StrokeThickness;
            Size sz = new(AdonerThumbSize, AdonerThumbSize);
            tLine.Arrange(new Rect(new Point(AdornedElement.RenderSize.Width / 2 - d, -RotateThumbSize), new Size(2, RotateThumbSize)));
            tMove.Arrange(new Rect(new Point(0, 0), new Size(AdornedElement.RenderSize.Width, AdornedElement.RenderSize.Height)));
            tBorder.Arrange(new Rect(new Point(0, 0), new Size(AdornedElement.RenderSize.Width, AdornedElement.RenderSize.Height)));
            tRotate.Arrange(new Rect(new Point(AdornedElement.RenderSize.Width / 2 - 10, -RotateThumbSize), new Size(20, 20)));

            tLeftUp.Arrange(new Rect(new Point(-offset + d, -offset + d), sz));
            tRightUp.Arrange(new Rect(new Point(AdornedElement.RenderSize.Width - offset - d, -offset + d), sz));
            tLeftBottom.Arrange(new Rect(new Point(-offset + d, AdornedElement.RenderSize.Height - offset - d), sz));
            tRightBottom.Arrange(new Rect(new Point(AdornedElement.RenderSize.Width - offset - d, AdornedElement.RenderSize.Height - offset - d), sz));

            tLeft.Arrange(new Rect(new Point(-offset + d, AdornedElement.RenderSize.Height / 2 - offset), sz));
            tUp.Arrange(new Rect(new Point(AdornedElement.RenderSize.Width / 2 - offset, -offset + d), sz));
            tRight.Arrange(new Rect(new Point(AdornedElement.RenderSize.Width - offset - d, AdornedElement.RenderSize.Height / 2 - offset), sz));
            tBottom.Arrange(new Rect(new Point(AdornedElement.RenderSize.Width / 2 - offset, AdornedElement.RenderSize.Height - offset - d), sz));
        }

        /// <summary>
        /// 平移：透明色
        /// </summary>
        /// <returns></returns>
        private Thumb CreateMoveThumb()
        {
            Thumb thumb = new()
            {
                Cursor = Cursors.SizeAll,
                Template = new ControlTemplate(typeof(Thumb))
                {
                    VisualTree = GetFactoryMove()
                },
            };

            thumb.DragStarted += (s, e) =>
            {
                IsActive = true;
                SetThumbVisibility(Visibility.Visible);
                // 委托事件
                //OnUpdateEvent?.Invoke(IsActive);
                // 强制刷新下
                InvalidateVisual();
            };

            thumb.DragDelta += (s, e) =>
            {
                if (AdornedElement is FrameworkElement element && IsActive)
                {
                    Canvas.SetLeft(element, Canvas.GetLeft(element) + e.HorizontalChange);
                    Canvas.SetTop(element, Canvas.GetTop(element) + e.VerticalChange);
                }
            };

            thumb.MouseEnter += (s, e) =>
            {
                tBorder.Visibility = Visibility.Visible;
                // 强制刷新
                InvalidateVisual();
            };

            thumb.MouseLeave += (s, e) =>
            {
                if (!IsActive)
                {
                    SetThumbVisibility(Visibility.Collapsed);
                    // 强制刷新
                    InvalidateVisual();
                }
            };

            thumb.MouseRightButtonDown += (s, e) =>
            {
                IsActive = false;
                SetThumbVisibility(Visibility.Collapsed);
                //OnUpdateEvent?.Invoke(IsActive);
            };

            return thumb;
        }

        /// <summary>
        /// 边框
        /// </summary>
        /// <returns></returns>
        private Thumb CreateBorderThumb()
        {
            Thumb thumb = new()
            {
                Template = new ControlTemplate(typeof(Thumb))
                {
                    VisualTree = GetFactoryBorder()
                },
            };
            return thumb;
        }

        /// <summary>
        /// 缩放：8 个标记点
        /// </summary>
        /// <param name="cursor">鼠标</param>
        /// <param name="horizontal">水平</param>
        /// <param name="vertical">垂直</param>
        /// <returns></returns>
        private Thumb CreateResizeThumb(Cursor cursor, HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            Thumb thumb = new()
            {
                Cursor = cursor,
                Width = AdonerThumbSize,
                Height = AdonerThumbSize,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Template = new ControlTemplate(typeof(Thumb))
                {
                    VisualTree = GetFactoryResize(),
                },
            };

            thumb.DragDelta += (s, e) =>
            {
                if (AdornedElement is not FrameworkElement element)
                {
                    return;
                }

                Resize(element);
                if (thumb.VerticalAlignment == VerticalAlignment.Bottom)
                {
                    if (element.Height + e.VerticalChange > ElementMiniSize)
                    {
                        element.Height += e.VerticalChange;
                    }
                }
                else if (thumb.VerticalAlignment == VerticalAlignment.Top)
                {
                    if (element.Height - e.VerticalChange > ElementMiniSize)
                    {
                        element.Height -= e.VerticalChange;
                        Canvas.SetTop(element, Canvas.GetTop(element) + e.VerticalChange);
                    }
                }

                if (thumb.HorizontalAlignment == HorizontalAlignment.Left)
                {
                    if (element.Width - e.HorizontalChange > ElementMiniSize)
                    {
                        element.Width -= e.HorizontalChange;
                        Canvas.SetLeft(element, Canvas.GetLeft(element) + e.HorizontalChange);
                    }
                }
                else if (thumb.HorizontalAlignment == HorizontalAlignment.Right)
                {
                    if (element.Width + e.HorizontalChange > ElementMiniSize)
                    {
                        element.Width += e.HorizontalChange;
                    }
                }
            };

            return thumb;
        }

        /// <summary>
        /// 连接线
        /// </summary>
        /// <returns></returns>
        private Thumb CreateLineThumb()
        {
            Thumb thumb = new()
            {
                Width = 2,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Template = new ControlTemplate(typeof(Thumb))
                {
                    VisualTree = GetFactoryLine(),
                },
            };
            return thumb;
        }

        /// <summary>
        /// 旋转
        /// </summary>
        /// <returns></returns>
        private Thumb CreateRotateThumb()
        {
            Thumb thumb = new Thumb()
            {
                Cursor = Cursors.Hand,
                Width = RotateThumbSize,
                Height = RotateThumbSize,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -RotateThumbSize, 0, 0),
                Template = new ControlTemplate(typeof(Thumb))
                {
                    VisualTree = GetFactoryRotate(),
                },
            };
            thumb.DragDelta += Thumb_DragDelta;
            thumb.DragStarted += Thumb_DragStarted;
            return thumb;
        }

        private void Resize(FrameworkElement fElement)
        {
            if (double.IsNaN(fElement.Width))
            {
                fElement.Width = fElement.RenderSize.Width;
            }

            if (double.IsNaN(fElement.Height))
            {
                fElement.Height = fElement.RenderSize.Height;
            }
        }

        /// <summary>
        /// 缩放：8 个控制点
        /// </summary>
        /// <returns></returns>
        private FrameworkElementFactory GetFactoryResize()
        {
            FrameworkElementFactory elementFactory = new(typeof(Rectangle));
            elementFactory.SetValue(Shape.StrokeProperty, Brushes.OrangeRed);
            elementFactory.SetValue(Shape.StrokeThicknessProperty, 2d);
            elementFactory.SetValue(Shape.FillProperty, Brushes.Bisque);
            return elementFactory;
        }

        /// <summary>
        /// 边框
        /// </summary>
        /// <returns></returns>
        private FrameworkElementFactory GetFactoryBorder()
        {
            FrameworkElementFactory elementFactory = new(typeof(Rectangle));
            elementFactory.SetValue(Shape.StrokeProperty, Brushes.OrangeRed);
            elementFactory.SetValue(Shape.StrokeThicknessProperty, 2d);
            elementFactory.SetValue(Shape.StrokeDashArrayProperty, new DoubleCollection { 4, 2 });
            elementFactory.SetValue(Shape.FillProperty, Brushes.Bisque);
            elementFactory.SetValue(Shape.IsHitTestVisibleProperty, false);
            return elementFactory;
        }

        /// <summary>
        /// 平移
        /// </summary>
        /// <returns></returns>
        private FrameworkElementFactory GetFactoryMove()
        {
            FrameworkElementFactory elementFactory = new(typeof(Rectangle));
            elementFactory.SetValue(Shape.FillProperty, Brushes.Transparent);
            return elementFactory;
        }

        /// <summary>
        /// 连接线
        /// </summary>
        /// <returns></returns>
        private FrameworkElementFactory GetFactoryLine()
        {
            FrameworkElementFactory elementFactory = new(typeof(Rectangle));
            elementFactory.SetValue(Shape.FillProperty, Brushes.Bisque);
            return elementFactory;
        }

        /// <summary>
        /// 旋转
        /// </summary>
        /// <returns></returns>
        private FrameworkElementFactory GetFactoryRotate()
        {
            //DrawingImage image = (DrawingImage)Application.Current.FindResource("Image_Rotate");
            //TileBrush tileBrush = new ImageBrush(image);
            var elementFactory = new FrameworkElementFactory(typeof(Ellipse));
            elementFactory.SetValue(Shape.FillProperty, Brushes.BlueViolet);
            return elementFactory;
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (designerItem != null && canvas != null)
            {
                var currentPoint = Mouse.GetPosition(canvas);
                var deltaVector = Point.Subtract(currentPoint, centerPoint);

                var angle = Vector.AngleBetween(startVector, deltaVector);

                var rotateTransform = designerItem.RenderTransform as RotateTransform;
                rotateTransform.Angle = initialAngle + Math.Round(angle, 0);
                designerItem.InvalidateMeasure();
            }
        }

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            var thumb = sender as Thumb;
            //designerItem = thumb.DataContext as FrameworkElement;
            designerItem = AdornedElement as FrameworkElement;
            canvas = VisualTreeHelper.GetParent(designerItem) as Canvas;
            if (canvas != null)
            {
                centerPoint = designerItem.TranslatePoint(
                    new Point(designerItem.Width * designerItem.RenderTransformOrigin.X,
                        designerItem.Height * designerItem.RenderTransformOrigin.Y),
                    canvas);

                var startPoint = Mouse.GetPosition(canvas);
                startVector = Point.Subtract(startPoint, centerPoint);

                rotateTransform = designerItem.RenderTransform as RotateTransform;
                if (rotateTransform == null)
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
}
