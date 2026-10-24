using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 新增任务表单收集到的输入，由 PuloniaTaskCreationService 校验并建立任务节点。
/// </summary>
public sealed class PuloniaTaskCreationRequest
{
    /// <summary>节点名称，调用方已去除首尾空白。</summary>
    public required string Name { get; init; }

    /// <summary>所选能力定义。</summary>
    public required PuloniaTaskDefinition Definition { get; init; }

    /// <summary>所选资源；Shell、进程内 C# 等无资源任务为空。</summary>
    public PuloniaTaskResourceDescriptor? Resource { get; init; }

    /// <summary>相对能力默认值发生变化的显式参数，直接作为节点或分组的参数保存。</summary>
    public JObject Overrides { get; init; } = new();

    /// <summary>选中路线或录制目录时的添加方式。</summary>
    public PuloniaTaskDirectoryImportMode DirectoryImportMode { get; init; } = PuloniaTaskDirectoryImportMode.Reference;

    /// <summary>批量添加目录时是否包含全部子目录。</summary>
    public bool IncludeSubdirectories { get; init; } = true;
}
