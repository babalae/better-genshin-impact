using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPick;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class AutoPickBlacklistConfigViewModel : AutoPickConfigWindowViewModelBase
{
    private const string DoNotPickPath = @"User\pick_black_lists.txt";
    private const string FuzzyDoNotPickPath = @"User\pick_fuzzy_black_lists.txt";
    private const string PickPath = @"User\pick_white_lists.txt";

    private readonly AutoPickConfig _config;

    [ObservableProperty] private bool _pickEnabled;

    [ObservableProperty] private string _doNotPickText;

    [ObservableProperty] private string _fuzzyDoNotPickText;

    [ObservableProperty] private string _pickText;

    public AutoPickBlacklistConfigViewModel(AutoPickConfig config)
    {
        _config = config;
        _pickEnabled = config.BlacklistModePickEnabled;
        _doNotPickText = Global.ReadAllTextIfExist(DoNotPickPath) ?? string.Empty;
        _fuzzyDoNotPickText = Global.ReadAllTextIfExist(FuzzyDoNotPickPath) ?? string.Empty;
        _pickText = Global.ReadAllTextIfExist(PickPath) ?? string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        SaveAndClose(() =>
        {
            Global.WriteAllText(DoNotPickPath, DoNotPickText);
            Global.WriteAllText(FuzzyDoNotPickPath, FuzzyDoNotPickText);
            Global.WriteAllText(PickPath, PickText);
            _config.BlacklistModePickEnabled = PickEnabled;
        });
    }
}
