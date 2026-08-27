using System.Reflection;
using System.Runtime.CompilerServices;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFight.Model;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class RunnerContextTests
{
    /// <summary>
    /// 验证重置上下文时只退役仍可能被运行任务借用的战斗场景。
    /// </summary>
    [Fact]
    public void Reset_RetiresCombatScenesWithoutDisposingCache()
    {
        var context = new RunnerContext();
        var scenes = (CombatScenes)RuntimeHelpers.GetUninitializedObject(typeof(CombatScenes));
        var currentScenesField = GetPrivateField("_combatScenes");
        var retiredScenesField = GetPrivateField("_retiredCombatScenes");
        currentScenesField.SetValue(context, scenes);

        context.Reset();

        Assert.Null(currentScenesField.GetValue(context));
        var retiredScenes = Assert.IsType<List<CombatScenes>>(retiredScenesField.GetValue(context));
        Assert.Contains(scenes, retiredScenes);
    }

    /// <summary>
    /// 获取 <see cref="RunnerContext"/> 的指定私有实例字段。
    /// </summary>
    /// <param name="name">字段名称。</param>
    /// <returns>匹配的字段元数据。</returns>
    private static FieldInfo GetPrivateField(string name)
    {
        return typeof(RunnerContext).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"RunnerContext field not found: {name}");
    }
}
