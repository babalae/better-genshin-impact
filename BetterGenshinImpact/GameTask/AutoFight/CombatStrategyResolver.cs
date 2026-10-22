using System;
using System.IO;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 把战斗策略名称或路径解析为任务可用的策略路径。
/// 参数里既可以保存策略名（任务计划，便于跨机器），也可以保存路径（JS、旧入口）。
/// </summary>
public static class CombatStrategyResolver
{
    /// <summary>
    /// 自动按队伍选择策略的固定名称。
    /// </summary>
    public const string AutoSelectName = "根据队伍自动选择";

    /// <summary>
    /// 解析策略名称或路径。
    /// </summary>
    /// <param name="nameOrPath">策略名称、策略文件路径或策略目录路径；为空时视为自动选择。</param>
    /// <param name="path">解析得到的路径；连招策略原样返回其固定名称。</param>
    /// <returns>策略存在时返回 true。</returns>
    public static bool TryResolve(string? nameOrPath, out string path)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath) || AutoSelectName.Equals(nameOrPath, StringComparison.Ordinal))
        {
            path = Global.Absolute(@"User\AutoFight\");
            return true;
        }

        if (AutoFightParam.ComboStrategyName.Equals(nameOrPath, StringComparison.Ordinal))
        {
            path = nameOrPath;
            return true;
        }

        // 已经是路径时直接检查存在性
        if (Path.IsPathRooted(nameOrPath))
        {
            path = nameOrPath;
            return File.Exists(path) || Directory.Exists(path);
        }

        (path, _) = AutoFightParam.ResolveStrategyPath(nameOrPath);
        return File.Exists(path);
    }

    /// <summary>
    /// 解析策略名称或路径，策略不存在时抛出异常。
    /// </summary>
    /// <param name="nameOrPath">策略名称或路径。</param>
    /// <returns>策略路径。</returns>
    /// <exception cref="FileNotFoundException">策略不存在。</exception>
    public static string Resolve(string? nameOrPath)
        => TryResolve(nameOrPath, out var path) ? path : throw new FileNotFoundException("战斗策略不存在。", path);
}
