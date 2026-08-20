using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Mcp;

[McpServerToolType]
public sealed class McpWorkflowTools(
    McpApplicationServices application,
    McpCommandCatalog commandCatalog,
    McpDetachedTaskRegistry detachedTaskRegistry)
{
    [McpServerTool(Name = "bgi_start_capture", OpenWorld = true), Description("启动游戏窗口识别与截图器；等价于主页“启动”命令。")]
    public async Task<object> StartCapture(CancellationToken cancellationToken = default)
    {
        var viewModel = application.Services.GetRequiredService<HomePageViewModel>();
        await Application.Current.Dispatcher.InvokeAsync(viewModel.OnStartTriggerAsync).Task.Unwrap()
            .WaitAsync(cancellationToken);
        return new { started = TaskContext.Instance().IsInitialized };
    }

    [McpServerTool(Name = "bgi_stop_capture", Destructive = true, Idempotent = true),
     Description("停止截图器和实时触发器，并请求取消当前独立任务。")]
    public Task<McpCommandInvocationResult> StopCapture(
        [Description("必须明确设为 true。")] bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("停止截图器需要将 confirm 设为 true。");
        }

        return commandCatalog.InvokeAsync("home_page.stop_trigger", null, true, cancellationToken);
    }

    [McpServerTool(Name = "bgi_list_script_groups", ReadOnly = true, Idempotent = true),
     Description("列出 User/ScriptGroup 下可执行的配置组及项目概要。")]
    public static IReadOnlyList<object> ListScriptGroups()
    {
        var folder = Global.Absolute(@"User\ScriptGroup");
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.GetFiles(folder, "*.json")
            .Select(file =>
            {
                try
                {
                    var group = ScriptGroup.FromJson(File.ReadAllText(file));
                    return (object)new
                    {
                        group.Name,
                        group.Index,
                        file = Path.GetFileName(file),
                        projects = group.Projects.Select(x => new { x.Name, x.Type, x.FolderName, x.Index }).ToArray(),
                    };
                }
                catch (Exception ex)
                {
                    return new { file = Path.GetFileName(file), error = ex.Message };
                }
            })
            .ToArray();
    }

    [McpServerTool(Name = "bgi_run_script_groups", OpenWorld = true),
     Description("按名称顺序启动一个或多个 BetterGI 配置组；确认取得独立任务锁后立即返回，后台继续执行，不占用 Agent 回合。")]
    public async Task<object> RunScriptGroups(
        [Description("配置组名称，名称必须与 bgi_list_script_groups 返回值一致。")]
        string[] names,
        CancellationToken cancellationToken = default)
    {
        if (names.Length == 0 || names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("至少提供一个非空配置组名称。", nameof(names));
        }

        var viewModel = application.Services.GetRequiredService<ScriptControlViewModel>();
        return await detachedTaskRegistry.LaunchAsync(
            $"配置组：{string.Join(",", names)}",
            () => Application.Current.Dispatcher.InvokeAsync(() =>
                viewModel.OnStartMultiScriptGroupWithNamesAsync(names)).Task.Unwrap(),
            waitForCompletion: false,
            startupTimeoutSeconds: 180,
            cancellationToken);
    }

    [McpServerTool(Name = "bgi_list_one_dragon_configs", ReadOnly = true, Idempotent = true),
     Description("列出 User/OneDragon 下的一条龙配置及启用任务。")]
    public static IReadOnlyList<object> ListOneDragonConfigs()
    {
        var folder = OneDragonFlowViewModel.OneDragonFlowConfigFolder;
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.GetFiles(folder, "*.json")
            .Select(file =>
            {
                try
                {
                    var config = JsonConvert.DeserializeObject<OneDragonFlowConfig>(File.ReadAllText(file));
                    return (object)new
                    {
                        name = config?.Name ?? Path.GetFileNameWithoutExtension(file),
                        file = Path.GetFileName(file),
                        enabledTasks = config?.TaskEnabledList.Where(x => x.Value).Select(x => x.Key).ToArray() ?? [],
                        config?.CompletionAction,
                    };
                }
                catch (Exception ex)
                {
                    return new { file = Path.GetFileName(file), error = ex.Message };
                }
            })
            .ToArray();
    }

    [McpServerTool(Name = "bgi_run_one_dragon", OpenWorld = true), Description("选择并执行一条龙配置。")]
    public async Task<object> RunOneDragon(
        [Description("一条龙配置名称；省略时使用当前选中的配置。")] string? configName = null,
        CancellationToken cancellationToken = default)
    {
        var viewModel = application.Services.GetRequiredService<OneDragonFlowViewModel>();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (viewModel.ConfigList.Count == 0)
            {
                viewModel.OnNavigatedTo();
            }

            if (!string.IsNullOrWhiteSpace(configName))
            {
                var selected = viewModel.ConfigList.FirstOrDefault(x =>
                    x.Name.Equals(configName, StringComparison.OrdinalIgnoreCase));
                if (selected is null)
                {
                    throw new ArgumentException($"未找到一条龙配置：{configName}", nameof(configName));
                }

                viewModel.SelectedConfig = selected;
                viewModel.SetSomeSelectedConfig(selected);
            }

            if (viewModel.SelectedConfig is null)
            {
                throw new InvalidOperationException("当前没有可执行的一条龙配置。");
            }
        }).Task;
        return await detachedTaskRegistry.LaunchAsync(
            $"一条龙：{viewModel.SelectedConfig?.Name}",
            () => Application.Current.Dispatcher.InvokeAsync(viewModel.OnOneKeyExecute).Task.Unwrap(),
            waitForCompletion: false,
            startupTimeoutSeconds: 180,
            cancellationToken);
    }

    [McpServerTool(Name = "bgi_redeem_codes", OpenWorld = true), Description("在游戏内依次使用原神兑换码。")]
    public async Task<object> RedeemCodes(
        [Description("12 位大写字母/数字兑换码列表。")] string[] codes,
        CancellationToken cancellationToken = default)
    {
        var normalized = codes
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var invalid = normalized
            .Where(x => x.Length != 12 || x.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsDigit(c))).ToArray();
        if (invalid.Length > 0)
        {
            throw new ArgumentException($"兑换码格式无效：{string.Join(", ", invalid)}", nameof(codes));
        }

        if (normalized.Length == 0)
        {
            throw new ArgumentException("至少提供一个兑换码。", nameof(codes));
        }

        return await detachedTaskRegistry.LaunchAsync(
            $"兑换码：{normalized.Length} 条",
            () => new TaskRunner().RunSoloTaskAsync(new UseRedemptionCodeTask(normalized.ToList())),
            waitForCompletion: false,
            startupTimeoutSeconds: 180,
            cancellationToken);
    }
}