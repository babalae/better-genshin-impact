using System.Windows.Media;
using BetterGenshinImpact.ViewModel;
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

    [ObservableProperty]
    private IViewModel? _viewModel;
}
