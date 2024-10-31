using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Model;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public partial class PathingConfigViewModel : ObservableObject, IViewModel
{
    [ObservableProperty]
    private ObservableCollection<Condition> _conditionConfigs = [];

    [ObservableProperty]
    private List<string> _subjects = ConditionDefinitions.Definitions.Keys.ToList();

    [RelayCommand]
    public void OnAddConditionConfig()
    {
        ConditionConfigs.Add(new Condition());
    }
}
