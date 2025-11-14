using System;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 仓库更新提示对话框
/// </summary>
public partial class RepoUpdateDialog : Wpf.Ui.Controls.FluentWindow
{
    private System.Windows.Threading.DispatcherTimer? _dialogTimer;
    private int _remainingSeconds;
    private readonly int _daysSinceUpdate;
    private TaskCompletionSource<MessageBoxResult>? _taskCompletionSource;

    /// <summary>
    /// 初始化仓库更新提示对话框
    /// </summary>
    /// <param name="daysSinceUpdate">距上次更新的天数</param>
    public RepoUpdateDialog(int daysSinceUpdate)
    {
        _daysSinceUpdate = daysSinceUpdate;

        InitializeComponent();

        // 配置窗口属性
        Title = "仓库更新提示";
        MessageTextBlock.Text = $"脚本仓库已经 {daysSinceUpdate} 天未更新\n\n温馨提示：\n脚本内容跟随仓库版本，旧版仓库会订阅到旧版脚本。\n更新仓库后需要重新订阅脚本，以更新脚本内容。\n\n是否立即更新？";
        Owner = Application.Current.MainWindow;

        // 注册事件
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.TryApplySystemBackdrop(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 计算倒计时秒数：30 天为 5 秒，超过 30 天每多 2 天增加 1 秒
        _remainingSeconds = 5 + (_daysSinceUpdate - 30) / 2;
        SecondaryButton.Content = $"直接打开 ({_remainingSeconds}s)";
        StartDialogTimer();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopDialogTimer();
        _taskCompletionSource?.TrySetResult(MessageBoxResult.None);
    }

    /// <summary>
    /// 显示对话框并等待结果
    /// </summary>
    public Task<MessageBoxResult> ShowDialogAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<MessageBoxResult>();
        ShowDialog();
        return _taskCompletionSource.Task;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        _taskCompletionSource?.TrySetResult(MessageBoxResult.Primary);
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        _taskCompletionSource?.TrySetResult(MessageBoxResult.Secondary);
        Close();
    }

    /// <summary>
    /// 启动对话框定时器
    /// </summary>
    private void StartDialogTimer()
    {
        // 创建定时器
        _dialogTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _dialogTimer.Tick += OnTimerTick;
        _dialogTimer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;

        if (_remainingSeconds > 0)
        {
            SecondaryButton.Content = $"直接打开 ({_remainingSeconds}s)";
        }
        else
        {
            // 倒计时结束，启用按钮
            SecondaryButton.Content = "直接打开";
            SecondaryButton.IsEnabled = true;
            _dialogTimer?.Stop();
        }
    }

    /// <summary>
    /// 停止对话框定时器
    /// </summary>
    private void StopDialogTimer()
    {
        if (_dialogTimer != null)
        {
            _dialogTimer.Tick -= OnTimerTick;
            _dialogTimer.Stop();
            _dialogTimer = null;
        }
    }
}
