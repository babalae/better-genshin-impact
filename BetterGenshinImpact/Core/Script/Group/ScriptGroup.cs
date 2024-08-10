using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace BetterGenshinImpact.Core.Script.Group;

public partial class ScriptGroup : ObservableObject
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    [ObservableProperty]
    private ObservableCollection<ScriptGroupProject> _projects = [];

    public ScriptGroup()
    {
        Projects.CollectionChanged += ProjectsCollectionChanged;
    }

    private void ProjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Projects));
    }
}
