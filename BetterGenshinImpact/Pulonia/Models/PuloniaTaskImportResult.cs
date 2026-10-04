using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>旧数据导入的独立预览，确认前不写入新存储。</summary>
public sealed class PuloniaTaskImportResult
{
    /// <summary>待导入的计划，所有 ID 均重新分配。</summary>
    public List<PuloniaTaskPlan> Plans { get; } = [];
    /// <summary>从旧功能配置提取的参数预设。</summary>
    public List<PuloniaTaskPreset> Presets { get; } = [];
    /// <summary>无法完全表示或需用户核对的差异，带源文件和节点位置。</summary>
    public List<string> Differences { get; } = [];
    /// <summary>读取过的源文件与内容指纹，不覆盖或删除原文件。</summary>
    public List<string> Sources { get; } = [];
    /// <summary>可复制、可保存的中文导入报告。</summary>
    public string Report => $"导入预览：{Plans.Count} 个计划，{Presets.Count} 个参数预设\n\n"
        + string.Join("\n", Plans.Select(p => $"计划：{p.Name}（{p.RootTask.Children.Count} 个顶层步骤）"))
        + "\n\n兼容差异：\n" + (Differences.Count == 0 ? "无已知参数转换差异。" : string.Join("\n", Differences.Select(d => "• " + d)))
        + "\n\n来源（原文件保留）：\n" + string.Join("\n", Sources)
        + "\n\n旧执行记录、CD、排队请求和断点不会导入新账本。缺失资源及无法映射的节点保留为禁用步骤，需修改后再启用。";
}
