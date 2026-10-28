using System;
using System.Diagnostics.CodeAnalysis;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 体力计划调度的一个子任务：是否参与本计划，以及传给该任务的参数。
/// 树脂使用等由体力计划决定的字段在运行时改写，这里保存的值只作为其余设置的来源。
/// </summary>
/// <typeparam name="TParam">子任务的参数类型。</typeparam>
public sealed class ResinPlanSubTask<TParam> where TParam : class, new()
{
    /// <summary>参数缺失时创建默认值；为 null 时使用参数类的无参构造。反序列化走无参构造，JSON 中的参数优先。</summary>
    private readonly Func<TParam>? _createDefault;

    /// <summary>子任务参数；首次读取时才创建默认值，避免反序列化时重复构造。</summary>
    private TParam? _param;

    /// <summary>使用参数类默认值的子任务；反序列化使用此构造。</summary>
    public ResinPlanSubTask()
    {
    }

    /// <summary>指定体力计划内的默认参数，例如首领默认只讨伐 1 次。</summary>
    /// <param name="createDefault">创建默认参数。</param>
    public ResinPlanSubTask(Func<TParam> createDefault) => _createDefault = createDefault;

    /// <summary>是否在体力计划中执行此任务。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>子任务参数；JSON 为 null 时回退为默认值。</summary>
    [AllowNull]
    public TParam Param
    {
        get => _param ??= _createDefault?.Invoke() ?? new TParam();
        set => _param = value;
    }
}
