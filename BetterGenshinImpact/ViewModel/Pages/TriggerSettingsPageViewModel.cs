using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TriggerSettingsPageViewModel : ObservableObject, INavigationAware
{
    public AllConfig Config { get; set; }

    public TriggerSettingsPageViewModel(IConfigService configService)
    {
        Config = configService.Get();
    }

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }


    [RelayCommand]
    private void OnEditBlacklist()
    {
        Process.Start("notepad.exe", Global.Absolute(@"User\pick_black_lists.json"));
    }

    [RelayCommand]
    private void OnEditWhitelist()
    {
        Process.Start("notepad.exe", Global.Absolute(@"User\pick_white_lists.json"));
    }

    [RelayCommand]
    private async void OnOpenCustomMessageBox(object sender)
    {
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "WPF UI Message Box",
            Content =
                "Never gonna give you up, never gonna let you down Never gonna run around and desert you Never gonna make you cry, never gonna say goodbye",
        };

        var result = await uiMessageBox.ShowDialogAsync();
    }
}