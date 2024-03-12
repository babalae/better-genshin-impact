using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using BetterGenshinImpact.View.Controls.Adorners;

namespace BetterGenshinImpact.View.Controls;

public class DesignerItemDecorator : Control
{
    private Adorner? adorner;

    public bool ShowDecorator
    {
        get { return (bool)GetValue(ShowDecoratorProperty); }
        set { SetValue(ShowDecoratorProperty, value); }
    }

    public static readonly DependencyProperty ShowDecoratorProperty =
        DependencyProperty.Register(nameof(ShowDecorator), typeof(bool), typeof(DesignerItemDecorator),
        new FrameworkPropertyMetadata(false, new PropertyChangedCallback(OnShowDecoratorChanged)));

    public DesignerItemDecorator()
    {
        Unloaded += OnDesignerItemDecoratorUnloaded;
    }

    private void HideAdorner()
    {
        if (adorner is not null)
        {
            adorner.Visibility = Visibility.Hidden;
        }
    }

    private void ShowAdorner()
    {
        if (adorner is null)
        {
            AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(this);

            if (adornerLayer is not null)
            {
                ContentControl? designerItem = DataContext as ContentControl;
                // Canvas? canvas = VisualTreeHelper.GetParent(designerItem) as Canvas;
                adorner = new ResizeRotateAdorner(designerItem);
                adornerLayer.Add(adorner);

                adorner.Visibility = ShowDecorator ? Visibility.Visible : Visibility.Hidden;
            }
        }
        else
        {
            adorner.Visibility = Visibility.Visible;
        }
    }

    private void OnDesignerItemDecoratorUnloaded(object sender, RoutedEventArgs e)
    {
        if (adorner is not null)
        {
            AdornerLayer.GetAdornerLayer(this)?.Remove(adorner);
            adorner = null;
        }
    }

    private static void OnShowDecoratorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DesignerItemDecorator decorator = (DesignerItemDecorator)d;
        bool showDecorator = (bool)e.NewValue;

        if (showDecorator)
        {
            decorator.ShowAdorner();
        }
        else
        {
            decorator.HideAdorner();
        }
    }
}
