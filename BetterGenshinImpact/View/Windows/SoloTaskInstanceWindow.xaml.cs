using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.ViewModel.Windows;
using System;
using System.Threading.Tasks;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 多实例独立任务启动与停止窗口。
/// </summary>
public partial class SoloTaskInstanceWindow : FluentWindow
{
    /// <summary>
    /// 窗口使用的实例管理 ViewModel。
    /// </summary>
    public SoloTaskInstanceWindowViewModel ViewModel { get; }

    /// <summary>
    /// 创建模态实例管理窗口。
    /// </summary>
    public SoloTaskInstanceWindow(
        InstanceService instanceService,
        SoloTaskType taskType,
        Func<Task<SoloTaskStartDefinition?>> definitionFactory)
    {
        ViewModel = new SoloTaskInstanceWindowViewModel(instanceService, taskType, definitionFactory);
        DataContext = ViewModel;
        InitializeComponent();
        // 按实例数量收紧默认高度；实例较多时由列表滚动承载，避免窗口占用过大。
        Height = Math.Clamp(218 + ViewModel.Instances.Count * 58, MinHeight, MaxHeight);
        ViewModel.CloseRequested += OnCloseRequested;
    }

    /// <summary>
    /// 接收 ViewModel 的关闭请求。
    /// </summary>
    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 关闭窗口时解除事件订阅并释放 ViewModel。
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        ViewModel.CloseRequested -= OnCloseRequested;
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
