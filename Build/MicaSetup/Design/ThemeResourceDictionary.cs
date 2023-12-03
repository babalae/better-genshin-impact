using MicaSetup.Design.Controls;
using System;
using System.Windows;

namespace MicaSetup.Design;

public sealed class ThemeResourceDictionary : ResourceDictionary
{
    public static ThemeResourceDictionary Instance { get; private set; } = null!;

    public ThemeResourceDictionary()
    {
        Instance = this;
        MergedDictionaries.Add(new ResourceDictionary()
        {
            Source = new Uri($"pack://application:,,,/MicaSetup;component/Resources/Themes/Dark.xaml"),
        });
        MergedDictionaries.Add(new ResourceDictionary()
        {
            Source = new Uri($"pack://application:,,,/MicaSetup;component/Resources/Themes/Light.xaml"),
        });
        MergedDictionaries.Add(new ResourceDictionary()
        {
            Source = new Uri($"pack://application:,,,/MicaSetup;component/Resources/Themes/Brushes.xaml"),
        });
    }

    public static void SyncResource()
    {
        Instance.MergedDictionaries.Clear();
        if (ThemeService.Current.CurrentTheme == WindowsTheme.Dark)
        {
            Instance.MergedDictionaries.Add(new ResourceDictionary()
            {
                Source = new Uri($"pack://application:,,,/MicaSetup;component/Resources/Themes/Dark.xaml"),
            });
            Instance.MergedDictionaries.Add(new ResourceDictionary()
            {
                Source = new Uri($"pack://application:,,,/MicaSetup;component/Resources/Themes/Brushes.xaml"),
            });
        }
        else
        {
            Instance.MergedDictionaries.Add(new ResourceDictionary()
            {
                Source = new Uri($"pack://application:,,,/MicaSetup;component/Resources/Themes/Light.xaml"),
            });
            Instance.MergedDictionaries.Add(new ResourceDictionary()
            {
                Source = new Uri($"pack://application:,,,/MicaSetup;component/Resources/Themes/Brushes.xaml"),
            });
        }
    }
}
