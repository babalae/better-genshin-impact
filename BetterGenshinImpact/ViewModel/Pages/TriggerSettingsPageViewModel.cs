using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Diagnostics;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TriggerSettingsPageViewModel : ViewModel
{
    [ObservableProperty]
    private string[] _clickChatOptionNames = ["优先选择第一个选项", "随机选择选项", "优先选择最后一个选项", "不选择选项"];

    [ObservableProperty]
    private string[] _selectChatOptionTypeNames = [SelectChatOptionTypes.UseMouse, SelectChatOptionTypes.UseInteractionKey];

    [ObservableProperty]
    private string[] _pickOcrEngineNames = [PickOcrEngineEnum.Paddle.ToString(), PickOcrEngineEnum.Yap.ToString()];

    [ObservableProperty]
    private List<string> _pickButtonNames;

    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private List<string> _hangoutBranches;

    public TriggerSettingsPageViewModel(IConfigService configService, INavigationService navigationService)
    {
        Config = configService.Get();
        _navigationService = navigationService;
        _hangoutBranches = HangoutConfig.Instance.HangoutOptionsTitleList;

        _pickButtonNames = new List<string> { "F", "E", "G" };
        if (!string.IsNullOrEmpty(Config.AutoPickConfig.PickKey)
            && Config.AutoPickConfig.PickKey.Length == 1
            && char.IsUpper(Config.AutoPickConfig.PickKey[0])
            && !_pickButtonNames.Contains(Config.AutoPickConfig.PickKey))
        {
            _pickButtonNames.Add(Config.AutoPickConfig.PickKey);
        }
    }

    [RelayCommand]
    private void OnEditBlacklist()
    {
        JsonMonoDialog.Show(@"User\pick_black_lists.json");
    }

    [RelayCommand]
    private void OnEditWhitelist()
    {
        JsonMonoDialog.Show(@"User\pick_white_lists.json");
    }

    // [RelayCommand]
    // private void OnOpenReExploreCharacterBox(object sender)
    // {
    //     var str = PromptDialog.Prompt("请使用派遣界面展示的角色名，英文逗号分割，从左往右优先级依次降低。\n示例：菲谢尔,班尼特,夜兰,申鹤,久岐忍",
    //         "派遣角色优先级配置", Config.AutoSkipConfig.AutoReExploreCharacter);
    //     Config.AutoSkipConfig.AutoReExploreCharacter = str.Replace("，", ",").Replace(" ", "");
    // }

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
