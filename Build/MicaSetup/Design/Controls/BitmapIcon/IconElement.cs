using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace MicaSetup.Design.Controls;

#pragma warning disable CS8618

public abstract class IconElement : FrameworkElement
{
    private protected IconElement()
    {
    }

    public static readonly DependencyProperty ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner(
                    typeof(IconElement),
                    new FrameworkPropertyMetadata(SystemColors.ControlTextBrush,
                        FrameworkPropertyMetadataOptions.Inherits,
                        OnForegroundPropertyChanged));

    private static void OnForegroundPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((IconElement)sender).OnForegroundPropertyChanged(args);
    }

    private void OnForegroundPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        var baseValueSource = DependencyPropertyHelper.GetValueSource(this, args.Property).BaseValueSource;
        _isForegroundDefaultOrInherited = baseValueSource <= BaseValueSource.Inherited;
        UpdateShouldInheritForegroundFromVisualParent();
    }

    [Bindable(true), Category("Appearance")]
    public Brush Foreground
    {
        get { return (Brush)GetValue(ForegroundProperty); }
        set { SetValue(ForegroundProperty, value); }
    }

    private static readonly DependencyProperty VisualParentForegroundProperty =
        DependencyProperty.Register(
            nameof(VisualParentForeground),
            typeof(Brush),
            typeof(IconElement),
            new PropertyMetadata(null, OnVisualParentForegroundPropertyChanged));

    protected Brush VisualParentForeground
    {
        get => (Brush)GetValue(VisualParentForegroundProperty);
        set => SetValue(VisualParentForegroundProperty, value);
    }

    private static void OnVisualParentForegroundPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((IconElement)sender).OnVisualParentForegroundPropertyChanged(args);
    }

    private protected virtual void OnVisualParentForegroundPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
    }

    protected bool ShouldInheritForegroundFromVisualParent
    {
        get => _shouldInheritForegroundFromVisualParent;
        private set
        {
            if (_shouldInheritForegroundFromVisualParent != value)
            {
                _shouldInheritForegroundFromVisualParent = value;

                if (_shouldInheritForegroundFromVisualParent)
                {
                    SetBinding(VisualParentForegroundProperty,
                        new Binding
                        {
                            Path = new PropertyPath(TextElement.ForegroundProperty),
                            Source = VisualParent
                        });
                }
                else
                {
                    ClearValue(VisualParentForegroundProperty);
                }

                OnShouldInheritForegroundFromVisualParentChanged();
            }
        }
    }

    private protected virtual void OnShouldInheritForegroundFromVisualParentChanged()
    {
    }

    private void UpdateShouldInheritForegroundFromVisualParent()
    {
        ShouldInheritForegroundFromVisualParent =
            _isForegroundDefaultOrInherited &&
            Parent != null &&
            VisualParent != null &&
            Parent != VisualParent;
    }

    protected UIElementCollection Children
    {
        get
        {
            EnsureLayoutRoot();
            return _layoutRoot.Children;
        }
    }

    private protected abstract void InitializeChildren();

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index)
    {
        if (index == 0)
        {
            EnsureLayoutRoot();
            return _layoutRoot;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayoutRoot();
        _layoutRoot.Measure(availableSize);
        return _layoutRoot.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        EnsureLayoutRoot();
        _layoutRoot.Arrange(new Rect(new Point(), finalSize));
        return finalSize;
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        UpdateShouldInheritForegroundFromVisualParent();
    }

    private void EnsureLayoutRoot()
    {
        if (_layoutRoot != null)
            return;

        _layoutRoot = new Grid
        {
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
        };
        InitializeChildren();

        AddVisualChild(_layoutRoot);
    }

    private Grid _layoutRoot;
    private bool _isForegroundDefaultOrInherited = true;
    private bool _shouldInheritForegroundFromVisualParent;
}
