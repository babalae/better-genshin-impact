using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 任务库列表中的一项已注册能力或本地资源。
/// </summary>
public sealed class PuloniaTaskLibraryItemViewModel
{
    /// <summary>
    /// 该项所属的能力定义。
    /// </summary>
    public PuloniaTaskDefinition Definition { get; }

    /// <summary>
    /// 可选的本地资源描述；纯能力任务为空。
    /// </summary>
    public PuloniaTaskResourceDescriptor? Resource { get; }

    /// <summary>
    /// 任务库分类。
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// 从资源相对路径提取的一级目录范围，便于按采集目标或资源集合快速收窄列表。
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// 列表主标题。
    /// </summary>
    public string DisplayName => Resource?.DisplayName ?? Definition.DisplayName;

    /// <summary>
    /// 列表副标题。
    /// </summary>
    public string SecondaryText => Resource is null
        ? Definition.Description
        : Resource.IsDirectory && Definition.TaskType is "pathing" or "keymouse"
            ? $"{Resource.RelativePath} · {Resource.ChildResourceCount} 项资源"
            : Resource.RelativePath;

    /// <summary>
    /// 列表类型标签。
    /// </summary>
    public string TypeLabel => Resource?.IsDirectory == true && Definition.TaskType is "pathing" or "keymouse"
        ? "目录"
        : Definition.DisplayName;

    /// <summary>
    /// 搜索使用的组合文本。
    /// </summary>
    public string SearchText => DisplayName + "\n" + SecondaryText + "\n" + Definition.TaskType + "\n" + Scope;

    /// <summary>
    /// 建立一项任务库数据。
    /// </summary>
    public PuloniaTaskLibraryItemViewModel(PuloniaTaskDefinition definition,
        PuloniaTaskResourceDescriptor? resource, string category)
    {
        Definition = definition;
        Resource = resource;
        Category = category;
        Scope = GetScope(definition, resource);
    }

    /// <summary>
    /// 把本地资源归入稳定的一级目录范围；纯能力和根目录使用明确标签。
    /// </summary>
    private static string GetScope(PuloniaTaskDefinition definition, PuloniaTaskResourceDescriptor? resource)
    {
        if (resource is null)
            return "能力";
        if (definition.TaskType == "javascript")
            return "JS 项目";
        if (resource.IsRootDirectory || resource.RelativePath == ".")
            return "资源根目录";
        var normalized = resource.RelativePath.Replace('\\', '/');
        var separator = normalized.IndexOf('/');
        return separator > 0 ? normalized[..separator] : resource.IsDirectory ? normalized : "根目录";
    }
}
