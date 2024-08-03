using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Script.Group;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class ScriptControlViewModel : ObservableObject, INavigationAware, IViewModel
{
    [ObservableProperty]
    private ObservableCollection<ScriptGroup> _scriptGroups = [];

    public void OnNavigatedFrom()
    {
    }

    public void OnNavigatedTo()
    {
    }

    public ScriptControlViewModel()
    {
    }
}
