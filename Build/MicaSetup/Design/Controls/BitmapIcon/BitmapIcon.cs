using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MicaSetup.Design.Controls;

#pragma warning disable CS8618

public class BitmapIcon : IconElement
{
    static BitmapIcon()
    {
        ForegroundProperty.OverrideMetadata(typeof(BitmapIcon), new FrameworkPropertyMetadata(OnForegroundChanged));
    }

    public BitmapIcon()
    {
    }

    public static readonly DependencyProperty UriSourceProperty =
        BitmapImage.UriSourceProperty.AddOwner(
            typeof(BitmapIcon),
            new FrameworkPropertyMetadata(OnUriSourceChanged));

    public Uri UriSource
    {
        get => (Uri)GetValue(UriSourceProperty);
        set => SetValue(UriSourceProperty, value);
    }

    private static void OnUriSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BitmapIcon)d).ApplyUriSource();
    }

    public static readonly DependencyProperty ShowAsMonochromeProperty =
        DependencyProperty.Register(
            nameof(ShowAsMonochrome),
            typeof(bool),
            typeof(BitmapIcon),
            new PropertyMetadata(true, OnShowAsMonochromeChanged));

    public bool ShowAsMonochrome
    {
        get => (bool)GetValue(ShowAsMonochromeProperty);
        set => SetValue(ShowAsMonochromeProperty, value);
    }

    private static void OnShowAsMonochromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BitmapIcon)d).ApplyShowAsMonochrome();
    }

    private protected override void InitializeChildren()
    {
        _image = new Image
        {
            Visibility = Visibility.Hidden
        };

        _opacityMask = new ImageBrush();
        _foreground = new Rectangle
        {
            OpacityMask = _opacityMask
        };

        ApplyForeground();
        ApplyUriSource();

        Children.Add(_image);

        ApplyShowAsMonochrome();
    }

    private protected override void OnShouldInheritForegroundFromVisualParentChanged()
    {
        ApplyForeground();
    }

    private protected override void OnVisualParentForegroundPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        if (ShouldInheritForegroundFromVisualParent)
        {
            ApplyForeground();
        }
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BitmapIcon)d).ApplyForeground();
    }

    private void ApplyForeground()
    {
        if (_foreground != null)
        {
            _foreground.Fill = ShouldInheritForegroundFromVisualParent ? VisualParentForeground : Foreground;
        }
    }

    private void ApplyUriSource()
    {
        if (_image != null && _opacityMask != null)
        {
            var uriSource = UriSource;
            if (uriSource != null)
            {
                var imageSource = new BitmapImage(uriSource);
                _image.Source = imageSource;
                _opacityMask.ImageSource = imageSource;
            }
            else
            {
                _image.ClearValue(Image.SourceProperty);
                _opacityMask.ClearValue(ImageBrush.ImageSourceProperty);
            }
        }
    }

    private void ApplyShowAsMonochrome()
    {
        bool showAsMonochrome = ShowAsMonochrome;

        if (_image != null)
        {
            _image.Visibility = showAsMonochrome ? Visibility.Hidden : Visibility.Visible;
        }

        if (_foreground != null)
        {
            if (showAsMonochrome)
            {
                if (!Children.Contains(_foreground))
                {
                    Children.Add(_foreground);
                }
            }
            else
            {
                Children.Remove(_foreground);
            }
        }
    }

    private Image _image;
    private Rectangle _foreground;
    private ImageBrush _opacityMask;
}
