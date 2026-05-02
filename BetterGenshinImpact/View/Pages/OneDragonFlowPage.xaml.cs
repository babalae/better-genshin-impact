using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Violeta.Controls;
using System.Linq; 
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Script.Group;
using System;

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
        
        // 监听ViewModel的属性变化
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }
    
    private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ShouldShowAddTaskGroupPopup) && ViewModel.ShouldShowAddTaskGroupPopup)
        {
            // 重置属性
            ViewModel.ShouldShowAddTaskGroupPopup = false;
            
            // 显示弹窗
            ShowAddTaskGroupPopup();
        }
    }
    
    private void ShowAddTaskGroupPopup()
    {
        try
        {
            // 读取配置组
            ViewModel.ReadScriptGroup();
            
            // 检查是否有ScriptGroups
            if (ViewModel.ScriptGroups == null || !ViewModel.ScriptGroups.Any())
            {
                Toast.Warning("没有找到配置组");
                return;
            }
            
            // 过滤已存在的配置组
            var availableGroups = ViewModel.ScriptGroups
                .Where(sg => ViewModel.TaskList == null || !ViewModel.TaskList.Any(task => task.Name == sg.Name))
                .ToList();
                
            if (!availableGroups.Any())
            {
                Toast.Warning("没有可添加的配置组");
                return;
            }
            
            // 设置ItemsControl的数据源
            ScriptGroupItemsControl.ItemsSource = availableGroups;
            
            // 获取主窗口
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                // 获取主窗口的位置和大小
                var windowLeft = mainWindow.Left;
                var windowTop = mainWindow.Top;
                var windowWidth = mainWindow.ActualWidth;
                var windowHeight = mainWindow.ActualHeight;
                
                // 估算弹窗大小
                var popupWidth = 350.0;
                var popupHeight = 300.0;
                
                // 计算居中位置（相对于主窗口）
                AddConfigGroupPopup.HorizontalOffset = windowLeft + (windowWidth - popupWidth) / 2;
                AddConfigGroupPopup.VerticalOffset = windowTop + (windowHeight - popupHeight) / 2;
            }
            
            // 显示弹窗
            AddConfigGroupPopup.IsOpen = true;
        }
        catch (Exception ex)
        {
            Toast.Error($"添加任务组时出错: {ex.Message}");
        }
    }
    
    private void AddTaskGroupButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAddTaskGroupPopup();
    }
    
    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedGroups = new List<string>();
        
        // 遍历所有CheckBox，收集选中的项
        foreach (var item in ScriptGroupItemsControl.Items)
        {
            var container = ScriptGroupItemsControl.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            if (container != null)
            {
                var checkBox = FindVisualChild<CheckBox>(container);
                if (checkBox?.IsChecked == true && checkBox.Tag is ScriptGroup scriptGroup)
                {
                    selectedGroups.Add(scriptGroup.Name);
                }
            }
        }
        
        if (selectedGroups.Any())
        {
            // 调用ViewModel的方法处理选中的配置组
            ViewModel.ProcessSelectedGroups(selectedGroups);
            
            // 保存配置并重置选中项
            ViewModel.SaveConfig();
            ViewModel.SelectedTask = null;
        }
        
        AddConfigGroupPopup.IsOpen = false;
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        AddConfigGroupPopup.IsOpen = false;
    }
    
    // 辅助方法：查找子控件
    private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;
            
            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
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
