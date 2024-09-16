using BetterGenshinImpact.GameTask.AutoPathing.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.Model;

[ObservableObject]
public partial class PathingTaskTreeObject : TreeObject<PathingTaskTreeObject>
{
    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private bool _isFolder;

    [ObservableProperty]
    private PathingTask? _task;
}
