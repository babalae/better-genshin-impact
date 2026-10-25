using System.Runtime.CompilerServices;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common.Resin;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinTests;

/// <summary>绕过依赖运行中应用的语言服务构造，只初始化参数测试需要的独立树脂状态。</summary>
internal static class ResinParameterTestFactory
{
    /// <summary>建立秘境测试参数，不读取全局配置、不启动应用。</summary>
    internal static AutoDomainParam Domain()
    {
        var param = (AutoDomainParam)RuntimeHelpers.GetUninitializedObject(typeof(AutoDomainParam));
        param.ResinUsage = ResinCapabilities.Domain.Defaults;
        param.ResinPriorityList = ["浓缩树脂", "原粹树脂"];
        return param;
    }

    /// <summary>建立幽境测试参数。</summary>
    internal static AutoStygianOnslaughtParam Stygian()
    {
        var param = (AutoStygianOnslaughtParam)RuntimeHelpers.GetUninitializedObject(typeof(AutoStygianOnslaughtParam));
        param.ResinUsage = ResinCapabilities.StygianOnslaught.Defaults;
        param.ResinPriorityList = ["浓缩树脂", "原粹树脂"];
        return param;
    }

    /// <summary>建立首领测试参数。</summary>
    internal static AutoBossParam Boss()
    {
        var param = (AutoBossParam)RuntimeHelpers.GetUninitializedObject(typeof(AutoBossParam));
        param.ResinUsage = ResinCapabilities.Boss.Defaults;
        return param;
    }

    /// <summary>建立地脉花测试参数。</summary>
    internal static AutoLeyLineOutcropParam LeyLine()
    {
        var param = (AutoLeyLineOutcropParam)RuntimeHelpers.GetUninitializedObject(typeof(AutoLeyLineOutcropParam));
        param.ResinUsage = ResinCapabilities.LeyLineOutcrop.Defaults;
        return param;
    }
}
