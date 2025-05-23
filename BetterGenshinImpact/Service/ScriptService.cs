using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;

using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;

namespace BetterGenshinImpact.Service;

public partial class ScriptService : IScriptService
{
    private readonly ILogger<ScriptService> _logger = App.GetLogger<ScriptService>();
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();
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
    public async Task RunMulti(IEnumerable<ScriptGroupProject> projectList, string? groupName = null)
    {
        groupName ??= "默认";

        var list = ReloadScriptProjects(projectList);

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

                foreach (var project in list)
                {
                    if (ShouldSkipTask(project))
                    {
                        continue;
                    }
                    //月卡检测
                    await _blessingOfTheWelkinMoonTask.Start(CancellationContext.Instance.Cts.Token);
                    if (project.Status != "Enabled")
                    {
                        _logger.LogInformation("脚本 {Name} 状态为禁用，跳过执行", project.Name);
                        continue;
                    }

                    if (CancellationContext.Instance.Cts.IsCancellationRequested)
                    {
                        _logger.LogInformation("执行被取消");
                        break;
                    }

                    
                    if (fisrt)
                    {
                        fisrt = false;
                        Notify.Event(NotificationEvent.GroupStart).Success($"配置组{groupName}启动");
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

                        await Task.Delay(2000);
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
        TaskContext.Instance().CurrentScriptProject = project;
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
                            TaskControl.Logger.LogInformation("如果你已经在游戏内的其他界面，请自行退出当前界面（ESC），使当前任务能够继续运行！");
                        }

                        await Task.Delay(500);
                    }
                });
            }
        }
    }
}
