namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 另一个实例的运行状态文件 <c>instances/&lt;实例目录名&gt;/state.json</c> 及其修改标记，供执行记录跨实例只读显示。
/// </summary>
/// <param name="DirectoryName">实例目录名</param>
/// <param name="Path">state.json 的完整路径</param>
/// <param name="Stamp">修改标记：文件长度与最后写入时间；文件被替换后会变化</param>
public sealed record PuloniaInstanceStateFile(string DirectoryName, string Path, string Stamp);
