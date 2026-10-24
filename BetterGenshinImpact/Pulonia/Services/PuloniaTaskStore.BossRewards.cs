using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>首领领奖累计的独立持久化；统计不改写计划修订，按节点、清空批次与账号隔离。</summary>
public sealed partial class PuloniaTaskStore
{
    /// <summary>读取一个节点当前批次的各账号累计值，供设置页展示及运行前限额计算。</summary>
    public async Task<IReadOnlyDictionary<string, int>> ReadBossRewardCountsAsync(string planId, string taskId,
        string resetId, CancellationToken ct = default)
    {
        var path = GetBossRewardProgressPath(planId, taskId, resetId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadBossRewardCountsCoreAsync(path, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>每次确认领奖成功后立即落盘；使用存储写锁，失败时阻止任务继续消耗树脂。</summary>
    public async Task IncrementBossRewardCountAsync(string planId, string taskId, string resetId,
        string? accountId, CancellationToken ct = default)
    {
        var path = GetBossRewardProgressPath(planId, taskId, resetId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = AcquireWriterLock();
            var counts = await ReadBossRewardCountsCoreAsync(path, ct).ConfigureAwait(false);
            var key = accountId ?? string.Empty;
            counts[key] = checked(counts.GetValueOrDefault(key) + 1);
            await WriteAtomicallyAsync(path, PuloniaTaskJson.Write(counts), ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>用稳定身份生成文件名，避免把账号或外部参数直接用作路径。</summary>
    private string GetBossRewardProgressPath(string planId, string taskId, string resetId)
    {
        var identity = new JArray(planId, taskId, resetId).ToString(Newtonsoft.Json.Formatting.None);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(RootDirectory, "boss-rewards", hash + ".json");
    }

    /// <summary>读取累计值并拒绝负值或损坏内容，避免把未知累计当零重新刷取。</summary>
    private static async Task<Dictionary<string, int>> ReadBossRewardCountsCoreAsync(string path, CancellationToken ct)
    {
        var json = await ReadIfExistsAsync(path, ct).ConfigureAwait(false);
        if (json is null) return new Dictionary<string, int>(StringComparer.Ordinal);
        var counts = PuloniaTaskJson.Read<Dictionary<string, int>>(json);
        if (counts.Values.Any(count => count < 0))
            throw new InvalidDataException("首领累计领奖次数不能为负数。");
        return counts;
    }
}
