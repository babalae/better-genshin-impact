using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Exceptions;
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
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();
    private readonly CancellationToken _ct = CancellationContext.Instance.Cts.Token;
    private readonly RunnerContext _runnerContext = RunnerContext.Instance;
    private readonly TaskContext _taskContext = TaskContext.Instance();

    private static bool IsCurrentHourEqual(string input)
    {
        // 尝试将输入字符串转换为整数
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

        // 如果输入非数字或不合法，返回 false
        return false;
    }
    public bool ShouldSkipTask(ScriptGroupProject project)
    {

        if (project.GroupInfo is { Config.PathingConfig.Enabled: true } )
        {
            if (IsCurrentHourEqual(project.GroupInfo.Config.PathingConfig.SkipDuring))
            {
                _logger.LogInformation($"{project.Name}任务已到禁止执行时段，将跳过！");
                return true;
            }

            var tcc = project.GroupInfo.Config.PathingConfig.TaskCycleConfig;
            if (tcc.Enable)
            {
                int index = tcc.GetExecutionOrder(DateTime.Now);
                if (index == -1)
                {
                    _logger.LogInformation($"{project.Name}周期配置参数错误，配置将不生效，任务正常执行！");
                }
                else if (index != tcc.Index)
                {
                    _logger.LogInformation($"{project.Name}任务已经不在执行周期（当前值${index}!=配置值${tcc.Index}），将跳过此任务！");
                    return true;
                }
               
            }
            
        }
        return false; // 不跳过
    }
    public async Task RunMulti(IEnumerable<ScriptGroupProject> projectList, string? groupName = null,TaskProgress? taskProgress = null)
    {
        groupName ??= "默认";

        var list = ReloadScriptProjects(projectList);
        
        //恢复临时的跳过标志
        foreach (var scriptGroupProject in projectList)
        {
            scriptGroupProject.SkipFlag = false;
        }
        

        // // 针对JS 脚本，检查是否包含定时器操作
        // var jsProjects = ExtractJsProjects(list);
        // if (!hasTimer && jsProjects.Count > 0)
        // {
        //     var codeList = await ReadCodeList(jsProjects);
        //     hasTimer = HasTimerOperation(codeList);
        // }

        // 没启动时候，启动截图器
        await StartGameTask();

        if (!string.IsNullOrEmpty(groupName))
        {
            // if (hasTimer)
            // {
            //     _logger.LogInformation("配置组 {Name} 包含实时任务操作调用", groupName);
            // }

            _logger.LogInformation("配置组 {Name} 加载完成，共{Cnt}个脚本，开始执行", groupName, list.Count);
        }

        // var timerOperation = hasTimer ? DispatcherTimerOperationEnum.UseCacheImageWithTriggerEmpty : DispatcherTimerOperationEnum.UseSelfCaptureImage;

        
        bool fisrt = true;
        await new TaskRunner()
            .RunThreadAsync(async () =>
            {
                var stopwatch = new Stopwatch();
                int projectIndex = -1;
                foreach (var project in list)
                {
                    projectIndex++;
                    // 在配置组开始执行前进行队伍切换
                    await SwitchPartyBeforeGroup(project, groupName);
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
                    //月卡检测
                    await _blessingOfTheWelkinMoonTask.Start(_ct);
                    if (project.Status != "Enabled")
                    {
                        _logger.LogInformation("脚本 {Name} 状态为禁用，跳过执行", project.Name);
                        continue;
                    }

                    if (_ct.IsCancellationRequested)
                    {
                        _logger.LogInformation("执行被取消");
                        break;
                    }

                    if (fisrt)
                    {
                        fisrt = false;
                        Notify.Event(NotificationEvent.GroupStart).Success($"配置组{groupName}启动");
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
                            var ra = TaskControl.CaptureToRectArea();

                            // 做一次全队阵亡判断
                            if (Bv.ClickIfInReviveModal(ra))
                            {
                                await Bv.WaitForMainUi(_ct); // 等待主界面加载完成
                                _logger.LogInformation("复苏完成");
                                await Task.Delay(4000, _ct);
                                // 血量肯定不满，直接去七天神像回血
                                await TpStatueOfTheSeven();
                            }
                            await ExecuteProject(project);

                            //多次执行时及时中断
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
                            _logger.LogInformation("取消执行配置组: {Msg}", e.Message);
                            throw;
                        }
                        catch (Exception e)
                        {
                            _logger.LogDebug(e, "执行脚本时发生异常");
                            _logger.LogError("执行脚本时发生异常: {Msg}", e.Message);
                            if (taskProgress!=null && taskProgress.CurrentScriptGroupProjectInfo!=null )
                            {
                                taskProgress.CurrentScriptGroupProjectInfo.Status = 2;
                            }
                        }
                        finally
                        {
                            stopwatch.Stop();
                            var elapsedTime = TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds);
                            // _logger.LogDebug("→ 脚本执行结束: {Name}, 耗时: {ElapsedMilliseconds} 毫秒", project.Name, stopwatch.ElapsedMilliseconds);
                            _logger.LogInformation("→ 脚本执行结束: {Name}, 耗时: {Minutes}分{Seconds:0.000}秒", project.Name,
                                elapsedTime.Hours * 60 + elapsedTime.Minutes, elapsedTime.TotalSeconds % 60);
                            _logger.LogInformation("------------------------------");
                        }
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
                            //累计连续失败次数
                            if (taskProgress.CurrentScriptGroupProjectInfo.Status == 2)
                            {
                                taskProgress.ConsecutiveFailureCount++;
                            }

                            taskProgress?.History?.Add(taskProgress.CurrentScriptGroupProjectInfo);
                            TaskProgressManager.SaveTaskProgress(taskProgress);
                        }

                        //异常达到一次次数，重启bgi
                        var autoconfig = TaskContext.Instance().Config.OtherConfig.AutoRestartConfig;
                        if (autoconfig.Enabled && taskProgress.ConsecutiveFailureCount >= autoconfig.FailureCount)
                        {
                            _logger.LogInformation("调度器任务出现未预期的异常，自动重启bgi");
                            Notify.Event(NotificationEvent.GroupEnd).Error("调度器任务出现未预期的异常，自动重启bgi");
                            if (autoconfig.RestartGameTogether 
                                && TaskContext.Instance().Config.GenshinStartConfig.LinkedStartEnabled 
                                && TaskContext.Instance().Config.GenshinStartConfig.AutoEnterGameEnabled)
                            {
                                SystemControl.CloseGame();
                                await Task.Delay(2000, _ct);
                            }

                            SystemControl.RestartApplication(["--TaskProgress",taskProgress.Name]);
                        }

                    }
                }
            });

        // 还原定时器
        TaskTriggerDispatcher.Instance().SetTriggers(GameTaskManager.LoadInitialTriggers());
        
        if (!string.IsNullOrEmpty(groupName))
        {
            _logger.LogInformation("配置组 {Name} 执行结束", groupName);
        }

        if (!fisrt)
        {
            Notify.Event(NotificationEvent.GroupEnd).Success($"配置组{groupName}结束");
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
                // hasTimer = true;
            }
            else if (project.Type == "Shell")
            {
                var newProject = ScriptGroupProject.BuildShellProject(project.Name);
                CopyProjectProperties(project, newProject);
                list.Add(newProject);
                // hasTimer = true;
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

    // private List<ScriptProject> ExtractJsProjects(List<ScriptGroupProject> list)
    // {
    //     var jsProjects = new List<ScriptProject>();
    //     foreach (var project in list)
    //     {
    //         if (project is { Type: "Javascript", Project: not null })
    //         {
    //             jsProjects.Add(project.Project);
    //         }
    //     }
    //
    //     return jsProjects;
    // }

    private async Task ExecuteProject(ScriptGroupProject project)
    {
        _taskContext.CurrentScriptProject = project;
        if (project.Type == "Javascript")
        {
            if (project.Project == null)
            {
                throw new Exception("Project 为空");
            }

            _logger.LogInformation("→ 开始执行JS脚本: {Name}", project.Name);
            await project.Run();
        }
        else if (project.Type == "KeyMouse")
        {
            _logger.LogInformation("→ 开始执行键鼠脚本: {Name}", project.Name);
            await project.Run();
        }
        else if (project.Type == "Pathing")
        {
            _logger.LogInformation("→ 开始执行地图追踪任务: {Name}", project.Name);
            await project.Run();
        }
        else if (project.Type == "Shell")
        {
            _logger.LogInformation("→ 开始执行shell: {Name}", project.Name);
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

    /// <summary>
    /// 在配置组开始执行前进行队伍切换
    /// </summary>
    /// <param name="project">项目</param>
    /// <param name="groupName">配置组名称</param>
    private async Task SwitchPartyBeforeGroup(ScriptGroupProject project, string groupName)
    {

        if (project.GroupInfo?.Config.PathingConfig == null || 
            string.IsNullOrEmpty(project.GroupInfo.Config.PathingConfig.PartyName))
        {
            return;
        }

        var partyName = project.GroupInfo.Config.PathingConfig.PartyName;
        var partyConfig = project.GroupInfo.Config.PathingConfig;
        
        _logger.LogInformation("配置组 {GroupName} 任务：开始切换队伍到 {PartyName}", groupName, partyName);
        
            
        // 执行完整的队伍切换逻辑
        var success = await SwitchPartyBefore(partyConfig, partyName);
            
        if (!success)
        {
            _logger.LogError("配置组 {GroupName} 队伍切换失败，结束配置组执行", groupName);
            throw new Exception($"配置组 {groupName} 队伍切换到 {partyName} 失败");
        }
            
        _logger.LogInformation("配置组 {GroupName} 队伍切换成功，继续执行配置组", groupName);
    }

    /// <summary>
    /// 配置组队伍切换前的完整逻辑
    /// </summary>
    /// <param name="partyConfig">队伍配置</param>
    /// <param name="partyName">队伍名称</param>
    /// <returns></returns>
    private async Task<bool> SwitchPartyBefore(Core.Config.PathingPartyConfig partyConfig, string partyName)
    {
        var ra = TaskControl.CaptureToRectArea();
        var pRaList = ra.FindMulti(AutoFightAssets.Instance.PRa); // 判断是否联机
        if (pRaList.Count > 0)
        {
            _logger.LogInformation("处于联机状态下，不切换队伍");
        }
        else
        {
            if (!await SwitchParty(partyName, partyConfig))
            {
                _logger.LogError("切换队伍失败，无法执行配置组！请检查配置组中的地图追踪配置！");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 执行队伍切换操作
    /// </summary>
    /// <param name="partyName">队伍名称</param>
    /// <param name="partyConfig">队伍配置</param>
    /// <returns></returns>
    private async Task<bool> SwitchParty(string? partyName, Core.Config.PathingPartyConfig partyConfig)
    {
        bool success = true;
        if (!string.IsNullOrEmpty(partyName))
        {
            if (_runnerContext.PartyName == partyName)
            {
                return success;
            }

            bool forceTp = partyConfig.IsVisitStatueBeforeSwitchParty;

            if (forceTp) // 强制传送模式
            {
                await new TpTask(_ct).TpToStatueOfTheSeven(); 
                success = await new SwitchPartyTask().Start(partyName, _ct);
            }
            else // 优先原地切换模式
            {
                try
                {
                    success = await new SwitchPartyTask().Start(partyName, _ct);
                }
                catch (PartySetupFailedException)
                {
                    await new TpTask(_ct).TpToStatueOfTheSeven();
                    success = await new SwitchPartyTask().Start(partyName, _ct);
                }
            }

            if (success)
            {
                _runnerContext.PartyName = partyName;
                _runnerContext.ClearCombatScenes();
            }
        }

        return success;
    }

    /// <summary>
    /// 传送到七天神像回血
    /// </summary>
    private async Task TpStatueOfTheSeven()
    {
        // tp 到七天神像回血
        var tpTask = new TpTask(_ct);
        await _runnerContext.StopAutoPickRunTask(async () => await tpTask.TpToStatueOfTheSeven(), 5);
        _logger.LogInformation("血量恢复完成");
    }

    public static async Task StartGameTask(bool waitForMainUi = true)
    {
        // 没启动时候，启动截图器
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
                            TaskControl.Logger.LogInformation("当前不在游戏主界面，等待进入主界面后执行任务...");
                            TaskControl.Logger.LogInformation("如果你已经在游戏内的其他界面，请自行退出当前界面（ESC），或是30秒后将程序将自动尝试到入主界面，使当前任务能够继续运行！");
                        }

                        await Task.Delay(500);
                        if (sw.Elapsed.TotalSeconds >= 30)
                        {
                            //防止自启动游戏后因为一些原因失焦，导致一直卡住
                            if (!SystemControl.IsGenshinImpactActiveByProcess())
                            {
                                loseFocusCount++;
                                if (loseFocusCount>50 && loseFocusCount<100)
                                {
                                    SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
                                }
                                SystemControl.ActivateWindow();
                            }

                            //自启动游戏，如果鼠标在游戏外面，将无法自动开门，这里尝试移动到游戏界面
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
