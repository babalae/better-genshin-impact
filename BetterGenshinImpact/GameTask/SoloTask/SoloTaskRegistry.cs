using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;
using BetterGenshinImpact.GameTask.AutoCook;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Factory;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 将可传输的独立任务请求还原为目标实例中的具体任务对象。
/// </summary>
public sealed class SoloTaskRegistry(GameScreenshotService screenshots, IConfigService configService)
{
    /// <summary>
    /// 根据请求创建只绑定当前运行环境的独立任务。
    /// </summary>
    /// <param name="request">已完成校验的启动请求。</param>
    /// <param name="runtime">目标实例本轮游戏运行环境。</param>
    public ISoloTask CreateTask(SoloTaskStartRequest request, GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(runtime);

        // 所有实例共用同一份配置文件，任务开始构造时直接读取磁盘上的最新配置。
        var config = configService.Read();
        return request.TaskType switch
        {
            SoloTaskType.AutoGeniusInvokation => new AutoGeniusInvokationTask(
                runtime,
                config,
                new GeniusInvokationTaskParam(GetRequired<string>(request.Parameters, "strategyContent"))),
            SoloTaskType.AutoWood => new AutoWoodTask(
                runtime,
                config,
                new WoodTaskParam(
                    GetRequired<int>(request.Parameters, "roundNum"),
                    GetRequired<int>(request.Parameters, "dailyMaxCount"))),
            SoloTaskType.AutoFight => CreateAutoFightTask(request, runtime, config),
            SoloTaskType.AutoDomain => new AutoDomainTask(
                runtime,
                config,
                new AutoDomainParam(
                    GetRequired<int>(request.Parameters, "roundNum"),
                    GetRequired<string>(request.Parameters, "strategyPath"))),
            SoloTaskType.AutoBoss => CreateAutoBossTask(request, runtime, config),
            SoloTaskType.AutoStygianOnslaught => CreateAutoStygianOnslaughtTask(request, runtime, config),
            SoloTaskType.AutoFishing => CreateAutoFishingTask(request, runtime, config),
            SoloTaskType.AutoLeyLineOutcrop => CreateAutoLeyLineOutcropTask(runtime, config),
            SoloTaskType.AutoMusicGame => new AutoMusicGameTask(runtime, config, new AutoMusicGameParam()),
            SoloTaskType.AutoAlbum => new AutoAlbumTask(runtime, config, new AutoMusicGameParam()),
            SoloTaskType.AutoCook => new AutoCookTask(runtime, config),
            SoloTaskType.AutoCombo => new AutoComboBuildTask(runtime, config),
            SoloTaskType.AutoArtifactSalvage => CreateArtifactSalvageTask(runtime, config),
            SoloTaskType.AutoRedeemCode => new UseRedemptionCodeTask(
                runtime,
                GetRequired<List<string>>(request.Parameters, "codes")),
            SoloTaskType.GetGridIcons => new GetGridIconsTask(
                runtime,
                config.GetGridIconsConfig.GridName,
                config.GetGridIconsConfig.StarAsSuffix,
                config.GetGridIconsConfig.MaxNumToGet),
            SoloTaskType.GridIconsAccuracyTest => new GridIconsAccuracyTestTask(
                runtime,
                config.GetGridIconsConfig.GridName,
                config.GetGridIconsConfig.MaxNumToGet),
            SoloTaskType.InventoryCountComparison => new InventoryCountComparisonTask(
                runtime,
                GetRequired<InventoryCountComparisonTarget>(request.Parameters, "target")),
            _ => throw new ArgumentOutOfRangeException(nameof(request.TaskType), request.TaskType, "未知的独立任务类型")
        };
    }

    /// <summary>
    /// 返回任务是否需要等待游戏进入主界面后再开始业务逻辑。
    /// </summary>
    public static bool ShouldWaitForMainUi(SoloTaskType taskType)
    {
        return taskType is not (SoloTaskType.AutoGeniusInvokation
            or SoloTaskType.AutoStygianOnslaught
            or SoloTaskType.AutoMusicGame
            or SoloTaskType.AutoAlbum);
    }

    /// <summary>
    /// 返回用于实例管理窗口展示的任务名称。
    /// </summary>
    public static string GetDisplayName(SoloTaskType taskType)
    {
        return taskType switch
        {
            SoloTaskType.AutoGeniusInvokation => "自动七圣召唤",
            SoloTaskType.AutoWood => "自动伐木",
            SoloTaskType.AutoFight => "自动战斗",
            SoloTaskType.AutoDomain => "自动秘境",
            SoloTaskType.AutoBoss => "自动首领讨伐",
            SoloTaskType.AutoStygianOnslaught => "自动幽境危战",
            SoloTaskType.AutoFishing => "自动钓鱼",
            SoloTaskType.AutoLeyLineOutcrop => "自动地脉花",
            SoloTaskType.AutoMusicGame => "自动音游",
            SoloTaskType.AutoAlbum => "自动演奏专辑",
            SoloTaskType.AutoCook => "自动烹饪",
            SoloTaskType.AutoCombo => "自动连招构建",
            SoloTaskType.AutoArtifactSalvage => "自动分解圣遗物",
            SoloTaskType.AutoRedeemCode => "自动使用兑换码",
            SoloTaskType.GetGridIcons => "截取物品图标",
            SoloTaskType.GridIconsAccuracyTest => "模型准确率测试",
            SoloTaskType.InventoryCountComparison => "数量 OCR 对比",
            _ => taskType.ToString()
        };
    }

    /// <summary>
    /// 创建自动战斗任务并保留点击处选择的策略路径。
    /// </summary>
    private static ISoloTask CreateAutoFightTask(
        SoloTaskStartRequest request,
        GameRuntime runtime,
        AllConfig config)
    {
        var path = GetRequired<string>(request.Parameters, "strategyPath");
        var param = new AutoFightParam(path, config.AutoFightConfig);
        return CombatTaskFactoryProvider.GetFactory(path).CreateTask(runtime, config, param);
    }

    /// <summary>
    /// 创建自动首领讨伐任务。
    /// </summary>
    private static ISoloTask CreateAutoBossTask(
        SoloTaskStartRequest request,
        GameRuntime runtime,
        AllConfig config)
    {
        var param = new AutoBossParam(GetRequired<string>(request.Parameters, "strategyPath"));
        param.SetAutoBossConfig(config.AutoBossConfig);
        return new AutoBossTask(runtime, config, param);
    }

    /// <summary>
    /// 创建自动幽境危战任务。
    /// </summary>
    private static ISoloTask CreateAutoStygianOnslaughtTask(
        SoloTaskStartRequest request,
        GameRuntime runtime,
        AllConfig config)
    {
        var param = new AutoStygianOnslaughtParam();
        param.SetAutoStygianOnslaughtConfig(config.AutoStygianOnslaughtConfig);
        return new AutoStygianOnslaughtTask(
            runtime,
            config,
            param,
            GetRequired<string>(request.Parameters, "strategyPath"));
    }

    /// <summary>
    /// 创建自动钓鱼任务，并按点击时的截图开关选择截图服务。
    /// </summary>
    private ISoloTask CreateAutoFishingTask(
        SoloTaskStartRequest request,
        GameRuntime runtime,
        AllConfig config)
    {
        var saveScreenshot = GetRequired<bool>(request.Parameters, "saveScreenshotOnKeyTick");
        var param = AutoFishingTaskParam.BuildFromConfig(config.AutoFishingConfig, saveScreenshot);
        return new AutoFishingTask(runtime, config, param, saveScreenshot ? screenshots : null);
    }

    /// <summary>
    /// 创建自动地脉花任务。
    /// </summary>
    private static ISoloTask CreateAutoLeyLineOutcropTask(GameRuntime runtime, AllConfig config)
    {
        var param = new AutoLeyLineOutcropParam();
        param.SetAutoLeyLineOutcropConfig(config.AutoLeyLineOutcropConfig);
        return new AutoLeyLineOutcropTask(runtime, config, param);
    }

    /// <summary>
    /// 创建自动分解圣遗物任务。
    /// </summary>
    private static ISoloTask CreateArtifactSalvageTask(GameRuntime runtime, AllConfig config)
    {
        var taskConfig = config.AutoArtifactSalvageConfig;
        var param = new AutoArtifactSalvageTaskParam(
            int.Parse(taskConfig.MaxArtifactStar),
            taskConfig.JavaScript,
            taskConfig.ArtifactSetFilter,
            taskConfig.MaxNumToCheck,
            taskConfig.RecognitionFailurePolicy);
        return new AutoArtifactSalvageTask(runtime, param);
    }

    /// <summary>
    /// 从任务专用 JSON 载荷读取必需参数。
    /// </summary>
    private static T GetRequired<T>(JObject? parameters, string propertyName)
    {
        var token = parameters?[propertyName]
                    ?? throw new ArgumentException($"独立任务参数缺少 {propertyName}。");
        return token.ToObject<T>()
               ?? throw new ArgumentException($"独立任务参数 {propertyName} 无效。");
    }
}
