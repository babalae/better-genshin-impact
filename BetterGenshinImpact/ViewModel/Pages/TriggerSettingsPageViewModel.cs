using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TriggerSettingsPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;

    public TriggerSettingsPageViewModel(IConfigService configService, INavigationService navigationService)
    {
        Config = configService.Get();
        _navigationService = navigationService;
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
        ShowJsonMonoDialog(@"User\pick_black_lists.json");
    }

    [RelayCommand]
    private void OnEditWhitelist()
    {
        ShowJsonMonoDialog(@"User\pick_white_lists.json");
    }

    private void ShowJsonMonoDialog(string path)
    {
        JsonMonoDialog dialog = new(path)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.Show();
    }

    [RelayCommand]
    private void OnOpenReExploreCharacterBox(object sender)
    {
        var str = PromptDialog.Prompt("请使用派遣界面展示的角色名，英文逗号分割，从左往右优先级依次降低。\n示例：菲谢尔,班尼特,夜兰,申鹤,久岐忍",
            "派遣角色优先级配置", Config.AutoSkipConfig.AutoReExploreCharacter);
        Config.AutoSkipConfig.AutoReExploreCharacter = str.Replace("，", ",").Replace(" ", "");
    }

    [RelayCommand]
    public void OnGoToQGroupUrl()
    {
        Process.Start(new ProcessStartInfo("http://qm.qq.com/cgi-bin/qm/qr?_wv=1027&k=mL1O7atys6Prlu5LBVqmDlfOrzyPMLN4&authKey=jSI2WuZyUjmpIUIAsBAf5g0r5QeSu9K6Un%2BRuSsQ8fQGYwGYwRVioFfJyYnQqvbf&noverify=0&group_code=863012276") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }
}
