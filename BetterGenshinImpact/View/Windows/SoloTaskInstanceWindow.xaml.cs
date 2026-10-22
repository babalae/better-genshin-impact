using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.Helpers.Ui;
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
    /// <param name="instanceService">多实例连接与独立任务操作服务</param>
    /// <param name="registry">独立任务注册表</param>
    /// <param name="taskId">要管理的任务标识</param>
    /// <param name="definitionFactory">第一次启动时收集任务参数的回调</param>
    public SoloTaskInstanceWindow(
        InstanceService instanceService,
        SoloTaskRegistry registry,
        string taskId,
        Func<Task<SoloTaskStartDefinition?>> definitionFactory)
    {
        ViewModel = new SoloTaskInstanceWindowViewModel(instanceService, registry, taskId, definitionFactory);
        DataContext = ViewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
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
