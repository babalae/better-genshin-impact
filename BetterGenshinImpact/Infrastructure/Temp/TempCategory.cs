using System.Collections.Generic;

namespace BetterGenshinImpact.Infrastructure.Temp;

/// <summary>
/// 临时文件分类，决定子目录名和在存储空间窗口中的名称
/// </summary>
/// <param name="Id">子目录名，只使用小写字母、数字和短横线</param>
/// <param name="DisplayName">展示名称</param>
public sealed record TempCategory(string Id, string DisplayName);

/// <summary>
/// 已定义的临时文件分类。新功能在这里新增一项并加入 <see cref="All"/>，即可按功能清理
/// </summary>
public static class TempCategories
{
    /// <summary>
    /// 脚本仓库 ZIP 导入解压、更换仓库地址时的重新克隆
    /// </summary>
    public static readonly TempCategory ScriptRepository = new("script-repo", "脚本仓库导入与更新");

    /// <summary>
    /// Windows 通知的配图
    /// </summary>
    public static readonly TempCategory Notification = new("notification", "通知图片");

    /// <summary>
    /// 全部分类，供存储区域登记
    /// </summary>
    public static IReadOnlyList<TempCategory> All { get; } = [ScriptRepository, Notification];
}
