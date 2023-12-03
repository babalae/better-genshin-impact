using System.Windows;
using System.Windows.Media;

namespace MicaSetup.Design.Controls;

public class ProgressBarHelper
{
    public static Color GetForeground1(DependencyObject obj)
    {
        return (Color)obj.GetValue(Foreground1Property);
    }

    public static void SetForeground1(DependencyObject obj, Color value)
    {
        obj.SetValue(Foreground1Property, value);
    }

    public static readonly DependencyProperty Foreground1Property = DependencyProperty.RegisterAttached("Foreground1", typeof(Color), typeof(ProgressBarHelper), new((Color)ColorConverter.ConvertFromString("#73EBF3")));

    public static Color GetForeground2(DependencyObject obj)
    {
        return (Color)obj.GetValue(Foreground2Property);
    }

    public static void SetForeground2(DependencyObject obj, Color value)
    {
        obj.SetValue(Foreground2Property, value);
    }

    public static readonly DependencyProperty Foreground2Property = DependencyProperty.RegisterAttached("Foreground2", typeof(Color), typeof(ProgressBarHelper), new((Color)ColorConverter.ConvertFromString("#238EFA")));
}
