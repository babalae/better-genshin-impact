using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.View.Pages;
using Wpf.Ui.Controls;
using Grid = System.Windows.Controls.Grid;

namespace BetterGenshinImpact.View.Windows;

public partial class KeyBindingsWindow : FluentWindow
{
    private static KeyBindingsWindow? _instance;
    private static readonly object _lock = new();

    public static KeyBindingsWindow Instance
    {
        get
        {
            lock (_lock)
            {
                if (_instance == null || !_instance.IsLoaded)
                {
                    _instance = new KeyBindingsWindow();
                    // 不让他销毁窗口
                    _instance.Closing += (s, e) =>
                    {
                        e.Cancel = true; 
                        _instance.Hide();
                    };
                }
                return _instance;
            }
        }
    }
    
    public KeyBindingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);

        var page = App.GetService<KeyBindingsSettingsPage>();
        Grid.SetRow(page!, 1);
        Grid1.Children.Add(page!);
    }
}