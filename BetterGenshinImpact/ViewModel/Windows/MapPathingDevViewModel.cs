using System.Collections.Generic;
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
    
    [ObservableProperty]
    private string _selectedMapType = TaskContext.Instance().Config.DevConfig.RecordMapName;
    
    [RelayCommand]
    private void DropDownChanged()
    {
        TaskContext.Instance().Config.DevConfig.RecordMapName = SelectedMapType;
    }
    
    [RelayCommand]
    private void OpenMapViewer()
    {
        if (_mapViewer == null || !_mapViewer.IsVisible)
        {
            _mapViewer = new MapViewer(SelectedMapType);
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
        PathRecorder.Instance.OpenEditorInWebView(SelectedMapType);
    }
}