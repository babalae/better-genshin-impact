using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class NotificationEventOption(string code, string displayName) : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public string Code { get; } = code;

    public string DisplayName { get; } = displayName;
}
