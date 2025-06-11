using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BetterGenshinImpact.View.Controls.Drawer;

public class CustomDrawer : ContentControl
{
    #region 依赖属性

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(CustomDrawer),
            new PropertyMetadata(false, OnIsOpenChanged));

    public static readonly DependencyProperty DrawerPositionProperty =
        DependencyProperty.Register(nameof(DrawerPosition), typeof(DrawerPosition), typeof(CustomDrawer),
            new PropertyMetadata(DrawerPosition.Right, OnDrawerPositionChanged));

    public static readonly DependencyProperty OpenWidthProperty =
        DependencyProperty.Register(nameof(OpenWidth), typeof(double), typeof(CustomDrawer),
            new PropertyMetadata(400.0));

    public static readonly DependencyProperty OpenHeightProperty =
        DependencyProperty.Register(nameof(OpenHeight), typeof(double), typeof(CustomDrawer),
            new PropertyMetadata(300.0));

    public static readonly DependencyProperty AnimationDurationProperty =
        DependencyProperty.Register(nameof(AnimationDuration), typeof(TimeSpan), typeof(CustomDrawer),
            new PropertyMetadata(TimeSpan.FromMilliseconds(200)));

    public static readonly DependencyProperty BackgroundOpacityProperty =
        DependencyProperty.Register(nameof(BackgroundOpacity), typeof(double), typeof(CustomDrawer),
            new PropertyMetadata(0.6));

    public static readonly DependencyProperty DrawerBackgroundProperty =
        DependencyProperty.Register(nameof(DrawerBackground), typeof(Brush), typeof(CustomDrawer),
            new PropertyMetadata(Brushes.Black));

    #endregion

    #region 属性

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public DrawerPosition DrawerPosition
    {
        get => (DrawerPosition)GetValue(DrawerPositionProperty);
        set => SetValue(DrawerPositionProperty, value);
    }

    public double OpenWidth
    {
        get => (double)GetValue(OpenWidthProperty);
        set => SetValue(OpenWidthProperty, value);
    }

    public double OpenHeight
    {
        get => (double)GetValue(OpenHeightProperty);
        set => SetValue(OpenHeightProperty, value);
    }

    public TimeSpan AnimationDuration
    {
        get => (TimeSpan)GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    public double BackgroundOpacity
    {
        get => (double)GetValue(BackgroundOpacityProperty);
        set => SetValue(BackgroundOpacityProperty, value);
    }

    public Brush DrawerBackground
    {
        get => (Brush)GetValue(DrawerBackgroundProperty);
        set => SetValue(DrawerBackgroundProperty, value);
    }

    #endregion

    private Border _backgroundOverlay;
    private Border _drawerContainer;
    private Grid _mainGrid;

    static CustomDrawer()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CustomDrawer),
            new FrameworkPropertyMetadata(typeof(CustomDrawer)));
    }

    public CustomDrawer()
    {
        this.Loaded += CustomDrawer_Loaded;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _mainGrid = GetTemplateChild("PART_MainGrid") as Grid;
        _backgroundOverlay = GetTemplateChild("PART_BackgroundOverlay") as Border;
        _drawerContainer = GetTemplateChild("PART_DrawerContainer") as Border;

        if (_backgroundOverlay != null)
        {
            _backgroundOverlay.MouseDown += BackgroundOverlay_MouseDown;
        }

        UpdateDrawerPosition();
        UpdateOpenState(false);
    }

    private void CustomDrawer_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateOpenState(false);
    }

    private void BackgroundOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        IsOpen = false;
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CustomDrawer drawer)
        {
            drawer.UpdateOpenState(true);
        }
    }

    private static void OnDrawerPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CustomDrawer drawer)
        {
            drawer.UpdateDrawerPosition();
            drawer.UpdateOpenState(false);
        }
    }

    private void UpdateDrawerPosition()
    {
        if (_drawerContainer == null) return;

        switch (DrawerPosition)
        {
            case DrawerPosition.Left:
                _drawerContainer.HorizontalAlignment = HorizontalAlignment.Left;
                _drawerContainer.VerticalAlignment = VerticalAlignment.Stretch;
                _drawerContainer.Width = OpenWidth;
                _drawerContainer.Height = double.NaN;
                break;
            case DrawerPosition.Right:
                _drawerContainer.HorizontalAlignment = HorizontalAlignment.Right;
                _drawerContainer.VerticalAlignment = VerticalAlignment.Stretch;
                _drawerContainer.Width = OpenWidth;
                _drawerContainer.Height = double.NaN;
                break;
            case DrawerPosition.Top:
                _drawerContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                _drawerContainer.VerticalAlignment = VerticalAlignment.Top;
                _drawerContainer.Width = double.NaN;
                _drawerContainer.Height = OpenHeight;
                break;
            case DrawerPosition.Bottom:
                _drawerContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                _drawerContainer.VerticalAlignment = VerticalAlignment.Bottom;
                _drawerContainer.Width = double.NaN;
                _drawerContainer.Height = OpenHeight;
                break;
        }
    }

    private void UpdateOpenState(bool animate)
    {
        if (_drawerContainer == null || _backgroundOverlay == null) return;

        _backgroundOverlay.IsHitTestVisible = IsOpen;
        
        _drawerContainer.Opacity = 1;

        if (IsOpen)
        {
            Visibility = Visibility.Visible;
        }

        if (animate)
        {
            // 动画背景遮罩
            DoubleAnimation backgroundAnimation = new DoubleAnimation
            {
                To = IsOpen ? BackgroundOpacity : 0,
                Duration = AnimationDuration
            };
            _backgroundOverlay.BeginAnimation(OpacityProperty, backgroundAnimation);

            // 确保RenderTransform已设置
            if (_drawerContainer.RenderTransform == null || !(_drawerContainer.RenderTransform is TranslateTransform))
            {
                _drawerContainer.RenderTransform = new TranslateTransform();
            }

            TranslateTransform transform = (TranslateTransform)_drawerContainer.RenderTransform;

            // 动画抽屉
            DoubleAnimation drawerAnimation = new DoubleAnimation
            {
                Duration = AnimationDuration,
                // 弹性动画效果
                // EasingFunction = IsOpen 
                //     ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                //     : new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 6 }
            };

            switch (DrawerPosition)
            {
                case DrawerPosition.Left:
                    // 打开时，先设置初始位置
                    if (IsOpen)
                    {
                        transform.X = -OpenWidth;
                    }

                    drawerAnimation.To = IsOpen ? 0 : -OpenWidth;
                    transform.BeginAnimation(TranslateTransform.XProperty, drawerAnimation);
                    break;
                case DrawerPosition.Right:
                    // 打开时，先设置初始位置
                    if (IsOpen)
                    {
                        transform.X = OpenWidth;
                    }

                    drawerAnimation.To = IsOpen ? 0 : OpenWidth;
                    transform.BeginAnimation(TranslateTransform.XProperty, drawerAnimation);
                    break;
                case DrawerPosition.Top:
                    // 打开时，先设置初始位置
                    if (IsOpen)
                    {
                        transform.Y = -OpenHeight;
                    }

                    drawerAnimation.To = IsOpen ? 0 : -OpenHeight;
                    transform.BeginAnimation(TranslateTransform.YProperty, drawerAnimation);
                    break;
                case DrawerPosition.Bottom:
                    // 打开时，先设置初始位置
                    if (IsOpen)
                    {
                        transform.Y = OpenHeight;
                    }

                    drawerAnimation.To = IsOpen ? 0 : OpenHeight;
                    transform.BeginAnimation(TranslateTransform.YProperty, drawerAnimation);
                    break;
            }

            if (!IsOpen)
            {
                drawerAnimation.Completed += (s, e) =>
                {
                    if (!IsOpen)
                    {
                        Visibility = Visibility.Collapsed;
                    }
                };
            }
        }
        else
        {
            // 无动画直接设置
            _backgroundOverlay.Opacity = IsOpen ? BackgroundOpacity : 0;

            TranslateTransform transform = new TranslateTransform();
            _drawerContainer.RenderTransform = transform;

            switch (DrawerPosition)
            {
                case DrawerPosition.Left:
                    transform.X = IsOpen ? 0 : -OpenWidth;
                    break;
                case DrawerPosition.Right:
                    transform.X = IsOpen ? 0 : OpenWidth;
                    break;
                case DrawerPosition.Top:
                    transform.Y = IsOpen ? 0 : -OpenHeight;
                    break;
                case DrawerPosition.Bottom:
                    transform.Y = IsOpen ? 0 : OpenHeight;
                    break;
            }

            Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

public enum DrawerPosition
{
    Left,
    Right,
    Top,
    Bottom
}