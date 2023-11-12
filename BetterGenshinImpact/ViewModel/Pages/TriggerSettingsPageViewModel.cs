using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
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
    private void OnOpenReExploreCharacterBox(object sender)
    {
        var str = PromptDialog.Prompt("请使用派遣界面展示的角色名，英文逗号分割，从左往右优先级依次降低。\n示例：菲谢尔,班尼特,夜兰,申鹤,久岐忍", 
            "派遣角色优先级配置", Config.AutoSkipConfig.AutoReExploreCharacter);
         Config.AutoSkipConfig.AutoReExploreCharacter = str.Replace("，", ",").Replace(" ","");
    }
}