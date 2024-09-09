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
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service;

public partial class ScriptService(HomePageViewModel homePageViewModel) : IScriptService
{
    private readonly ILogger<ScriptService> _logger = App.GetLogger<ScriptService>();

    public async Task RunMultiJs(List<string> folderNameList, string? groupName = null)
    {
        // 重新加载脚本项目
        var projects = folderNameList.Select(name => new ScriptProject(name)).ToList();

        var codeList = await ReadCodeList(projects);
        var hasTimer = HasTimerOperation(codeList);

        // 没启动时候，启动截图器
        await homePageViewModel.OnStartTriggerAsync();

        if (!string.IsNullOrEmpty(groupName))
        {
            if (hasTimer)
            {
                _logger.LogInformation("配置组 {Name} 包含实时任务操作调用", groupName);
            }

            _logger.LogInformation("配置组 {Name} 加载完成，共{Cnt}个脚本，开始执行", groupName, projects.Count);
        }

        // 循环执行所有脚本
        var timerOperation = hasTimer ? DispatcherTimerOperationEnum.UseCacheImageWithTriggerEmpty : DispatcherTimerOperationEnum.UseSelfCaptureImage;
        await new TaskRunner(timerOperation)
            .RunThreadAsync(async () =>
            {
                foreach (var project in projects)
                {
                    try
                    {
                        if (hasTimer)
                        {
                            TaskTriggerDispatcher.Instance().ClearTriggers();
                        }

                        _logger.LogInformation("------------------------------");
                        _logger.LogInformation("→ 开始执行脚本: {Name}", project.Manifest.Name);
                        await project.ExecuteAsync();
                        await Task.Delay(1000);
                    }
                    catch (Exception e)
                    {
                        _logger.LogDebug(e, "执行脚本时发生异常");
                        _logger.LogError("执行脚本时发生异常: {Msg}", e.Message);
                    }
                    finally
                    {
                        _logger.LogInformation("→ 脚本执行结束: {Name}", project.Manifest.Name);
                        _logger.LogInformation("------------------------------");
                    }
                }
            });

        if (!string.IsNullOrEmpty(groupName))
        {
            _logger.LogInformation("配置组 {Name} 执行结束", groupName);
        }
    }

    public async Task RunMulti(IEnumerable<ScriptGroupProject> projectList, string? groupName = null)
    {
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
        await homePageViewModel.OnStartTriggerAsync();

        if (hasTimer)
        {
            _logger.LogInformation("配置组 {Name} 包含实时任务操作调用", groupName ?? "默认");
        }
        _logger.LogInformation("配置组 {Name} 加载完成，共{Cnt}个脚本，开始执行", groupName ?? "默认", list.Count);

        var timerOperation = hasTimer ? DispatcherTimerOperationEnum.UseCacheImageWithTriggerEmpty : DispatcherTimerOperationEnum.UseSelfCaptureImage;

        await new TaskRunner(timerOperation)
           .RunThreadAsync(async () =>
           {
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

                           await ExecuteProject(project);

                           await Task.Delay(2000);
                       }
                       catch (Exception e)
                       {
                           _logger.LogDebug(e, "执行脚本时发生异常");
                           _logger.LogError("执行脚本时发生异常: {Msg}", e.Message);
                       }
                       finally
                       {
                           _logger.LogInformation("→ 脚本执行结束: {Name}", project.Name);
                           _logger.LogInformation("------------------------------");
                       }
                   }
               }
           });

        _logger.LogInformation("配置组 {Name} 执行结束", groupName);
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
                var newProject = ScriptGroupProject.BuildKeyMouseProject(project.FolderName);
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
}
