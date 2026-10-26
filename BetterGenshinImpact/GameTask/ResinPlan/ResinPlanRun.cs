using System;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 一次体力计划运行的内存状态：结果数据、最近一次读取的树脂和已消耗的点数。只在本次运行中存在，不落盘。
/// </summary>
internal sealed class ResinPlanRun
{
    /// <summary>本次运行的结果数据，阶段记录按执行顺序追加。</summary>
    public ResinPlanData Data { get; } = new();

    /// <summary>最近一次读取到的树脂持有量；尚未读取时为 null。</summary>
    public ResinPlanStock? Stock { get; private set; }

    /// <summary>子任务执行后持有量已经变化，下一阶段开始前需要重新读取。</summary>
    public bool StockStale { get; private set; } = true;

    /// <summary>剩余预算；全部模式为 null。</summary>
    public int? Remaining => ResinPlanBudget.Remaining(Data.Budget.MaxResin, Data.Budget.SpentPoints);

    /// <summary>建立运行状态。</summary>
    /// <param name="maxResin">本次最多消耗的点数；0 表示全部。</param>
    public ResinPlanRun(int maxResin) => Data.Budget.MaxResin = Math.Max(0, maxResin);

    /// <summary>记录开始时的持有量。</summary>
    /// <param name="stock">开始时读取到的持有量。</param>
    public void Start(ResinPlanStock stock)
    {
        Data.Budget.StartOriginal = stock.Original;
        Data.Budget.StartCondensed = stock.Condensed;
        UpdateStock(stock);
    }

    /// <summary>保存最新读取到的持有量。</summary>
    /// <param name="stock">持有量。</param>
    public void UpdateStock(ResinPlanStock stock)
    {
        Stock = stock;
        StockStale = false;
    }

    /// <summary>子任务即将执行，之后的持有量需要重新读取。</summary>
    public void MarkStockStale() => StockStale = true;

    /// <summary>追加一个阶段记录。</summary>
    /// <param name="stage">阶段。</param>
    /// <param name="name">阶段中文名称。</param>
    /// <param name="taskId">调用的子任务标识。</param>
    /// <param name="target">本步对象。</param>
    /// <returns>新的阶段记录，默认结果为跳过。</returns>
    public ResinPlanStep AddStep(ResinPlanStage stage, string name, string taskId, string target)
    {
        var step = new ResinPlanStep { Stage = stage, Name = name, TaskId = taskId, Target = target };
        Data.Steps.Add(step);
        return step;
    }

    /// <summary>把已执行阶段的消耗与合成份数计入合计。</summary>
    /// <param name="step">已执行的阶段。</param>
    public void Complete(ResinPlanStep step)
    {
        Data.Budget.SpentPoints += ResinPlanBudget.Points(step.Resin);
        if (step.CraftedCount is { } crafted)
        {
            Data.Budget.CraftedCondensed += crafted;
        }
    }
}
