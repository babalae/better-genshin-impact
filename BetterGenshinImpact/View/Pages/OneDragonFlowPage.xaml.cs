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
    
    public static readonly List<string> SereniteaPotTypes = new List<string> { "每天重复", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };

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
        
    }
    
    private async void SereniteaPotTpType_Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedConfig == null)
        {
            Toast.Warning("初始化失败，请先选择配置单");
            return;
        }
        
        if (sender.Equals(PopupCloseButton)) //关闭弹窗
        {
            Popup.IsOpen = false;
        }
        
        else if (sender.Equals(PopupConfirmButton)) //确认选择
        {
            var selectedObjects = new List<string>(SereniteaPotComboBox.SelectedItem.ToString().Split('/'))
                .Concat(_checkBoxMappings.Where(pair => pair.Key.IsChecked == true).Select(pair => pair.Value)).ToList();

            ViewModel.SelectedConfig.SecretTreasureObjects = selectedObjects;
            ViewModel.SaveConfig();
            
            Popup.IsOpen = false;
        }
        
        else if (sender.Equals(PotButton)) //打开初始化显示购买信息
        {
            if (ViewModel.SelectedConfig.SecretTreasureObjects.Count == 0)
            {
                SereniteaPotComboBox.SelectedItem = new List<string> { "每天重复" };
                ViewModel.SaveConfig();
                return;
            }
            SereniteaPotComboBox.SelectedItem = ViewModel.SelectedConfig.SecretTreasureObjects[0];

            foreach (var pair in _checkBoxMappings)
            {
                pair.Key.IsChecked = ViewModel.SelectedConfig.SecretTreasureObjects.Contains(pair.Value);
            }
        }
        
    }
    
}
