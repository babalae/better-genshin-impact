using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Violeta.Controls;
using System.Linq; 
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BetterGenshinImpact.View.Pages;

public partial class OneDragonFlowPage
{
    public OneDragonFlowViewModel ViewModel { get; }
    
    private readonly Dictionary<CheckBox, string> _checkBoxMappings;
    
    public static readonly List<string> SereniteaPotSchedule = new List<string> { "每天重复", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };

    public OneDragonFlowPage(OneDragonFlowViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        
        _checkBoxMappings = new Dictionary<CheckBox, string>
        {
            { ClothCheckBox, "布匹" },
            { MomentResinCheckBox, "须臾树脂" },
            { SereniteapotExpBookCheckBox, "大英雄的经验" },
            { SereniteapotExpBookSmallCheckBox, "流浪者的经验" },
            { MagicmineralprecisionCheckBox, "精锻用魔矿" },
            { MOlaCheckBox, "摩拉" },
            { ExpBottleBigCheckBox, "祝圣精华" },
            { ExpBottleSmallCheckBox, "祝圣油膏" }
        };

        // 监听配置切换和 DomainName 变化，更新秘境相关控件的显隐
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(viewModel.SelectedConfig))
            {
                WatchDomainNameChange();
                UpdateDomainVisibility();
            }
        };
        WatchDomainNameChange();
    }

    private System.ComponentModel.PropertyChangedEventHandler? _configHandler;
    private BetterGenshinImpact.Core.Config.OneDragonFlowConfig? _watchedConfig;

    /// <summary>
    /// 监听当前配置单的 DomainName 属性变化
    /// </summary>
    private void WatchDomainNameChange()
    {
        // 从旧配置对象上解绑（而非当前 SelectedConfig，避免切换后解绑错对象）
        if (_configHandler != null && _watchedConfig != null)
            _watchedConfig.PropertyChanged -= _configHandler;

        _watchedConfig = ViewModel.SelectedConfig;

        _configHandler = (s, e) =>
        {
            if (e.PropertyName == nameof(BetterGenshinImpact.Core.Config.OneDragonFlowConfig.DomainName))
                UpdateDomainVisibility();
        };

        if (_watchedConfig != null)
            _watchedConfig.PropertyChanged += _configHandler;

        // 立即更新一次，确保初始状态正确
        UpdateDomainVisibility();
    }

    /// <summary>
    /// 根据当前 DomainName 的类型前缀判断是否为标准秘境，控制队伍名称和选择序号的显隐
    /// </summary>
    private void UpdateDomainVisibility()
    {
        var name = ViewModel.SelectedConfig?.DomainName;
        var (type, _) = Helpers.DomainCascadingItems.Parse(name);
        // 标准秘境（type == "domain" 或空）显示，一条龙任务和配置组隐藏
        var vis = (type == "domain" || type == "") ? Visibility.Visible : Visibility.Collapsed;
        PartyNameGrid.Visibility = vis;
        SundayLabelText.Visibility = vis;
        SundaySubLabelText.Visibility = vis;
        SundayComboBox.Visibility = vis;
    }
    
    private async void SereniteaPotTpType_Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedConfig == null)
        {
            return;
        }
        
        if (sender.Equals(PopupCloseButton)) Popup.IsOpen = false; //关闭弹窗
        
        else if (sender.Equals(PopupConfirmButton)) //确认选择
        {
            var selectedObjects = new List<string>(SereniteaPotComboBox.SelectedItem.ToString().Split('/'))
                .Concat(_checkBoxMappings.Where(pair => pair.Key.IsChecked == true).Select(pair => pair.Value)).ToList();

            ViewModel.SelectedConfig.SecretTreasureObjects = selectedObjects;
            Popup.IsOpen = false;
        }
        
        else if (sender.Equals(PotButton)) //初始化显示购买信息
        {
            if (!ViewModel.SelectedConfig.SecretTreasureObjects.Any())
            {
                ViewModel.SelectedConfig.SecretTreasureObjects.Add("每天重复"); 
            }
          
            SereniteaPotComboBox.SelectedItem = ViewModel.SelectedConfig.SecretTreasureObjects[0];
            
            foreach (var pair in _checkBoxMappings)
            {
                pair.Key.IsChecked = ViewModel.SelectedConfig.SecretTreasureObjects.Contains(pair.Value);
            }

        }
        
    }
    
}
