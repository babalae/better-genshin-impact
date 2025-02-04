using System.Windows;

namespace BetterGenshinImpact.View;

public interface IWindow
{
    event RoutedEventHandler Loaded;

    void Show();
}