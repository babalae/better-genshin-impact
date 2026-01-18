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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using BetterGenshinImpact.GameTask;
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
        var exactText = Global.ReadAllTextIfExist(exactPath);

        // 读取模糊匹配黑名单
        var fuzzyPath = @"User\pick_fuzzy_black_lists.txt";
        var fuzzyText = Global.ReadAllTextIfExist(fuzzyPath);

        // 创建精确匹配黑名单输入框
        var exactRichTextBox = new System.Windows.Controls.RichTextBox
        {
            Height = 150,
            Width = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsReturn = true,
            AcceptsTab = true
        };
        
        // 创建 FlowDocument 并设置紧凑的行间距
        var exactFlowDocument = new FlowDocument();
        exactFlowDocument.LineHeight = 1.0; // 设置行高为1倍
        exactFlowDocument.PagePadding = new Thickness(2); // 减小页面内边距
        
        // 设置 RichTextBox 的文本内容
        if (!string.IsNullOrEmpty(exactText))
        {
            var paragraph = new Paragraph(new Run(exactText));
            paragraph.Margin = new Thickness(0); // 移除段落边距
            paragraph.LineHeight = 1.0; // 设置段落行高
            exactFlowDocument.Blocks.Add(paragraph);
        }
        else
        {
            // 即使没有文本也要设置默认段落样式
            var paragraph = new Paragraph();
            paragraph.Margin = new Thickness(0);
            paragraph.LineHeight = 1.0;
            exactFlowDocument.Blocks.Add(paragraph);
        }
        
        exactRichTextBox.Document = exactFlowDocument;

        // 创建模糊匹配黑名单输入框
        var fuzzyRichTextBox = new System.Windows.Controls.RichTextBox
        {
            Height = 150,
            Width = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsReturn = true,
            AcceptsTab = true
        };
        
        // 创建 FlowDocument 并设置紧凑的行间距
        var fuzzyFlowDocument = new FlowDocument();
        fuzzyFlowDocument.LineHeight = 1.0; // 设置行高为1倍
        fuzzyFlowDocument.PagePadding = new Thickness(2); // 减小页面内边距
        
        // 设置 RichTextBox 的文本内容
        if (!string.IsNullOrEmpty(fuzzyText))
        {
            var paragraph = new Paragraph(new Run(fuzzyText));
            paragraph.Margin = new Thickness(0); // 移除段落边距
            paragraph.LineHeight = 1.0; // 设置段落行高
            fuzzyFlowDocument.Blocks.Add(paragraph);
        }
        else
        {
            // 即使没有文本也要设置默认段落样式
            var paragraph = new Paragraph();
            paragraph.Margin = new Thickness(0);
            paragraph.LineHeight = 1.0;
            fuzzyFlowDocument.Blocks.Add(paragraph);
        }
        
        fuzzyRichTextBox.Document = fuzzyFlowDocument;

        // 创建包含两个输入框的容器
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
        stackPanel.Children.Add(exactRichTextBox);
        stackPanel.Children.Add(fuzzyLabel);
        stackPanel.Children.Add(fuzzyRichTextBox);

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
            // 从 RichTextBox 获取纯文本内容
            var exactTextRange = new TextRange(exactRichTextBox.Document.ContentStart, exactRichTextBox.Document.ContentEnd);
            var fuzzyTextRange = new TextRange(fuzzyRichTextBox.Document.ContentStart, fuzzyRichTextBox.Document.ContentEnd);
            
            File.WriteAllText(Global.Absolute(exactPath), exactTextRange.Text);
            File.WriteAllText(Global.Absolute(fuzzyPath), fuzzyTextRange.Text);
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
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }
}
