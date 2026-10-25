using System;
using System.Windows;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>树脂界面读取参数快照和解析任务能力的公共规则，不改写输入 JSON。</summary>
public static class ResinUsageViewModelSupport
{
    /// <summary>缺失、null 或格式错误时使用独立默认副本；合法结构中的无效业务值保留供编辑器提示。</summary>
    public static ResinUsage Read(JToken? value, ResinUsageCapability capability)
    {
        if (value is not JObject) return capability.Defaults;
        try { return SoloTaskJson.Deserialize<ResinUsage>(value.ToString(Newtonsoft.Json.Formatting.None)); }
        catch (System.Text.Json.JsonException) { return capability.Defaults; }
    }

    /// <summary>根据独立任务标识或 Pulonia 任务类型返回任务名与同一份能力声明。</summary>
    public static (string Name, ResinUsageCapability Capability)? GetTask(string? taskType)
        => taskType switch
        {
            "auto_domain" or "solo.auto_domain" => ("自动秘境", AutoDomainTask.ResinCapability),
            "auto_stygian_onslaught" or "solo.auto_stygian_onslaught" => ("自动幽境危战", AutoStygianOnslaughtTask.ResinCapability),
            "auto_boss" or "solo.auto_boss" => ("自动首领讨伐", AutoBossTask.ResinCapability),
            "auto_ley_line_outcrop" or "solo.auto_ley_line_outcrop" => ("自动地脉花", AutoLeyLineOutcropTask.ResinCapability),
            _ => null
        };

    /// <summary>静态 Schema 字段工厂没有 DI，允许显式注入，否则仅在真实应用中获取服务；隔离容器和离屏测试不启动 App。</summary>
    public static IResinSettingsDialogs? ResolveDialogs(IResinSettingsDialogs? dialogs)
    {
        if (dialogs is not null || Application.Current is not App) return dialogs;
        try { return App.GetService<IResinSettingsDialogs>(); }
        catch (InvalidOperationException) { return null; }
    }
}
