namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 新增任务时选中路线或录制目录的添加方式。
/// </summary>
public enum PuloniaTaskDirectoryImportMode
{
    /// <summary>保存固定目录版本的目录引用，资源清单在运行准备时展开。</summary>
    Reference,

    /// <summary>把当前文件清单展开为任务树，并按子目录建立分组。</summary>
    Structured,

    /// <summary>把当前文件清单展开到同一个分组，不保留子目录层级。</summary>
    Flat
}
