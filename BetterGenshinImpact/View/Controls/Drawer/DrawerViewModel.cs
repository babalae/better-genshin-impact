using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.View.Controls.Drawer;

public partial class DrawerViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDrawerOpen;

    [ObservableProperty]
    private object? _drawerContent;

    [ObservableProperty]
    private DrawerPosition _drawerPosition = DrawerPosition.Right;

    [ObservableProperty]
    private double _drawerWidth = 300;

    [ObservableProperty]
    private double _drawerHeight = 300;

    [RelayCommand]
    public void OpenDrawer(object content)
    {
        DrawerContent = content;
        IsDrawerOpen = true;
    }

    [RelayCommand]
    public void CloseDrawer()
    {
        IsDrawerOpen = false;
    }

    [RelayCommand]
    public void ToggleDrawer(object? content = null)
    {
        if (IsDrawerOpen)
        {
            CloseDrawer();
        }
        else
        {
            if (content != null)
            {
                DrawerContent = content;
            }
            IsDrawerOpen = true;
        }
    }
}