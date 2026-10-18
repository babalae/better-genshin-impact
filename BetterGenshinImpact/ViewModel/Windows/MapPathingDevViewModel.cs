using System.Collections.Generic;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class MapPathingDevViewModel: ObservableObject
{
    public IEnumerable<EnumItem<DisplayMapTypes>> MapTypeItems { get; } = EnumExtensions.ToEnumItems<DisplayMapTypes>();

    private MapViewer? _mapViewer;
    
    // [ObservableProperty]
    // private string _selectedMapType = DevConfig.RecordMapName;

    /// <summary>
    /// 开发者配置。本 ViewModel 由窗口直接 new，没有注入入口，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    public DevConfig DevConfig { get; set; } = BetterGenshinImpact.Service.ConfigService.Config!.DevConfig;
    
    [RelayCommand]
    private void DropDownChanged()
    {
        // DevConfig.RecordMapName = SelectedMapType;
    }
    
    [RelayCommand]
    private void OpenMapViewer()
    {
        if (_mapViewer == null || !_mapViewer.IsVisible)
        {
            _mapViewer = new MapViewer(DevConfig.RecordMapName);
            _mapViewer.Closed += (s, e) => _mapViewer = null;
            _mapViewer.Show();
        }
        else
        {
            _mapViewer.Activate();
        }
    }

    [RelayCommand]
    private void OpenMapEditor()
    {
        PathRecorder.Instance.OpenEditorInWebView(DevConfig.RecordMapName);
    }
}