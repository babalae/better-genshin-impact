using CsTrees;
using CsTrees.Composites;
using CsTrees.FluentBuilder;
using System.Collections.Generic;
using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo
{
    /// <summary>
    /// 经典复合节点目录
    /// 工厂方法会被 CsTrees.MEAI 源生成器转换为 LLM 的工具调用方法，
    /// [Description] 就是 LLM 可见的组合节点语义说明
    /// </summary>
    internal class ClassicCompositesCatalog : IBehaviourCatalog
    {
        [Description("打开一个 Sequence（序列）作用域。序列按顺序执行子节点，全部成功才成功，任一失败则失败。用于'先做A再做B'的流程。")]
        public Sequence Sequence([Description("节点名称")] string name, [Description("启用 memory 时，上一次 tick 处于 RUNNING 的子节点将作为起始点，跳过前面的子节点。")] bool memory, IEnumerable<Behaviour> children) => new Sequence(name, memory, children);

        [Description("打开一个 Selector（选择）作用域。选择器按顺序尝试子节点，第一个成功就成功，全部失败才失败。用于'尝试A，失败则尝试B'的备选逻辑。")]
        public Selector Selector([Description("节点名称")] string name, [Description("启用 memory 时，上一次 tick 处于 RUNNING 的子节点将作为起始点，跳过前面的子节点。")] bool memory, IEnumerable<Behaviour> children) => new Selector(name, memory, children);

        [Description("打开一个 ParallelAll（并行）作用域。并行同时执行所有子节点，全部成功才成功。")]
        public Parallel ParallelAll([Description("节点名称")] string name, IEnumerable<Behaviour> children) => new Parallel(name, new ParallelPolicy.SuccessOnAll(), children);

        [Description("打开一个 ParallelOne（并行）作用域。并行同时执行所有子节点，任意一个成功就算成功。")]
        public Parallel ParallelOne([Description("节点名称")] string name, IEnumerable<Behaviour> children) => new Parallel(name, new ParallelPolicy.SuccessOnOne(), children);
    }
}
