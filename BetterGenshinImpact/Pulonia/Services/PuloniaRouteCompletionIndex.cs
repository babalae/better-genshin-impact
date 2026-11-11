using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 一个账户的完成记录按路线身份建立的只读索引，供运行判定、界面和运行预览多次查询；
/// 由一份已读取的完成记录构建，文件变化后重建。
/// </summary>
public sealed class PuloniaRouteCompletionIndex
{
    /// <summary>路线身份 → 最新完成记录。</summary>
    private readonly Dictionary<string, PuloniaRouteCompletion> _byRoute = new(StringComparer.Ordinal);

    /// <summary>没有任何记录的索引。</summary>
    public static PuloniaRouteCompletionIndex Empty { get; } = new([]);

    /// <summary>建立索引；同一路线有多条时保留完成时间最晚的一条（文件中本来每份路线只有一条）。</summary>
    /// <param name="completions">一个账户的完成记录。</param>
    public PuloniaRouteCompletionIndex(IEnumerable<PuloniaRouteCompletion> completions)
    {
        foreach (var completion in completions)
        {
            if (!_byRoute.TryGetValue(completion.Route, out var current) || current.CompletedAt < completion.CompletedAt)
                _byRoute[completion.Route] = completion;
        }
        Completions = new ReadOnlyCollection<PuloniaRouteCompletion>(_byRoute.Values.ToArray());
    }

    /// <summary>全部完成记录（每份路线一条），只读。</summary>
    public IReadOnlyList<PuloniaRouteCompletion> Completions { get; }

    /// <summary>查找一份路线的完成记录；没有时为空。</summary>
    /// <param name="route">路线身份。</param>
    public PuloniaRouteCompletion? Find(string route) => _byRoute.GetValueOrDefault(route);

    /// <summary>
    /// 枚举路线身份以指定前缀开头的记录，供目录引用在不展开目录时计数。
    /// </summary>
    /// <param name="routePrefix">目录对应的路线身份前缀，以 <c>/</c> 结尾。</param>
    /// <param name="recursive">是否包含子目录中的文件。</param>
    public IEnumerable<PuloniaRouteCompletion> EnumerateUnder(string routePrefix, bool recursive)
    {
        foreach (var (route, completion) in _byRoute)
        {
            if (!route.StartsWith(routePrefix, StringComparison.Ordinal))
                continue;
            if (!recursive && route.IndexOf('/', routePrefix.Length) >= 0)
                continue;
            yield return completion;
        }
    }
}
