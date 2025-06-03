using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Windows.System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Model;
using Meziantou.Framework.Win32;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.View.Windows;

[ObservableObject]
public partial class CheckUpdateWindow : FluentWindow
{
    public Func<object, CheckUpdateWindowButton, Task>? UserInteraction = null!;

    [ObservableProperty] private bool showUpdateStatus = false;

    [ObservableProperty] private string updateStatusMessage = string.Empty;

    private UpdateOption _option;

    public CheckUpdateWindow(UpdateOption option)
    {
        _option = option ?? throw new ArgumentNullException(nameof(option));
        DataContext = this;
        InitializeComponent();
        
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
            
            // 删除前两行
            MyGrid.RowDefinitions.RemoveAt(0);
            MyGrid.RowDefinitions.RemoveAt(0);
            
            // 注意：删除行定义后，需要调整剩余元素的 Grid.Row 属性
            foreach (FrameworkElement child in MyGrid.Children)
            {
                int currentRow = System.Windows.Controls.Grid.GetRow(child);
                if (currentRow > 1) // 如果元素在第三行或之后
                {
                    Grid.SetRow(child, currentRow - 2); // 行号减2
                }
            }
            
            if (ServerPanel.Children.Count > 0)
            {
                ServerPanel.Children.RemoveAt(0);
            }
            SizeToContent = SizeToContent.Height; // 设置高度为自动
            UpdateLayout();
        }


        Closing += OnClosing;
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
    private async Task UpdateFromSteambirdAsync()
    {
        await RunUpdaterAsync("-I");
    }

    [RelayCommand]
    private async Task UpdateFromMirrorChyanAsync()
    {
        var cdk = MirrorChyanHelper.GetAndPromptCdk();
        if (string.IsNullOrEmpty(cdk))
        {
            return;
        }
        
        if (_option.Channel == UpdateChannel.Stable)
        {
            await RunUpdaterAsync("--source mirrorc");
        }
        else
        {
            await RunUpdaterAsync("--source mirrorc-alpha");
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
            await MessageBox.ErrorAsync("更新程序不存在，请选择其他更新方式！");
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

    public enum CheckUpdateWindowButton
    {
        BackgroundUpdate,
        OtherUpdate,
        Update,
        Ignore,
        Cancel,
    }
}