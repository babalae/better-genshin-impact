using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Model;

public partial class OneDragonTaskItem : ObservableObject
{
    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private Brush? _statusColor;

    [ObservableProperty]
    private bool _isEnabled;
}
