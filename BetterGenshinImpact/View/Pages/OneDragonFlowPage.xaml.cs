using BetterGenshinImpact.Helpers;
﻿using BetterGenshinImpact.ViewModel.Pages;
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
    
    public static readonly List<string> SereniteaPotSchedule = new List<string> { Lang.S["GameTask_11571_33f80e"], Lang.S["GameTask_11578_5ce438"], Lang.S["GameTask_11577_34e521"], Lang.S["GameTask_11576_711d99"], Lang.S["GameTask_11575_3df6af"], Lang.S["GameTask_11574_450ea3"], Lang.S["GameTask_11573_1ae72f"], Lang.S["GameTask_11572_67b195"] };

    public OneDragonFlowPage(OneDragonFlowViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        
        _checkBoxMappings = new Dictionary<CheckBox, string>
        {
            { ClothCheckBox, Lang.S["OneDragon_046_92f5e1"] },
            { MomentResinCheckBox, Lang.S["OneDragon_047_6fe57c"] },
            { SereniteapotExpBookCheckBox, Lang.S["OneDragon_048_5c94a2"] },
            { SereniteapotExpBookSmallCheckBox, Lang.S["OneDragon_049_7d0006"] },
            { MagicmineralprecisionCheckBox, Lang.S["OneDragon_050_5787cc"] },
            { MOlaCheckBox, Lang.S["OneDragon_051_077b44"] },
            { ExpBottleBigCheckBox, Lang.S["OneDragon_052_8dc5de"] },
            { ExpBottleSmallCheckBox, Lang.S["OneDragon_053_374692"] }
        };
        
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
                ViewModel.SelectedConfig.SecretTreasureObjects.Add(Lang.S["GameTask_11571_33f80e"]); 
            }
          
            SereniteaPotComboBox.SelectedItem = ViewModel.SelectedConfig.SecretTreasureObjects[0];
            
            foreach (var pair in _checkBoxMappings)
            {
                pair.Key.IsChecked = ViewModel.SelectedConfig.SecretTreasureObjects.Contains(pair.Value);
            }

        }
        
    }
    
}
