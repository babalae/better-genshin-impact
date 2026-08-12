using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPick;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class AutoPickWhitelistConfigViewModel : AutoPickConfigWindowViewModelBase
{
    private const string PickPath = @"User\pick_whitelist_mode_pick_lists.txt";
    private const string DoNotPickPath = @"User\pick_whitelist_mode_do_not_pick_lists.txt";

    private readonly AutoPickConfig _config;

    [ObservableProperty] private bool _doNotPickEnabled;

    [ObservableProperty] private string _pickText;

    [ObservableProperty] private string _doNotPickText;

    public AutoPickWhitelistConfigViewModel(AutoPickConfig config)
    {
        _config = config;
        _doNotPickEnabled = config.WhitelistModeDoNotPickEnabled;
        _pickText = Global.ReadAllTextIfExist(PickPath) ?? string.Empty;
        _doNotPickText = Global.ReadAllTextIfExist(DoNotPickPath) ?? string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        SaveAndClose(() =>
        {
            Global.WriteAllText(PickPath, PickText);
            Global.WriteAllText(DoNotPickPath, DoNotPickText);
            _config.WhitelistModeDoNotPickEnabled = DoNotPickEnabled;
        });
    }
}
