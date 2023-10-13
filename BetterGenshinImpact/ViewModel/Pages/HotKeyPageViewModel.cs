using System.Collections.ObjectModel;
using System.Windows.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HotKeyPageViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<HotKeyConfig> _hotkeyConfigs = new();

    public HotKeyPageViewModel()
    {
        var h1 = new HotKeyConfig
        {
            DisplayName = "Test",
            FunctionClass = "Test",
            Hotkey = new HotKey(Key.T, ModifierKeys.Control | ModifierKeys.Shift)
        };
        _hotkeyConfigs.Add(h1);
        var h2 = new HotKeyConfig
        {
            DisplayName = "Test2",
            FunctionClass = "Test2",
            Hotkey = new HotKey(Key.A, ModifierKeys.Control | ModifierKeys.Shift)
        };
        _hotkeyConfigs.Add(h2);
    }
}