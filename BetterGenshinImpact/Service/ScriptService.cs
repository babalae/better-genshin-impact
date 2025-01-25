using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model.Enum;
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

namespace BetterGenshinImpact.Service;

public partial class ScriptService : IScriptService
{
    private readonly ILogger<ScriptService> _logger = App.GetLogger<ScriptService>();

    public async Task RunMulti(IEnumerable<ScriptGroupProject> projectList, string? groupName = null)
    {
        groupName ??= "默认";

        var hasTimer = false;
        var list = ReloadScriptProjects(projectList, ref hasTimer);

        // 针对JS 脚本，检查是否包含定时器操作
        var jsProjects = ExtractJsProjects(list);
        if (!hasTimer && jsProjects.Count > 0)
        {
            var codeList = await ReadCodeList(jsProjects);
            hasTimer = HasTimerOperation(codeList);
        }

        // 没启动时候，启动截图器
        await StartGameTask();

        if (!string.IsNullOrEmpty(groupName))
        {
            if (hasTimer)
            {
                _logger.LogInformation("配置组 {Name} 包含实时任务操作调用", groupName);
            }

            _logger.LogInformation("配置组 {Name} 加载完成，共{Cnt}个脚本，开始执行", groupName, list.Count);
        }

        var timerOperation = hasTimer ? DispatcherTimerOperationEnum.UseCacheImageWithTriggerEmpty : DispatcherTimerOperationEnum.UseSelfCaptureImage;

        await new TaskRunner(timerOperation)
            .RunThreadAsync(async () =>
            {
                var stopwatch = new Stopwatch();

                foreach (var project in list)
                {
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

                    for (var i = 0; i < project.RunNum; i++)
                    {
                        try
                        {
                            if (hasTimer)
                            {
                                TaskTriggerDispatcher.Instance().ClearTriggers();
                            }

                            _logger.LogInformation("------------------------------");

                            stopwatch.Reset();
                            stopwatch.Start();
                            await ExecuteProject(project);
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
                            _logger.LogDebug("→ 脚本执行结束: {Name}, 耗时: {ElapsedMilliseconds} 毫秒", project.Name, stopwatch.ElapsedMilliseconds);
                            _logger.LogInformation("→ 脚本执行结束: {Name}, 耗时: {Minutes}分{Seconds:0.000}秒", project.Name,
                                elapsedTime.Hours * 60 + elapsedTime.Minutes, elapsedTime.TotalSeconds % 60);
                            _logger.LogInformation("------------------------------");
                        }

                        await Task.Delay(2000);
                    }
                }
            });

        if (!string.IsNullOrEmpty(groupName))
        {
            _logger.LogInformation("配置组 {Name} 执行结束", groupName);
        }
    }

    private List<ScriptGroupProject> ReloadScriptProjects(IEnumerable<ScriptGroupProject> projectList, ref bool hasTimer)
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
                hasTimer = true;
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
    }

    private List<ScriptProject> ExtractJsProjects(List<ScriptGroupProject> list)
    {
        var jsProjects = new List<ScriptProject>();
        foreach (var project in list)
        {
            if (project is { Type: "Javascript", Project: not null })
            {
                jsProjects.Add(project.Project);
            }
        }

        return jsProjects;
    }

    private async Task ExecuteProject(ScriptGroupProject project)
    {
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
            _logger.LogInformation("→ 开始执行路径追踪任务: {Name}", project.Name);
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
                await Task.Run(() =>
                {
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
                    }
                });
            }
        }
    }
}