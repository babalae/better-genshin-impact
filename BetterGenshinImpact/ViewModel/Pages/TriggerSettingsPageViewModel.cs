using System;
using System.Collections.Generic;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using System.Windows;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.SkillCd;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TriggerSettingsPageViewModel : ViewModel
{
    [ObservableProperty] private string[] _clickChatOptionNames = ["优先选择第一个选项", "随机选择选项", "优先选择最后一个选项", "不选择选项"];

    [ObservableProperty] private string[] _selectChatOptionTypeNames = [SelectChatOptionTypes.UseMouse, SelectChatOptionTypes.UseInteractionKey];

    [ObservableProperty] private string[] _pickOcrEngineNames = [PickOcrEngineEnum.Paddle.ToString(), PickOcrEngineEnum.Yap.ToString()];

    [ObservableProperty] private bool _isBlacklistAutoPickMode;

    [ObservableProperty] private bool _isWhitelistAutoPickMode;

    [ObservableProperty] private List<string> _pickButtonNames;

    [ObservableProperty] private Dictionary<string, string> _pictureInPictureSourceTypeDict =
        new()
        {
            { nameof(PictureSourceType.CaptureLoop), "60帧模式" },
            { nameof(PictureSourceType.TriggerDispatcher), "截图器供图" }
        };

    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;

    [ObservableProperty] private List<string> _hangoutBranches;

    public TriggerSettingsPageViewModel(IConfigService configService, INavigationService navigationService)
    {
        Config = configService.Get();
        _navigationService = navigationService;
        _hangoutBranches = HangoutConfig.Instance.HangoutOptionsTitleList;
        UpdateAutoPickModeVisibility();

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
    private void OnOpenBlacklistModeConfig()
    {
        var window = new AutoPickBlacklistConfigWindow(Config.AutoPickConfig)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void OnOpenWhitelistModeConfig()
    {
        var window = new AutoPickWhitelistConfigWindow(Config.AutoPickConfig)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void OnAutoPickModeChanged(AutoPickMode mode)
    {
        Config.AutoPickConfig.Mode = mode;
        UpdateAutoPickModeVisibility();
        GameTaskManager.RefreshTriggerConfigs();
    }

    private void UpdateAutoPickModeVisibility()
    {
        IsBlacklistAutoPickMode = Config.AutoPickConfig.Mode == AutoPickMode.Blacklist;
        IsWhitelistAutoPickMode = Config.AutoPickConfig.Mode == AutoPickMode.Whitelist;
    }

    // [RelayCommand]
    // private void OnOpenReExploreCharacterBox(object sender)
    // {
    //     var str = PromptDialog.Prompt("请使用派遣界面展示的角色名，英文逗号分割，从左往右优先级依次降低。\n示例：菲谢尔,班尼特,夜兰,申鹤,久岐忍",
    //         "派遣角色优先级配置", Config.AutoSkipConfig.AutoReExploreCharacter);
    //     Config.AutoSkipConfig.AutoReExploreCharacter = str.Replace("，", ",").Replace(" ", "");
    // }

    [RelayCommand]
    private void OnRemoveSkillCdRule(SkillCdRule rule)
    {
        if (TemporarySkillCdCollection != null && rule != null)
        {
            TemporarySkillCdCollection.Remove(rule);
        }
    }

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<SkillCdRule> _temporarySkillCdCollection;

    [RelayCommand]
    private void OnEditSkillCdConfig()
    {
        var configList = Config.SkillCdConfig.CustomCdList;
        
        var window = new SkillCdConfigWindow(configList)
        {
            Owner = Application.Current.MainWindow
        };
        
        window.Closed += (s, e) => 
        {
            Config.SkillCdConfig.CustomCdList = window.GetValidRules();
            GameTaskManager.RefreshTriggerConfigs();
        };

        window.ShowDialog();
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }
}
