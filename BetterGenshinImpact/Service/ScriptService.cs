using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.FarmingPlan;
using BetterGenshinImpact.GameTask.TaskProgress;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

public partial class ScriptService : IScriptService
{
    private readonly ILogger<ScriptService> _logger = App.GetLogger<ScriptService>();
    private readonly ILocalizationService? _localizationService = App.GetService<ILocalizationService>();
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();
    
    private static bool IsCurrentHourEqual(string input)
    {
        // 尝试将输入字符串转换为数字
        if (int.TryParse(input, out int hour))
        {
            // 验证小时是否在合法范围内（0-23）
            if (hour is >= 0 and <= 23)
            {
                // 获取当前小时数
                int currentHour = DateTime.Now.Hour;
                // 判断是否相等
                return currentHour == hour;
            }
        }

        // 如果输入不是数字或不合法，返回 false
        return false;
    }
    
    public bool ShouldSkipTask(ScriptGroupProject project)
    {
        if (project.GroupInfo is { Config.PathingConfig.Enabled: true } )
        {
            if (IsCurrentHourEqual(project.GroupInfo.Config.PathingConfig.SkipDuring))
            {
                var message = _localizationService?.GetString("script.taskReachedForbiddenTime", project.Name) ?? $"{project.Name}已经到达禁止执行时段，跳过任务";
                _logger.LogInformation(message);
                return true;
            }

            var tcc = project.GroupInfo.Config.PathingConfig.TaskCycleConfig;
            if (tcc.Enable)
            {
                int index = tcc.GetExecutionOrder(DateTime.Now);
                if (index == -1)
                {
                    var message = _localizationService?.GetString("script.taskSettingIncorrectOrCompleted", project.Name) ?? $"{project.Name}任务设置不正确，或今日任务已执行，跳过任务";
                    _logger.LogInformation(message);
                }
                else if (index != tcc.Index)
                {
                    var message = _localizationService?.GetString("script.taskExceededCycle", project.Name, index, tcc.Index) ?? $"{project.Name}任务已经超过执行周期，当前值{index}!=配置值{tcc.Index}，跳过任务，等待明天";
                    _logger.LogInformation(message);
                    return true;
                }
            }
        }

        if (TaskContext.Instance().Config.OtherConfig.FarmingPlanConfig.Enabled)
        {
            try
            {
                var task = PathingTask.BuildFromFilePath(Path.Combine(MapPathingViewModel.PathJsonPath, project.FolderName, project.Name));
                string message;
                if (FarmingStatsRecorder.IsDailyFarmingLimitReached(task.FarmingInfo,out message))
                {
                    _logger.LogInformation($"{project.Name}:{message},跳过任务");
                    return true;
                }
            }
            catch (Exception e)
            {
                var errorMessage = _localizationService?.GetString("script.farmingPlanStatisticsError", e.Message) ?? $"锄地规划统计异常：{e.Message}";
                TaskControl.Logger.LogError(errorMessage);
            }
        }
        
        return false;
    }  
  public async Task RunMulti(IEnumerable<ScriptGroupProject> projectList, string? groupName = null,TaskProgress? taskProgress = null)
    {
        groupName ??= _localizationService?.GetString("script.defaultGroupName") ?? "默认";

        var list = ReloadScriptProjects(projectList);
        
        // Reset skip time related flags
        foreach (var scriptGroupProject in projectList)
        {
            scriptGroupProject.SkipFlag = false;
        }

        // No timer operations, start image capture
        await StartGameTask();

        if (!string.IsNullOrEmpty(groupName))
        {
            var message = _localizationService?.GetString("script.groupLoadedAndStarting", groupName, list.Count) ?? $"Script group {groupName} loaded successfully, {list.Count} scripts total, starting execution";
            _logger.LogInformation(message);
        }

        bool first = true;
        await new TaskRunner()
            .RunThreadAsync(async () =>
            {
                var stopwatch = new Stopwatch();
                int projectIndex = -1;
                foreach (var project in list)
                {
                    projectIndex++;
                    if (taskProgress != null && taskProgress.Next != null)
                    {
                        if (taskProgress.Next.Index>projectIndex)
                        {
                            continue;
                        }
                        taskProgress.Next = null;
                    }

                    if (project is {SkipFlag:true})
                    {
                        continue;
                    }
                    if (ShouldSkipTask(project))
                    {
                        continue;
                    }
                    // Monthly card task
                    await _blessingOfTheWelkinMoonTask.Start(CancellationContext.Instance.Cts.Token);
                    if (project.Status != "Enabled")
                    {
                        var message = _localizationService?.GetString("script.scriptDisabledSkipping", project.Name) ?? $"Script {project.Name} is disabled, skipping execution";
                        _logger.LogInformation(message);
                        continue;
                    }

                    if (CancellationContext.Instance.Cts.IsCancellationRequested)
                    {
                        var message = _localizationService?.GetString("script.executionCancelled") ?? "Execution cancelled";
                        _logger.LogInformation(message);
                        break;
                    }

                    if (first)
                    {
                        first = false;
                        Notify.Event(NotificationEvent.GroupStart).Success("notification.message.configGroupStartNamed", groupName);
                    }

                    if (taskProgress!=null)
                    {
                        taskProgress.CurrentScriptGroupProjectInfo = new TaskProgress.ScriptGroupProjectInfo
                        {
                            Name = project.Name,
                            FolderName = project.FolderName
                            ,Index = projectIndex
                            ,GroupName = taskProgress?.CurrentScriptGroupName ?? ""
                        };
                        TaskProgressManager.SaveTaskProgress(taskProgress);
                    }
                    for (var i = 0; i < project.RunNum; i++)
                    {
                        try
                        {
                            TaskTriggerDispatcher.Instance().ClearTriggers();

                            _logger.LogInformation("------------------------------");

                            stopwatch.Reset();
                            stopwatch.Start();
                            
                            await ExecuteProject(project);

                            // Check again during execution time
                            if (ShouldSkipTask(project))
                            {
                                continue;
                            }
                        }
                        catch (NormalEndException e)
                        {
                            throw;
                        }
                        catch (TaskCanceledException e)
                        {
                            var message = _localizationService?.GetString("script.cancellingTask", e.Message) ?? $"Cancelling task execution: {e.Message}";
                            _logger.LogInformation(message);
                            throw;
                        }
                        catch (Exception e)
                        {
                            var errorMessage = _localizationService?.GetString("script.scriptExecutionError") ?? "An exception occurred while executing script";
                            _logger.LogDebug(e, errorMessage);
                            _logger.LogError($"{errorMessage}: {{Msg}}", e.Message);
                            if (taskProgress!=null && taskProgress.CurrentScriptGroupProjectInfo!=null )
                            {
                                taskProgress.CurrentScriptGroupProjectInfo.Status = 2;
                            }
                        }
                        finally
                        {
                            stopwatch.Stop();
                            var elapsedTime = TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds);
                            var message = _localizationService?.GetString("script.scriptExecutionCompleted", project.Name, elapsedTime.Hours * 60 + elapsedTime.Minutes, elapsedTime.TotalSeconds % 60) ?? 
                                $"√ Script execution completed: {project.Name}, elapsed time: {elapsedTime.Hours * 60 + elapsedTime.Minutes}min {elapsedTime.TotalSeconds % 60:0.000}sec";
                            _logger.LogInformation(message);
                            _logger.LogInformation("------------------------------");
                        }

                        await Task.Delay(2000);
                    }

                    if (taskProgress != null)
                    {
                        if (taskProgress.CurrentScriptGroupProjectInfo!=null )
                        {
                            taskProgress.CurrentScriptGroupProjectInfo.TaskEnd = true;
                            taskProgress.CurrentScriptGroupProjectInfo.EndTime = DateTime.Now;
                            if (taskProgress.CurrentScriptGroupProjectInfo.Status == 1)
                            {
                                taskProgress.ConsecutiveFailureCount = 0;
                                taskProgress.LastSuccessScriptGroupProjectInfo =
                                    taskProgress.CurrentScriptGroupProjectInfo;
                                taskProgress.LastScriptGroupName =taskProgress.CurrentScriptGroupName;
                            }
                            // Accumulate consecutive failure count
                            if (taskProgress.CurrentScriptGroupProjectInfo.Status == 2)
                            {
                                taskProgress.ConsecutiveFailureCount++;
                            }

                            taskProgress?.History?.Add(taskProgress.CurrentScriptGroupProjectInfo);
                            TaskProgressManager.SaveTaskProgress(taskProgress);
                        }

                        // When exceptions reach a certain number, restart BGI
                        var autoconfig = TaskContext.Instance().Config.OtherConfig.AutoRestartConfig;
                        if (autoconfig.Enabled && taskProgress.ConsecutiveFailureCount >= autoconfig.FailureCount)
                        {
                            var message = _localizationService?.GetString("script.consecutiveUnexpectedErrors") ?? "Multiple consecutive unexpected errors occurred, automatically restarting BGI";
                            _logger.LogInformation(message);
                            Notify.Event(NotificationEvent.GroupEnd).Error("notification.error.unexpectedError");
                            if (autoconfig.RestartGameTogether 
                                && TaskContext.Instance().Config.GenshinStartConfig.LinkedStartEnabled 
                                && TaskContext.Instance().Config.GenshinStartConfig.AutoEnterGameEnabled)
                            {
                                SystemControl.CloseGame();
                                Thread.Sleep(2000);
                            }

                            SystemControl.RestartApplication(["--TaskProgress",taskProgress.Name]);
                        }
                    }
                }
            });

        // Restore timers
        TaskTriggerDispatcher.Instance().SetTriggers(GameTaskManager.LoadInitialTriggers());
        
        if (!string.IsNullOrEmpty(groupName))
        {
            var message = _localizationService?.GetString("script.groupExecutionCompleted", groupName) ?? $"Script group {groupName} execution completed";
            _logger.LogInformation(message);
        }

        if (!first)
        {
            Notify.Event(NotificationEvent.GroupEnd).Success("notification.message.configGroupEndNamed", groupName);
        }

        if (taskProgress != null)
        {
            taskProgress.Next = null;
        }
    }

    private List<ScriptGroupProject> ReloadScriptProjects(IEnumerable<ScriptGroupProject> projectList)
    {
        var list = new List<ScriptGroupProject>();
        foreach (var project in projectList)
        {
            if (project.Type == "Javascript")
            {
                var newProject = new ScriptGroupProject(new ScriptProject(project.FolderName));
                CopyProjectProperties(project, newProject);
                list.Add(newProject);
            }
            else if (project.Type == "KeyMouse")
            {
                var newProject = ScriptGroupProject.BuildKeyMouseProject(project.Name);
                CopyProjectProperties(project, newProject);
                list.Add(newProject);
            }
            else if (project.Type == "Pathing")
            {
                var newProject = ScriptGroupProject.BuildPathingProject(project.Name, project.FolderName);
                CopyProjectProperties(project, newProject);
                list.Add(newProject);
            }
            else if (project.Type == "Shell")
            {
                var newProject = ScriptGroupProject.BuildShellProject(project.Name);
                CopyProjectProperties(project, newProject);
                list.Add(newProject);
            }
        }

        return list;
    }

    private void CopyProjectProperties(ScriptGroupProject source, ScriptGroupProject target)
    {
        target.Status = source.Status;
        target.Schedule = source.Schedule;
        target.RunNum = source.RunNum;
        target.JsScriptSettingsObject = source.JsScriptSettingsObject;
        target.GroupInfo = source.GroupInfo;
        target.AllowJsNotification = source.AllowJsNotification;
        target.SkipFlag = source.SkipFlag;
    }

    private async Task ExecuteProject(ScriptGroupProject project)
    {
        TaskContext.Instance().CurrentScriptProject = project;
        if (project.Type == "Javascript")
        {
            if (project.Project == null)
            {
                var errorMessage = _localizationService?.GetString("script.projectIsNull") ?? "Project is null";
                throw new Exception(errorMessage);
            }

            var message = _localizationService?.GetString("script.startingJsScript", project.Name) ?? $"√ Starting JS script: {project.Name}";
            _logger.LogInformation(message);
            await project.Run();
        }
        else if (project.Type == "KeyMouse")
        {
            var message = _localizationService?.GetString("script.startingKeyMouseScript", project.Name) ?? $"√ Starting key-mouse script: {project.Name}";
            _logger.LogInformation(message);
            await project.Run();
        }
        else if (project.Type == "Pathing")
        {
            var message = _localizationService?.GetString("script.startingPathingTask", project.Name) ?? $"√ Starting pathing task: {project.Name}";
            _logger.LogInformation(message);
            await project.Run();
        }
        else if (project.Type == "Shell")
        {
            var message = _localizationService?.GetString("script.startingShellScript", project.Name) ?? $"√ Starting shell script: {project.Name}";
            _logger.LogInformation(message);
            await project.Run();
        }
    }

    private async Task<List<string>> ReadCodeList(List<ScriptProject> list)
    {
        var codeList = new List<string>();
        foreach (var project in list)
        {
            var code = await project.LoadCode();
            codeList.Add(code);
        }

        return codeList;
    }

    private bool HasTimerOperation(IEnumerable<string> codeList)
    {
        return codeList.Any(code => DispatcherAddTimerRegex().IsMatch(code));
    }

    [GeneratedRegex(@"^(?!\s*\/\/)\s*dispatcher\.\s*addTimer", RegexOptions.Multiline)]
    private static partial Regex DispatcherAddTimerRegex();

    public static async Task StartGameTask(bool waitForMainUi = true)
    {
        // 没有定时器操作，启动图像
        var homePageViewModel = App.GetService<HomePageViewModel>();
        if (!homePageViewModel!.TaskDispatcherEnabled)
        {
            await homePageViewModel.OnStartTriggerAsync();

            if (waitForMainUi)
            {
                await Task.Run(async () =>
                {
                    await Task.Delay(200);
                    var first = true;
                    var sw = Stopwatch.StartNew();
                    var loseFocusCount = 0;
                    while (true)
                    {
                        if (!homePageViewModel.TaskDispatcherEnabled || !TaskContext.Instance().IsInitialized)
                        {
                            continue;
                        }

                        var content = TaskControl.CaptureToRectArea();
                        if (Bv.IsInMainUi(content) || Bv.IsInAnyClosableUi(content))
                        {
                            return;
                        }

                        if (first)
                        {
                            first = false;
                            var localizationService = App.GetService<ILocalizationService>();
                            var message1 = localizationService?.GetString("script.notInMainUiWaiting") ?? "Not currently in the game main UI, waiting to enter main UI before executing tasks...";
                            var message2 = localizationService?.GetString("script.mainUiInstructions") ?? "If you are already in the game's main UI, please press ESC to exit the current interface or wait 30 seconds to attempt to automatically click an empty area, allowing foreground tasks to execute normally!";
                            TaskControl.Logger.LogInformation(message1);
                            TaskControl.Logger.LogInformation(message2);
                        }

                        await Task.Delay(500);
                        if (sw.Elapsed.TotalSeconds >= 30)
                        {
                            //防止长期不在游戏内，为一些原因失去焦点，一直卡住
                            if (!SystemControl.IsGenshinImpactActiveByProcess())
                            {
                                loseFocusCount++;
                                if (loseFocusCount>50 && loseFocusCount<100)
                                {
                                    SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
                                }
                                SystemControl.ActivateWindow();
                            }

                            //如果长期在游戏内，但不在游戏主界面，无法自动点击，这里尝试移动鼠标到游戏内
                            if (sw.Elapsed.TotalSeconds < 200)
                            {
                                GlobalMethod.MoveMouseTo(300, 300);
                            }
                        }
                    }
                });
            }
        }
    }
}