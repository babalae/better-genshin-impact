using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoFight.Strategy;

/// <summary>扫描策略文件名和时间，生成目录树；不读取内容或创建目录。</summary>
public static class CombatStrategyCatalog
{
    /// <summary>在线程池扫描允许的策略类型，自动匹配的范围只统计文本策略。</summary>
    public static Task<CombatStrategyCatalogSnapshot> LoadAsync(CombatStrategyKind allowedKinds, CancellationToken ct)
        => Task.Run(() =>
        {
            var roots = new Dictionary<CombatStrategyKind, CombatStrategyNode>();
            var scopes = new List<CombatStrategyNode>();
            foreach (var kind in new[] { CombatStrategyKind.Txt, CombatStrategyKind.Json, CombatStrategyKind.KeyMouse })
            {
                ct.ThrowIfCancellationRequested();
                if ((allowedKinds & kind) == 0 && !(kind == CombatStrategyKind.Txt && allowedKinds.HasFlag(CombatStrategyKind.AutoSelect))) continue;
                var path = Global.Absolute(kind == CombatStrategyKind.KeyMouse ? @"User\KeyMouseScript" : @"User\AutoFight");
                var root = ScanRoot(Path.GetFullPath(path), kind, ct);
                if ((allowedKinds & kind) != 0) roots.Add(kind, root);
                if (kind == CombatStrategyKind.Txt && allowedKinds.HasFlag(CombatStrategyKind.AutoSelect))
                    AddScopes(root, scopes);
            }
            return new CombatStrategyCatalogSnapshot(roots, scopes.AsReadOnly());
        }, ct);

    /// <summary>按深度优先顺序平铺策略文件。</summary>
    public static IEnumerable<CombatStrategyNode> Flatten(CombatStrategyNode root)
    {
        if (!root.IsFolder) yield return root;
        foreach (var child in root.Children)
            foreach (var file in Flatten(child)) yield return file;
    }

    /// <summary>扫描文件信息，忽略无权限目录和重解析点，避免目录联接形成循环。</summary>
    private static CombatStrategyNode ScanRoot(string path, CombatStrategyKind kind, CancellationToken ct)
    {
        var files = new List<FileInfo>();
        if (Directory.Exists(path))
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            var extension = kind == CombatStrategyKind.Txt ? ".txt" : ".json";
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (entry.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(entry))
                    files.Add(new FileInfo(entry));
            }
        }
        return BuildDirectory(path, path, files, ct);
    }

    /// <summary>从文件信息构建子树，没有策略的目录自然不会进入快照。</summary>
    private static CombatStrategyNode BuildDirectory(string root, string folder, IReadOnlyList<FileInfo> files, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var children = new List<CombatStrategyNode>();
        foreach (var file in files.Where(f => string.Equals(f.DirectoryName, folder, StringComparison.OrdinalIgnoreCase)))
            children.Add(new CombatStrategyNode
            {
                Name = Path.GetFileNameWithoutExtension(file.Name),
                RelativePath = Path.GetRelativePath(root, file.FullName),
                Strategy = CombatStrategyResolver.Parse(file.FullName),
                CreationTimeUtc = file.CreationTimeUtc,
                LastWriteTimeUtc = file.LastWriteTimeUtc,
                FileCount = 1,
            });
        foreach (var group in files.Where(f => !string.Equals(f.DirectoryName, folder, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(f => Path.GetRelativePath(folder, f.FullName).Split(Path.DirectorySeparatorChar)[0], StringComparer.OrdinalIgnoreCase))
            children.Add(BuildDirectory(root, Path.Combine(folder, group.Key), group.ToArray(), ct));
        return new CombatStrategyNode
        {
            Name = Path.GetFileName(folder),
            RelativePath = Path.GetRelativePath(root, folder) is "." ? "" : Path.GetRelativePath(root, folder),
            IsFolder = true,
            Children = children.OrderByDescending(n => n.IsFolder).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            FileCount = files.Count,
        };
    }

    /// <summary>按树顺序保留根目录和所有含文本策略的子目录。</summary>
    private static void AddScopes(CombatStrategyNode node, List<CombatStrategyNode> scopes)
    {
        scopes.Add(node);
        foreach (var child in node.Children.Where(n => n.IsFolder)) AddScopes(child, scopes);
    }
}

/// <summary>一次策略目录扫描的不可变快照。</summary>
/// <param name="Roots">允许的手动策略类型及其根节点。</param>
/// <param name="AutoMatchScopes">按队伍匹配的范围，根目录在第一项。</param>
public sealed record CombatStrategyCatalogSnapshot(
    IReadOnlyDictionary<CombatStrategyKind, CombatStrategyNode> Roots,
    IReadOnlyList<CombatStrategyNode> AutoMatchScopes);
