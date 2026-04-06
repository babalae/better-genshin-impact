using System;
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.SkillCd;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TriggerSettingsPageViewModel : ViewModel
{
    [ObservableProperty] private string[] _clickChatOptionNames = ["优先选择第一个选项", "随机选择选项", "优先选择最后一个选项", "不选择选项"];

    [ObservableProperty] private string[] _selectChatOptionTypeNames = [SelectChatOptionTypes.UseMouse, SelectChatOptionTypes.UseInteractionKey];

    [ObservableProperty] private string[] _pickOcrEngineNames = [PickOcrEngineEnum.Paddle.ToString(), PickOcrEngineEnum.Yap.ToString()];

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
        // 读取精确匹配黑名单
        var exactPath = @"User\pick_black_lists.txt";
        var exactText = Global.ReadAllTextIfExist(exactPath) ?? string.Empty;

        // 读取模糊匹配黑名单
        var fuzzyPath = @"User\pick_fuzzy_black_lists.txt";
        var fuzzyText = Global.ReadAllTextIfExist(fuzzyPath) ?? string.Empty;

        // 创建精确匹配黑名单输入框
        var exactTextBox = new TextBox
        {
            Height = 150,
            Width = 400,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PlaceholderText = "每行一条记录",
            Text = exactText
        };

        var fuzzyTextBox = new TextBox
        {
            Height = 150,
            Width = 400,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PlaceholderText = "每行一条记录",
            Text = fuzzyText
        };

        var stackPanel = new StackPanel();

        var exactLabel = new Wpf.Ui.Controls.TextBlock
        {
            Text = "精确匹配黑名单：",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        };

        var fuzzyLabel = new Wpf.Ui.Controls.TextBlock
        {
            Text = "模糊匹配黑名单：",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 5)
        };

        stackPanel.Children.Add(exactLabel);
        stackPanel.Children.Add(exactTextBox);
        stackPanel.Children.Add(fuzzyLabel);
        stackPanel.Children.Add(fuzzyTextBox);

        var p = new PromptDialog(
            "黑名单配置\n" +
            "每行一条记录。\n" +
            "示例：\n" +
            "精致的宝箱\n" +
            "史莱姆凝液\n" +
            "牢固的箭簇",
            "黑名单配置",
            stackPanel,
            null);
        p.Height = 600;
        p.Width = 500;
        p.ShowDialog();

        if (p.DialogResult == true)
        {
            Global.WriteAllText(exactPath, exactTextBox.Text);
            Global.WriteAllText(fuzzyPath, fuzzyTextBox.Text);
            GameTaskManager.RefreshTriggerConfigs();
        }
    }

    [RelayCommand]
    private void OnEditWhitelist()
    {
        var path = @"User\pick_white_lists.txt";
        var text = Global.ReadAllTextIfExist(path);
        if (string.IsNullOrEmpty(text))
        {
            text = "";
        }

        var multilineTextBox = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            //Height = 340,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PlaceholderText = "请在此输入白名单配置，每行一条记录。\n" +
                              "示例：\n" +
                              "调查\n" +
                              "合成\n" +
                              "启动"
        };
        var p = new PromptDialog(
            "白名单配置，每行一条记录\n" +
            "示例：\n" +
            "调查\n" +
            "合成\n" +
            "启动",
            "白名单配置",
            multilineTextBox,
            text);
        p.Height = 500;
        p.ShowDialog();
        if (p.DialogResult == true)
        {
            File.WriteAllText(Global.Absolute(path), multilineTextBox.Text);
            GameTaskManager.RefreshTriggerConfigs();
        }
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
