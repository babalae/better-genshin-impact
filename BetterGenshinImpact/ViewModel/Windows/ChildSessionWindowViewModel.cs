using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class ChildSessionWindowViewModel : ViewModel
{
    private readonly ChildSessionService _childSessionService;

    [ObservableProperty]
    private Brush _connectionStatusBrush = Brushes.Red;

    [ObservableProperty]
    private string _connectionStatusToolTip = "桌面分身未启动";

    [ObservableProperty]
    private bool _isTopmost;

    [ObservableProperty]
    private bool _isAdaptive = true;

    [ObservableProperty]
    private bool _isOneToOne;

    [ObservableProperty]
    private bool _keepAspectRatio = true;

    public bool IsDefaultResolutionSelected => true;

    public string TopmostButtonToolTip => IsTopmost ? "取消置顶" : "置顶";

    public bool HasChildSession => _childSessionService.ChildSessionId is not null;

    public ChildSessionWindowViewModel(ChildSessionService childSessionService)
    {
        _childSessionService = childSessionService;
        _childSessionService.StateChanged += OnChildSessionStateChanged;
        UpdateConnectionStatus();
    }

    public Task LogoffAndHideAsync()
    {
        return ExecuteAsync(_childSessionService.LogoffAndHideAsync);
    }

    partial void OnIsTopmostChanged(bool value)
    {
        OnPropertyChanged(nameof(TopmostButtonToolTip));
    }

    [RelayCommand]
    private Task StartAsync()
    {
        return ExecuteAsync(_childSessionService.StartAsync);
    }

    [RelayCommand]
    private void Hide()
    {
        Execute(_childSessionService.HideWindow);
    }

    [RelayCommand]
    private void SwitchWindow()
    {
        Execute(_childSessionService.ShowChildSessionTaskView);
    }

    [RelayCommand]
    private Task LaunchBetterGiAsync()
    {
        return ExecuteAsync(_childSessionService.LaunchBetterGiAsync);
    }

    [RelayCommand]
    private void SelectDefaultResolution()
    {
        OnPropertyChanged(nameof(IsDefaultResolutionSelected));
    }

    [RelayCommand]
    private void UseAdaptive()
    {
        if (!Execute(() => _childSessionService.SetSmartSizing(true)))
        {
            return;
        }

        IsAdaptive = true;
        IsOneToOne = false;
        OnPropertyChanged(nameof(IsAdaptive));
        OnPropertyChanged(nameof(IsOneToOne));
    }

    [RelayCommand]
    private void UseOneToOne()
    {
        if (!Execute(() => _childSessionService.SetSmartSizing(false)))
        {
            return;
        }

        IsAdaptive = false;
        IsOneToOne = true;
        OnPropertyChanged(nameof(IsAdaptive));
        OnPropertyChanged(nameof(IsOneToOne));
    }

    [RelayCommand]
    private void ToggleKeepAspectRatio()
    {
        KeepAspectRatio = !KeepAspectRatio;
    }

    [RelayCommand]
    private void ShowDesktop()
    {
        Execute(_childSessionService.ShowChildSessionDesktop);
    }

    [RelayCommand]
    private void ShowTaskView()
    {
        Execute(_childSessionService.ShowChildSessionTaskView);
    }

    [RelayCommand]
    private async Task LaunchExecutableAsync()
    {
        var fileDialog = new OpenFileDialog
        {
            Title = "选择要在 BetterGI 桌面分身中以管理员权限启动的程序",
            Filter = "Windows 程序 (*.exe)|*.exe",
            CheckFileExists = true,
            CheckPathExists = true,
            DereferenceLinks = true,
            Multiselect = false
        };

        if (fileDialog.ShowDialog() != true)
        {
            return;
        }

        var executablePath = Path.GetFullPath(fileDialog.FileName);
        await ExecuteAsync(() => _childSessionService.LaunchExecutableAsync(executablePath));
    }

    [RelayCommand]
    private void ToggleTopmost()
    {
        IsTopmost = !IsTopmost;
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            UpdateConnectionStatus();
        }
    }

    private bool Execute(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception)
        {
            ShowError(exception);
            return false;
        }
        finally
        {
            UpdateConnectionStatus();
        }
    }

    private void OnChildSessionStateChanged(object? sender, EventArgs e)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            UpdateConnectionStatus();
            return;
        }

        _ = Application.Current.Dispatcher.BeginInvoke(UpdateConnectionStatus);
    }

    private void UpdateConnectionStatus()
    {
        if (_childSessionService.ConnectedState == 1)
        {
            ConnectionStatusBrush = Brushes.LimeGreen;
        }
        else if (_childSessionService.ChildSessionId is not null)
        {
            ConnectionStatusBrush = Brushes.DodgerBlue;
        }
        else
        {
            ConnectionStatusBrush = Brushes.Red;
        }

        ConnectionStatusToolTip = _childSessionService.StatusText;
    }

    private static void ShowError(Exception exception)
    {
        var actualException = exception.GetBaseException();
        var suggestion = actualException is System.ComponentModel.Win32Exception { NativeErrorCode: 5 }
            ? "\n\n操作被系统拒绝，请确认 BetterGI 正在以管理员权限运行。"
            : string.Empty;

        ThemedMessageBox.Error(
            $"{actualException.Message}{suggestion}",
            "BetterGI 桌面分身");
    }
}
