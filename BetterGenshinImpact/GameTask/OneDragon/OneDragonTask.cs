using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.OneDragon;

/// <summary>
/// 一条龙：按配置依次执行内置任务和配置组，最后检查奖励并执行完成后操作。
/// 注册为独立任务后，独立任务的按钮、多实例管理窗口与 task.* 跨实例请求都可以直接复用
/// </summary>
[SoloTaskInfo(TaskId, "一条龙", Group = "日常奖励", Order = 100, Scopes = SoloTaskScopes.Standalone)]
public class OneDragonTask : ISoloTask<OneDragonParam, NoData>
{
    /// <summary>
    /// 稳定任务标识
    /// </summary>
    public const string TaskId = "one_dragon";

    /// <summary>
    /// 配置不存在或无法读取时的原因码
    /// </summary>
    public const string ConfigNotFoundReason = "one_dragon_config_not_found";

    /// <summary>
    /// 配置中没有启用任务时的原因码
    /// </summary>
    public const string NoEnabledTaskReason = "one_dragon_no_enabled_task";

    /// <summary>
    /// 配置组文件所在目录
    /// </summary>
    private static readonly string ScriptGroupFolder = Global.Absolute(@"User\ScriptGroup");

    /// <summary>
    /// 一条龙日志
    /// </summary>
    private readonly ILogger<OneDragonTask> _logger = App.GetLogger<OneDragonTask>();

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根。由独立任务运行服务传入启动时从磁盘读取的最新配置
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 配置组执行服务
    /// </summary>
    private readonly IScriptService _scriptService;

    /// <summary>
    /// 创建一条龙任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    /// <param name="scriptService">配置组执行服务</param>
    public OneDragonTask(GameRuntime runtime, AllConfig config, IScriptService scriptService)
    {
        _runtime = runtime;
        _config = config;
        _scriptService = scriptService;
    }

    /// <summary>
    /// 执行一条龙。单个子任务失败只记录日志并继续后续任务；取消时抛出 OperationCanceledException
    /// </summary>
    /// <param name="param">一条龙参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务结果</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(OneDragonParam param, CancellationToken ct)
    {
        // 1. 读取一条龙配置
        var configName = string.IsNullOrEmpty(param.ConfigName) ? _config.SelectedOneDragonFlowConfigName : param.ConfigName;
        OneDragonFlowConfig? flowConfig;
        try
        {
            flowConfig = OneDragonFlowConfigStore.Load(configName);
        }
        catch (Exception e) when (e is IOException or Newtonsoft.Json.JsonException or UnauthorizedAccessException)
        {
            _logger.LogDebug(e, "读取一条龙配置失败");
            return SoloTaskResult.Failed(ConfigNotFoundReason, $"读取一条龙配置「{configName}」失败：{e.Message}");
        }

        if (flowConfig is null)
        {
            return SoloTaskResult.Failed(ConfigNotFoundReason, $"未找到一条龙配置「{configName}」");
        }

        _logger.LogInformation("启用一条龙配置：{Name}", flowConfig.Name);

        // 2. 还原任务列表，按"从此执行"标记跳过前面的任务
        var tasks = SkipToStartTask(OneDragonTaskItem.CreateListFromConfig(flowConfig), param.StartTaskId);
        var enabledTasks = tasks.Where(t => t.IsEnabled).ToList();
        if (enabledTasks.Count == 0)
        {
            return SoloTaskResult.Skipped(NoEnabledTaskReason, "一条龙配置中没有启用的任务");
        }

        foreach (var task in tasks)
        {
            task.InitAction(_config, flowConfig);
        }

        var builtInCount = enabledTasks.Count(t => t.IsBuiltIn);
        var scriptGroupCount = enabledTasks.Count - builtInCount;
        _logger.LogInformation("启用一条龙任务的数量: {Count}", builtInCount);
        _logger.LogInformation("启用配置组任务的数量: {Count}", scriptGroupCount);

        // 首领讨伐累计领奖次数由任务回调修改，修改后立即写回文件
        flowConfig.PropertyChanged += OnFlowConfigPropertyChanged;
        try
        {
            // 3. 依次执行任务
            Notify.Event(NotificationEvent.DragonStart).Success("一条龙启动");
            var builtInIndex = 1;
            var scriptGroupIndex = 1;
            foreach (var task in enabledTasks)
            {
                ct.ThrowIfCancellationRequested();
                if (task.IsBuiltIn)
                {
                    _logger.LogInformation("一条龙任务执行: {Index}/{Count}", builtInIndex++, builtInCount);
                    await task.Action!(_runtime, ct);
                    await Task.Delay(1000, ct);
                }
                else
                {
                    _logger.LogInformation("配置组任务执行: {Index}/{Count}", scriptGroupIndex++, scriptGroupCount);
                    await RunScriptGroupAsync(task.Name, ct);
                }
            }
        }
        finally
        {
            flowConfig.PropertyChanged -= OnFlowConfigPropertyChanged;
        }

        // 4. 检查奖励并通知
        await new CheckRewardsTask(_runtime, _config).Start(ct);
        await Task.Delay(500, ct);
        Notify.Event(NotificationEvent.DragonEnd).Success("一条龙和配置组任务结束");
        _logger.LogInformation("一条龙和配置组任务结束");

        // 5. 完成后操作
        RunCompletionAction(flowConfig.CompletionAction);
        return SoloTaskResult.Success();
    }

    /// <summary>
    /// 有"从此执行"标记时，从被标记的任务开始；找不到标记的任务时从头开始
    /// </summary>
    /// <param name="tasks">完整任务列表</param>
    /// <param name="startTaskId">起始任务 Id</param>
    /// <returns>本次要执行的任务列表</returns>
    private List<OneDragonTaskItem> SkipToStartTask(List<OneDragonTaskItem> tasks, string? startTaskId)
    {
        if (string.IsNullOrEmpty(startTaskId))
        {
            return tasks;
        }

        var index = tasks.FindIndex(t => t.Id == startTaskId);
        if (index < 0)
        {
            _logger.LogWarning("一条龙：未找到标记的任务，将从头开始执行");
            return tasks;
        }

        _logger.LogInformation("一条龙：任务将从 {Name} 开始执行", tasks[index].Name);
        return tasks.Skip(index).ToList();
    }

    /// <summary>
    /// 执行一个配置组。失败只记录日志并提示，不中断一条龙
    /// </summary>
    /// <param name="groupName">配置组名</param>
    /// <param name="ct">取消令牌</param>
    private async Task RunScriptGroupAsync(string groupName, CancellationToken ct)
    {
        try
        {
            Notify.Event(NotificationEvent.DragonStart).Success("配置组任务启动");
            await Task.Delay(500, ct);
            var filePath = Path.Combine(ScriptGroupFolder, $"{groupName}.json");
            var group = ScriptGroup.FromJson(await File.ReadAllTextAsync(filePath, ct));
            await _scriptService.RunMulti(_runtime, ScriptControlViewModel.GetNextProjects(group), group.Name, ct);
            await Task.Delay(1000, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "执行配置组任务时失败");
            Toast.Error("执行配置组任务时失败");
        }
    }

    /// <summary>
    /// 首领讨伐累计领奖次数变化时写回文件；写入失败只记录日志，不中断任务
    /// </summary>
    private void OnFlowConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OneDragonFlowConfig flowConfig
            || e.PropertyName != nameof(OneDragonFlowConfig.AutoBossCompletedRunCount))
        {
            return;
        }

        try
        {
            OneDragonFlowConfigStore.SaveAutoBossCompletedRunCount(flowConfig.Name, flowConfig.AutoBossCompletedRunCount);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "保存首领讨伐累计领奖次数失败");
        }
    }

    /// <summary>
    /// 执行一条龙结束后的操作
    /// </summary>
    /// <param name="completionAction">配置中的完成后操作</param>
    private static void RunCompletionAction(string completionAction)
    {
        switch (completionAction)
        {
            case "关闭游戏":
                SystemControl.CloseGame();
                break;
            case "关闭软件":
                Application.Current.Dispatcher.Invoke(() => { Application.Current.Shutdown(); });
                break;
            case "关闭游戏和软件":
                SystemControl.CloseGame();
                Application.Current.Dispatcher.Invoke(() => { Application.Current.Shutdown(); });
                break;
            case "关机":
                SystemControl.CloseGame();
                SystemControl.Shutdown();
                break;
        }
    }
}
