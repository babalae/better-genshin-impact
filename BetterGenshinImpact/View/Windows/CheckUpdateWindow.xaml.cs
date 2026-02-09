using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Windows.System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Model;
using Meziantou.Framework.Win32;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BetterGenshinImpact.View.Windows;

[ObservableObject]
public partial class CheckUpdateWindow : FluentWindow
{
    public Func<object, CheckUpdateWindowButton, Task>? UserInteraction = null!;

    [ObservableProperty] private bool showUpdateStatus = false;

    [ObservableProperty] private string updateStatusMessage = string.Empty;

    [ObservableProperty] private bool showOtherUpdateTip = false;

    [ObservableProperty] private string selectedGitSource = "CNB";

    public string GitSourceDescription => SelectedGitSource == "CNB" ? Lang.S["View_12167_e9f73b"] : "【国外】直接从 Github 下载并更新";

    partial void OnSelectedGitSourceChanged(string value)
    {
        OnPropertyChanged(nameof(GitSourceDescription));
    }

    private UpdateOption _option;

    public CheckUpdateWindow(UpdateOption option)
    {
        _option = option ?? throw new ArgumentNullException(nameof(option));
        DataContext = this;
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);

        // 存在CDK则显示修改按钮
        if (string.IsNullOrEmpty(MirrorChyanHelper.GetCdk()))
        {
            EditCdkButton.Visibility = Visibility.Collapsed;
        }

        if (option.Trigger == UpdateTrigger.Manual)
        {
            IgnoreButton.Visibility = Visibility.Collapsed;
        }

        if (option.Channel == UpdateChannel.Alpha)
        {
            WebpagePanel.Height = 0;
            WebpagePanel.Visibility = Visibility.Collapsed;
            UpdateStatusMessageGrid.Height = 0;
            ShowUpdateStatus = false;

            // 隐藏开源渠道和Steambird服务卡片
            GitSourceCard.Visibility = Visibility.Collapsed;
            // SteambirdCard.Visibility = Visibility.Collapsed;
            
            SizeToContent = SizeToContent.Height; // 设置高度为自动
            UpdateLayout();
        }


        Closing += OnClosing;

        // 延迟显示气泡提示
        if (option.Channel != UpdateChannel.Alpha)
        {
            var showTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(0.8)
            };
            showTimer.Tick += (s, e) =>
            {
                showTimer.Stop();
                ShowOtherUpdateTip = true;

                // 5秒后自动消失
                var hideTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(6)
                };
                hideTimer.Tick += (s2, e2) =>
                {
                    hideTimer.Stop();
                    ShowOtherUpdateTip = false;
                };
                hideTimer.Start();
            };
            showTimer.Start();
        }
    }

    protected void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ShowUpdateStatus)
        {
            e.Cancel = true;
        }
    }

    public void NavigateToHtml(string html)
    {
        WebpagePanel?.NavigateToHtml(html);
    }

    [RelayCommand]
    private async Task BackgroundUpdateAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.BackgroundUpdate);
        }
    }

    [RelayCommand]
    private async Task OtherUpdateAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.OtherUpdate);
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.Update);
        }
    }

    [RelayCommand]
    private async Task UpdateFromGitHostPlatformAsync()
    {
        string source = SelectedGitSource == "CNB" ? "cnb" : "github";

        if (source == "github")
        {
            // 提示用户这个是国外服务器，可能会很慢
            var result = await ThemedMessageBox.ShowAsync(Lang.S["View_12166_45b230"],
                Lang.S["MsgBox_Title_Warning"], System.Windows.MessageBoxButton.OKCancel, ThemedMessageBox.MessageBoxIcon.Warning);
            if (result != MessageBoxResult.OK)
            {
                return;
            }
        }

        await RunUpdaterAsync($"-I --source {source}");
    }


    [RelayCommand]
    private async Task UpdateFromSteambirdAsync()
    {
        if (_option.Channel == UpdateChannel.Stable)
        {
            await RunUpdaterAsync("-I");
        }
        else
        {
            await RunUpdaterAsync("-I --source dfs-alpha");
        }
    }

    [RelayCommand]
    private async Task UpdateFromMirrorChyanAsync()
    {
        // 没输入 CDK 的情况下提示这是收费渠道
        if (string.IsNullOrEmpty(MirrorChyanHelper.GetCdk()))
        {
            var result = await ThemedMessageBox.ShowAsync(Lang.S["View_12165_8c472e"],
                Lang.S["View_12164_75607d"], System.Windows.MessageBoxButton.OKCancel, ThemedMessageBox.MessageBoxIcon.Warning);
            if (result != MessageBoxResult.OK)
            {
                return;
            }
        }
        
        
        var cdk = MirrorChyanHelper.GetAndPromptCdk();
        if (string.IsNullOrEmpty(cdk))
        {
            return;
        }

        if (_option.Channel == UpdateChannel.Stable)
        {
            await RunUpdaterAsync("-I --source mirrorc");
        }
        else
        {
            await RunUpdaterAsync("-I --source mirrorc-alpha");
        }
    }

    /// <summary>
    ///  --source mirrorc
    ///  --source mirrorc-alpha
    ///  --source github
    ///  --dfs-extras {"hutao-token": "...."}
    /// </summary>
    private async Task RunUpdaterAsync(string parameters)
    {
        // 唤起更新程序
        string updaterExePath = Global.Absolute("BetterGI.update.exe");
        if (!File.Exists(updaterExePath))
        {
            await ThemedMessageBox.ErrorAsync(Lang.S["Update_UpdaterNotFound"]);
            return;
        }

        // 启动
        Process.Start(updaterExePath, parameters);

        // 退出程序
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private async Task IgnoreAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.Ignore);
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.Cancel);
        }
    }

    [RelayCommand]
    private void EditCdk()
    {
        MirrorChyanHelper.EditCdk();
    }

    private void OnCloseTipClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ShowOtherUpdateTip = false;
    }

    public enum CheckUpdateWindowButton
    {
        BackgroundUpdate,
        OtherUpdate,
        Update,
        Ignore,
        Cancel,
    }
}