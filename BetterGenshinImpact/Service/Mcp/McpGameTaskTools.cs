using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

/// <summary>
/// 任务设置页中独立游戏任务的显式 function calling 声明。
/// </summary>
[McpServerToolType]
public sealed class McpGameTaskTools(
    McpApplicationServices application,
    McpCommandCatalog commandCatalog,
    McpDetachedTaskRegistry detachedTaskRegistry)
{
    private static readonly IReadOnlyDictionary<string, GameTaskDefinition> Definitions =
        new Dictionary<string, GameTaskDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["genius_invokation"] = new("七圣召唤", "autoGeniusInvokationConfig", "使用已选牌组策略自动进行七圣召唤；要求策略文件存在。",
                "bgi_run_genius_invokation", null),
            ["wood"] = new("自动伐木", "autoWoodConfig", "按轮次自动砍伐木材，并受每日最大数量限制。", "bgi_run_auto_wood",
                "rounds、dailyMaxCount 是本次运行参数，不在 AllConfig 中。"),
            ["fight"] = new("自动战斗", "autoFightConfig", "使用当前战斗策略持续执行自动战斗。", "bgi_run_auto_fight", null),
            ["domain"] = new("自动秘境", "autoDomainConfig", "进入配置的秘境、自动战斗并按树脂设置领奖。战斗策略来自 autoFightConfig。",
                "bgi_run_auto_domain", "rounds 是本次运行参数；0 表示持续到资源或流程结束。"),
            ["boss"] = new("自动首领讨伐", "autoBossConfig", "按首领、次数、队伍和战斗策略设置执行 Boss 讨伐。", "bgi_run_auto_boss", null),
            ["stygian_onslaught"] = new("自动幽境危战", "autoStygianOnslaughtConfig", "按当前难度、队伍和策略执行幽境危战。",
                "bgi_run_stygian_onslaught", null),
            ["music_game"] = new("自动音游", "autoMusicGameConfig", "自动完成当前千音雅集/音游谱面。", "bgi_run_auto_music_game", null),
            ["album"] = new("自动专辑", "autoMusicGameConfig", "自动处理音游专辑流程。", "bgi_run_auto_album", null),
            ["cook"] = new("自动烹饪", "autoCookConfig", "在当前烹饪界面按配置连续自动烹饪。", "bgi_run_auto_cook", null),
            ["fishing"] = new("自动钓鱼", "autoFishingConfig", "按鱼饵、时间策略、甩杆和识别设置执行钓鱼。", "bgi_run_auto_fishing",
                "saveScreenshotOnKeyTick 控制本次运行按键调试截图。"),
            ["ley_line"] = new("自动地脉花", "autoLeyLineOutcropConfig", "按国家、花类型、次数、树脂和战斗设置执行地脉花。",
                "bgi_run_ley_line_outcrop", null),
            ["artifact_salvage"] = new("自动分解圣遗物", "autoArtifactSalvageConfig", "按星级、套装过滤、识别失败策略和 JS 规则分解圣遗物。",
                "bgi_run_artifact_salvage", null),
            ["grid_icons"] = new("采集背包网格图标", "getGridIconsConfig", "开发/数据工具：从指定背包网格批量截图并保存图标。",
                "bgi_collect_grid_icons", null),
            ["grid_accuracy"] = new("网格图标模型准确率测试", "getGridIconsConfig", "开发/测试工具：对当前网格分类运行模型准确率测试。",
                "bgi_test_grid_icon_accuracy", null),
        };

    [McpServerTool(Name = "bgi_list_game_tasks", ReadOnly = true, Idempotent = true),
     Description("列出可直接运行的 BetterGI 独立游戏任务。返回 taskId、中文用途、设置路径、专用运行工具名和额外运行参数。AI 应先读取设置，再决定是否修改和执行。")]
    public static IReadOnlyList<object> ListGameTasks() => Definitions
        .Select(x => (object)new
        {
            taskId = x.Key,
            x.Value.DisplayName,
            x.Value.Description,
            settingsPath = x.Value.ConfigPath,
            x.Value.RunTool,
            x.Value.RuntimeInputs,
        })
        .ToArray();

    [McpServerTool(Name = "bgi_get_game_task_settings", ReadOnly = true, Idempotent = true),
     Description("读取一个独立游戏任务实际使用的完整配置对象，并返回修改路径。先调用 bgi_list_game_tasks 获取 taskId；修改单项请使用 bgi_set_setting。")]
    public object GetGameTaskSettings(
        [Description("bgi_list_game_tasks 返回的 taskId，例如 domain、fishing、artifact_salvage。")]
        string taskId)
    {
        var definition = GetDefinition(taskId);
        var config = application.Services.GetRequiredService<IConfigService>().Get();
        var property = config.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                           .FirstOrDefault(
                               x => x.Name.Equals(definition.ConfigPath, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException($"配置属性不存在：{definition.ConfigPath}");
        return new
        {
            taskId,
            definition.DisplayName,
            definition.Description,
            settingsPath = definition.ConfigPath,
            definition.RuntimeInputs,
            settings = JsonSerializer.SerializeToElement(property.GetValue(config), property.PropertyType,
                ConfigService.JsonOptions),
            modifyExample = new
            {
                tool = "bgi_set_setting",
                path = $"{definition.ConfigPath}.<bgi_describe_settings 返回的子属性>",
                value = "<符合目标类型的 JSON 值>",
                confirm = true,
            },
        };
    }

    [McpServerTool(Name = "bgi_run_genius_invokation", OpenWorld = true),
     Description("运行自动七圣召唤。使用 autoGeniusInvokationConfig.strategyName 指定的本地策略文件；不存在时任务不会启动。")]
    public Task<McpDetachedTaskLaunchResult> RunGeniusInvokation(CancellationToken cancellationToken = default) =>
        Run("switch_auto_genius_invokation", cancellationToken);

    [McpServerTool(Name = "bgi_run_auto_wood", OpenWorld = true),
     Description("运行自动伐木。rounds=0 表示最多 9999 轮；dailyMaxCount=0 或 >=9999 表示不使用较小的本次上限。")]
    public async Task<McpDetachedTaskLaunchResult> RunAutoWood(
        [Description("本次伐木轮数，0-9999。")] int rounds = 1,
        [Description("本次每日最大木材数量，0-9999。")] int dailyMaxCount = 2000,
        CancellationToken cancellationToken = default)
    {
        if (rounds is < 0 or > 9999) throw new ArgumentOutOfRangeException(nameof(rounds));
        if (dailyMaxCount is < 0 or > 9999) throw new ArgumentOutOfRangeException(nameof(dailyMaxCount));
        var viewModel = application.Services.GetRequiredService<TaskSettingsPageViewModel>();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            viewModel.AutoWoodRoundNum = rounds;
            viewModel.AutoWoodDailyMaxCount = dailyMaxCount;
        }).Task;
        return await Run("switch_auto_wood", cancellationToken);
    }

    [McpServerTool(Name = "bgi_run_auto_fight", OpenWorld = true),
     Description("运行自动战斗，使用 autoFightConfig.strategyName 当前策略。策略为空或文件不存在时拒绝启动。")]
    public Task<McpDetachedTaskLaunchResult> RunAutoFight(CancellationToken cancellationToken = default) =>
        Run("switch_auto_fight", cancellationToken);

    [McpServerTool(Name = "bgi_run_auto_domain", OpenWorld = true),
     Description("运行自动秘境。使用 autoDomainConfig 的秘境/队伍/树脂设置和 autoFightConfig 的战斗策略；rounds=0 表示最多 9999 轮。")]
    public async Task<McpDetachedTaskLaunchResult> RunAutoDomain(
        [Description("本次秘境轮数，0-9999。")] int rounds = 1,
        CancellationToken cancellationToken = default)
    {
        if (rounds is < 0 or > 9999) throw new ArgumentOutOfRangeException(nameof(rounds));
        var viewModel = application.Services.GetRequiredService<TaskSettingsPageViewModel>();
        await Application.Current.Dispatcher.InvokeAsync(() => viewModel.AutoDomainRoundNum = rounds).Task;
        return await Run("switch_auto_domain", cancellationToken);
    }

    [McpServerTool(Name = "bgi_run_auto_boss", OpenWorld = true),
     Description("运行自动首领讨伐，使用 autoBossConfig 中的 Boss 名称、次数、队伍、领奖和 strategyName。")]
    public Task<McpDetachedTaskLaunchResult> RunAutoBoss(CancellationToken cancellationToken = default) =>
        Run("switch_auto_boss", cancellationToken);

    [McpServerTool(Name = "bgi_run_stygian_onslaught", OpenWorld = true),
     Description("运行自动幽境危战，使用 autoStygianOnslaughtConfig 中的难度、队伍、轮数和 strategyName。")]
    public Task<McpDetachedTaskLaunchResult> RunStygianOnslaught(CancellationToken cancellationToken = default) =>
        Run("switch_auto_stygian_onslaught", cancellationToken);

    [McpServerTool(Name = "bgi_run_auto_music_game", OpenWorld = true),
     Description("运行自动音游，读取 autoMusicGameConfig，并要求游戏当前位于受支持的音游流程。")]
    public Task<McpDetachedTaskLaunchResult> RunAutoMusicGame(CancellationToken cancellationToken = default) =>
        Run("switch_auto_music_game", cancellationToken);

    [McpServerTool(Name = "bgi_run_auto_album", OpenWorld = true), Description("运行自动音游专辑流程，读取 autoMusicGameConfig。")]
    public Task<McpDetachedTaskLaunchResult> RunAutoAlbum(CancellationToken cancellationToken = default) =>
        Run("switch_auto_album", cancellationToken);

    [McpServerTool(Name = "bgi_run_auto_cook", OpenWorld = true),
     Description("运行自动烹饪，读取 autoCookConfig；游戏应处于可识别的烹饪界面。")]
    public Task<McpDetachedTaskLaunchResult> RunAutoCook(CancellationToken cancellationToken = default) =>
        Run("switch_auto_cook", cancellationToken);

    [McpServerTool(Name = "bgi_run_auto_fishing", OpenWorld = true),
     Description("运行自动钓鱼，读取 autoFishingConfig 的鱼饵、时间策略和识别设置。可临时启用按键调试截图。")]
    public async Task<McpDetachedTaskLaunchResult> RunAutoFishing(
        [Description("本次运行是否在关键按键时保存调试截图；还要求 commonConfig.screenshotEnabled=true。")]
        bool saveScreenshotOnKeyTick = false,
        CancellationToken cancellationToken = default)
    {
        var viewModel = application.Services.GetRequiredService<TaskSettingsPageViewModel>();
        await Application.Current.Dispatcher
            .InvokeAsync(() => viewModel.SaveScreenshotOnKeyTick = saveScreenshotOnKeyTick).Task;
        return await Run("switch_auto_fishing", cancellationToken);
    }

    [McpServerTool(Name = "bgi_run_ley_line_outcrop", OpenWorld = true),
     Description("运行自动地脉花，读取 autoLeyLineOutcropConfig 的国家、启示/藏金花类型、次数、树脂、队伍和掉落扫描设置。")]
    public Task<McpDetachedTaskLaunchResult> RunLeyLineOutcrop(CancellationToken cancellationToken = default) =>
        Run("switch_auto_ley_line_outcrop", cancellationToken);

    [McpServerTool(Name = "bgi_run_artifact_salvage", Destructive = true, OpenWorld = true),
     Description("运行自动圣遗物分解。该任务会在游戏内永久分解物品，使用 autoArtifactSalvageConfig 的最大星级、套装过滤和 JS 规则。")]
    public Task<McpDetachedTaskLaunchResult> RunArtifactSalvage(
        [Description("必须明确设为 true，确认游戏内物品会被永久分解。")]
        bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("圣遗物分解需要将 confirm 设为 true。");
        return Run("switch_artifact_salvage", cancellationToken);
    }

    [McpServerTool(Name = "bgi_collect_grid_icons", OpenWorld = true),
     Description("开发/数据采集工具：按 getGridIconsConfig 从当前背包网格批量采集图标到 log/gridIcons。会操作游戏翻页。")]
    public Task<McpDetachedTaskLaunchResult> CollectGridIcons(CancellationToken cancellationToken = default) =>
        Run("switch_get_grid_icons", cancellationToken);

    [McpServerTool(Name = "bgi_test_grid_icon_accuracy", ReadOnly = true, OpenWorld = true),
     Description("开发/测试工具：按 getGridIconsConfig 对当前网格运行模型准确率测试；读取游戏画面并写测试日志，不修改背包物品。")]
    public Task<McpDetachedTaskLaunchResult> TestGridIconAccuracy(CancellationToken cancellationToken = default) =>
        Run("switch_grid_icons_model_accuracy_test", cancellationToken);

    private Task<McpDetachedTaskLaunchResult> Run(string commandName, CancellationToken cancellationToken) =>
        detachedTaskRegistry.LaunchAsync(
            commandName,
            async () => _ = await commandCatalog.InvokeAsync(
                $"task_settings_page.{commandName}", null, false, CancellationToken.None),
            waitForCompletion: false,
            startupTimeoutSeconds: 180,
            cancellationToken);

    private static GameTaskDefinition GetDefinition(string taskId) =>
        Definitions.TryGetValue(taskId, out var definition)
            ? definition
            : throw new ArgumentException($"未知 taskId：{taskId}。请先调用 bgi_list_game_tasks。", nameof(taskId));

    private sealed record GameTaskDefinition(
        string DisplayName,
        string ConfigPath,
        string Description,
        string RunTool,
        string? RuntimeInputs);
}