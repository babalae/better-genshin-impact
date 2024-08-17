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

    public async Task RunMulti(List<string> folderNameList, string? groupName = null)
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
            .RunAsync(async () =>
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
