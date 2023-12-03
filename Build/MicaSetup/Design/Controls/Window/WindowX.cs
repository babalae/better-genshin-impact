using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaSetup.Natives;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;

namespace MicaSetup.Design.Controls;

[INotifyPropertyChanged]
public partial class WindowX : Window
{
    public HwndSource? HwndSource { get; private set; }
    public nint? Handle { get; private set; }
    public SnapLayout SnapLayout { get; } = new();

    public bool IsActivated
    {
        get => (bool)GetValue(IsActivatedProperty);
        set => SetValue(IsActivatedProperty, value);
    }

    public static readonly DependencyProperty IsActivatedProperty = DependencyProperty.Register(nameof(IsActivated), typeof(bool), typeof(WindowX), new PropertyMetadata(false));

    public WindowX()
    {
        Loaded += (s, e) =>
        {
            HwndSource = (PresentationSource.FromVisual(this) as HwndSource)!;
            HwndSource.AddHook(new HwndSourceHook(WndProc));
            Handle = new WindowInteropHelper(this).Handle;

            if (GetTemplateChild("PART_BtnMaximize") is Button buttonMaximize)
            {
                if (SnapLayout.IsSupported)
                {
                    SnapLayout.Register(buttonMaximize);
                }
            }
        };
    }

    protected private virtual nint WndProc(nint hWnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        return SnapLayout.WndProc(hWnd, msg, wParam, lParam, ref handled);
    }

    protected override void OnActivated(EventArgs e)
    {
        IsActivated = true;
        _ = WindowBackdrop.ApplyBackdrop(this, WindowBackdropType.Mica, ThemeService.Current.CurrentTheme switch
        {
            WindowsTheme.Dark => ApplicationTheme.Dark,
            WindowsTheme.Light => ApplicationTheme.Light,
            WindowsTheme.Auto or _ => ApplicationTheme.Unknown,
        });
        base.OnActivated(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        IsActivated = false;
        base.OnDeactivated(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowBackdrop.ApplyBackdrop(this, WindowBackdropType.None, ApplicationTheme.Unknown);
        NativeMethods.HideAllWindowButton(new WindowInteropHelper(this).Handle);
    }

    public override void EndInit()
    {
        ApplyResizeBorderThickness(WindowState);
        base.EndInit();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.Property.Name is nameof(WindowState))
        {
            ApplyResizeBorderThickness((WindowState)e.NewValue);
        }
        base.OnPropertyChanged(e);
    }

    private void ApplyResizeBorderThickness(WindowState windowsState)
    {
        if (windowsState == WindowState.Maximized || ResizeMode == ResizeMode.NoResize || ResizeMode == ResizeMode.CanMinimize)
        {
            WindowChrome.SetWindowChrome(this, new WindowChrome()
            {
                CaptionHeight = 0d,
                CornerRadius = new CornerRadius(8d),
                GlassFrameThickness = new Thickness(-1d),
                ResizeBorderThickness = new Thickness(0d),
            });
        }
        else
        {
            WindowChrome.SetWindowChrome(this, new WindowChrome()
            {
                CaptionHeight = 0d,
                CornerRadius = new CornerRadius(8d),
                GlassFrameThickness = new Thickness(-1d),
                ResizeBorderThickness = new Thickness(3d),
            });
        }
    }

    [RelayCommand]
    private void CloseWindow()
    {
        WindowSystemCommands.CloseWindow(this);
    }

    [RelayCommand]
    private void MinimizeWindow()
    {
        WindowSystemCommands.MinimizeWindow(this);
    }

    [RelayCommand]
    private void MaximizeWindow()
    {
        WindowSystemCommands.MaximizeWindow(this);
    }

    [RelayCommand]
    private void RestoreWindow()
    {
        WindowSystemCommands.RestoreWindow(this);
    }

    [RelayCommand]
    private void MaximizeOrRestoreWindow()
    {
        WindowSystemCommands.MaximizeOrRestoreWindow(this);
    }

    [RelayCommand]
    private void ShowSystemMenu()
    {
        WindowSystemCommands.ShowSystemMenu(this);
    }
}
