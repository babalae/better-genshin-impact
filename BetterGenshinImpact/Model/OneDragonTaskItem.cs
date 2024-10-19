using System;
using BetterGenshinImpact.ViewModel.Pages.OneDragon;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace BetterGenshinImpact.Model;

public partial class OneDragonTaskItem : ObservableObject
{
    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private Brush _statusColor = Brushes.Gray;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private OneDragonBaseViewModel? _viewModel;

    public OneDragonTaskItem(OneDragonBaseViewModel viewModel)
    {
        _viewModel = viewModel;
        Name = viewModel.Title;
    }

    public OneDragonTaskItem(Type viewModelType)
    {
        ViewModel = App.GetService(viewModelType) as OneDragonBaseViewModel;
        if (ViewModel == null)
        {
            throw new ArgumentException("Invalid view model type", nameof(viewModelType));
        }
        Name = ViewModel.Title;
    }
}
