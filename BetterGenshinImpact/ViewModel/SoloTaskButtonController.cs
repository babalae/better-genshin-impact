using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using Newtonsoft.Json.Linq;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 独立任务按钮（<see cref="BetterGenshinImpact.View.Controls.SoloTaskButton"/>）的行为：
/// 按实例状态刷新按钮，单实例时直接启动或停止，多实例时打开实例管理窗口。独立任务页与一条龙页共用
/// </summary>
public sealed class SoloTaskButtonController
{
    /// <summary>
    /// 多实例连接与独立任务操作服务
    /// </summary>
    private readonly InstanceService _instanceService;

    /// <summary>
    /// 独立任务注册表，提供显示名与参数序列化
    /// </summary>
    private readonly SoloTaskRegistry _registry;

    /// <summary>
    /// 配置服务，生成启动定义前把配置落盘，供目标实例读取
    /// </summary>
    private readonly IConfigService _configService;

    /// <summary>
    /// 由本控制器刷新的按钮状态
    /// </summary>
    private readonly IReadOnlyList<SoloTaskButtonState> _states;

    /// <summary>
    /// 本控制器发出的启动或停止请求是否仍在等待目标实例确认；为 true 时所有按钮统一禁用
    /// </summary>
    private bool _operationPending;

    /// <summary>
    /// 按钮状态刷新完成后在 UI 线程触发
    /// </summary>
    public event EventHandler? Refreshed;

    /// <summary>
    /// 创建按钮控制器，订阅实例连接与任务状态变化并立即刷新一次
    /// </summary>
    /// <param name="instanceService">多实例连接与独立任务操作服务</param>
    /// <param name="registry">独立任务注册表</param>
    /// <param name="configService">配置服务</param>
    /// <param name="states">由本控制器刷新的按钮状态</param>
    public SoloTaskButtonController(
        InstanceService instanceService,
        SoloTaskRegistry registry,
        IConfigService configService,
        IEnumerable<SoloTaskButtonState> states)
    {
        _instanceService = instanceService;
        _registry = registry;
        _configService = configService;
        _states = states.ToList();
        _instanceService.ConnectedInstancesChanged += OnInstancesChanged;
        _instanceService.SoloTaskStatusesChanged += OnInstancesChanged;
        Refresh();
    }

    /// <summary>
    /// 根据当前可管理实例数量和真实任务状态刷新全部按钮
    /// </summary>
    public void Refresh()
    {
        var targets = _instanceService.GetSoloTaskTargets();
        var statuses = targets.Select(_instanceService.GetSoloTaskStatus).ToArray();
        foreach (var state in _states)
        {
            var taskId = state.TaskId;
            var activeRunCount = statuses.Count(status => status.IsActive && status.TaskId == taskId);
            var singleState = targets.Length == 1 && statuses[0].TaskId == taskId
                ? statuses[0].State
                : SoloTaskRunState.Idle;
            var isBlockedByOtherTask = targets.Length == 1
                                       && statuses[0].IsActive
                                       && statuses[0].TaskId != taskId;
            state.Update(targets.Length, activeRunCount, singleState, isBlockedByOtherTask, _operationPending);
        }

        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 按参数对象生成启动定义。生成前把配置落盘：子实例从同一份配置文件读取任务用到的其余配置
    /// </summary>
    /// <param name="taskId">任务标识</param>
    /// <param name="param">参数对象，类型必须是该任务的参数类型</param>
    /// <returns>启动定义</returns>
    public SoloTaskStartDefinition CreateDefinition(string taskId, object param)
    {
        var descriptor = _registry.Get(taskId);
        _configService.Flush();
        return new SoloTaskStartDefinition
        {
            TaskId = taskId,
            Parameters = JObject.Parse(descriptor.SerializeParam(param))
        };
    }

    /// <summary>
    /// 处理按钮点击：多实例时打开实例管理窗口；单实例时运行中则停止，空闲则启动
    /// </summary>
    /// <param name="taskId">任务标识</param>
    /// <param name="definitionFactory">启动前生成启动定义；返回 null 表示校验失败或用户取消</param>
    public async Task ManageAsync(string taskId, Func<Task<SoloTaskStartDefinition?>> definitionFactory)
    {
        var targets = _instanceService.GetSoloTaskTargets();
        if (targets.Length > 1)
        {
            var window = new SoloTaskInstanceWindow(_instanceService, _registry, taskId, definitionFactory)
            {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
            Refresh();
            return;
        }

        var target = targets.Single();
        var status = _instanceService.GetSoloTaskStatus(target);
        if (status.IsActive && status.TaskId != taskId)
        {
            Toast.Warning($"当前实例正在运行“{_registry.GetDisplayName(status.TaskId)}”，请先等待该任务结束或使用其停止入口。");
            return;
        }

        _operationPending = true;
        Refresh();
        try
        {
            SoloTaskOperationResponse response;
            if (status.IsActive && status.RunId is Guid runId)
            {
                response = await _instanceService.StopSoloTaskAsync(target, runId);
            }
            else
            {
                var definition = await definitionFactory();
                if (definition is null)
                {
                    return;
                }

                response = await _instanceService.StartSoloTaskAsync(target, definition.CreateRequest());
            }

            if (!response.Accepted)
            {
                Toast.Warning(response.ErrorMessage ?? "目标实例拒绝了本次操作。");
            }
        }
        catch (TimeoutException ex)
        {
            try
            {
                await _instanceService.RefreshSoloTaskStatusAsync(target);
            }
            catch
            {
                // 查询失败时保留原始超时提示，连接状态事件会继续校正按钮。
            }

            await ThemedMessageBox.WarningAsync($"独立任务操作等待确认超时：{ex.Message}", "独立任务");
        }
        catch (Exception ex)
        {
            await ThemedMessageBox.ErrorAsync($"独立任务操作失败：{ex.Message}", "独立任务");
        }
        finally
        {
            _operationPending = false;
            Refresh();
        }
    }

    /// <summary>
    /// 在当前实例启动任务，不打开实例管理窗口。用于命令行等只针对本实例的入口
    /// </summary>
    /// <param name="definition">启动定义</param>
    /// <returns>当前实例的启动结果</returns>
    public Task<SoloTaskOperationResponse> StartOnCurrentInstanceAsync(SoloTaskStartDefinition definition)
    {
        return _instanceService.StartSoloTaskAsync(_instanceService.Context.ToEndpoint(), definition.CreateRequest());
    }

    /// <summary>
    /// 实例连接或任务状态变化后切回 UI 线程刷新
    /// </summary>
    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        UIDispatcherHelper.BeginInvoke(Refresh);
    }
}
