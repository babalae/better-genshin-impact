using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Script;

namespace BetterGenshinImpact.GameTask.AutoFight.Strategy;

/// <summary>后台加载列表副标题；按路径和修改时间缓存，单批最多 200 个、并行度 2。</summary>
public static class CombatStrategyMetadataProvider
{
    /// <summary>进程内元数据缓存，时间变化时覆盖旧项。</summary>
    private static readonly ConcurrentDictionary<string, (DateTime Time, CombatStrategyMetadata Metadata)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>触发区复用已加载的摘要，不因悬停额外访问磁盘。</summary>
    internal static string GetCachedSummary(string? path)
        => path != null && _cache.TryGetValue(path, out var cached) ? cached.Metadata.Summary : "";

    /// <summary>读取当前批次的文件信息；损坏文件作为元数据错误返回，不影响其他条目。</summary>
    public static async Task<IReadOnlyDictionary<string, CombatStrategyMetadata>> GetAsync(
        IReadOnlyList<CombatStrategyNode> files, CancellationToken ct)
    {
        var result = new ConcurrentDictionary<string, CombatStrategyMetadata>(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(files.Where(f => !f.IsFolder && f.Strategy != null).Take(200),
            new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, (file, token) =>
            {
                token.ThrowIfCancellationRequested();
                var path = file.Strategy!.Path;
                if (_cache.TryGetValue(path, out var cached) && cached.Time == file.LastWriteTimeUtc)
                {
                    result[path] = cached.Metadata;
                    return ValueTask.CompletedTask;
                }
                CombatStrategyMetadata metadata;
                try
                {
                    metadata = Read(file, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    metadata = new("解析失败：" + ex.Message, ex.Message);
                }
                _cache[path] = (file.LastWriteTimeUtc, metadata);
                result[path] = metadata;
                return ValueTask.CompletedTask;
            });
        return result;
    }

    /// <summary>通过已有解析器提取角色和作者，键鼠录制使用流式 JSON token 读取。</summary>
    private static CombatStrategyMetadata Read(CombatStrategyNode file, CancellationToken ct)
    {
        var strategy = file.Strategy!;
        if (strategy.Kind == CombatStrategyKind.Txt)
            return new(string.Join(" · ", CombatScriptParser.Parse(strategy.Path).AvatarNames), null);
        if (strategy.Kind == CombatStrategyKind.Json)
        {
            var json = JsonCombatStrategyParser.ParseFile(strategy.Path);
            var parts = json.GetCharacterNames();
            if (!string.IsNullOrWhiteSpace(json.Info.Author)) parts.Insert(0, "作者 " + json.Info.Author);
            return new(string.Join(" · ", parts), null);
        }
        var info = new FileInfo(strategy.Path);
        if (info.Length > 5 * 1024 * 1024) return new($"文件大小 {info.Length / 1024d / 1024:F1} MB", null);
        // 不构建事件对象；文件已限制为 5 MB，读取字节后使用 Utf8JsonReader 顺序扫描。
        var bytes = File.ReadAllBytes(strategy.Path);
        ReadOnlySpan<byte> content = bytes;
        if (content.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) content = content[3..];
        var reader = new Utf8JsonReader(content, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var count = 0;
        var time = 0d;
        var eventsDepth = -1;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1 && reader.ValueTextEquals("macroEvents"))
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray) throw new InvalidDataException("macroEvents 必须为数组");
                eventsDepth = reader.CurrentDepth;
            }
            else if (eventsDepth >= 0)
            {
                if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == eventsDepth) eventsDepth = -2;
                else if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == eventsDepth + 1) count++;
                else if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == eventsDepth + 2 && reader.ValueTextEquals("time"))
                {
                    reader.Read();
                    time = reader.GetDouble();
                }
            }
        }
        if (eventsDepth != -2) throw new InvalidDataException("录制文件缺少 macroEvents");
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, time));
        return new($"时长 {(int)duration.TotalMinutes}:{duration.Seconds:00} · {count} 个事件", null);
    }
}

/// <summary>策略的列表副标题与解析错误。</summary>
/// <param name="Summary">涉及角色、作者或录制时长。</param>
/// <param name="Error">解析失败原因，成功时为 null。</param>
public sealed record CombatStrategyMetadata(string Summary, string? Error);
