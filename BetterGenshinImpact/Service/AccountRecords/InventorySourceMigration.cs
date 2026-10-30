using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>将旧来源字典中的批次摘要一次性转存为事件，不增加来源分析逻辑。</summary>
public sealed partial class InventoryStore
{
    /// <summary>先可靠保存各账号的历史，再精简来源字典；稳定 ID 保证中断后可重复执行。</summary>
    private void MigrateSourceAttempts(Dictionary<string, AccountRecordSource> sources)
    {
        var legacy = sources.Values.SelectMany(source => source.LegacyAttempts.Select(attempt => (source, attempt))).ToArray();
        if (legacy.Length == 0) return;
        // 旧来源接受的 UID 范围更宽；仅迁移当前支持的账号，避免旧数据阻塞维护和写入。
        foreach (var group in legacy.Where(pair => IsValidUid(pair.attempt.Uid)).GroupBy(pair => pair.attempt.Uid))
        {
            var data = Load(group.Key);
            foreach (var (source, attempt) in group)
            {
                var id = "legacy-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Reference + "\n" + attempt.Id)));
                if (data.Events.Any(item => item.Id == id) || HasReceipt(data, "历史上报", "Id:" + id)) continue;
                // 旧摘要缺乏完整逐物品输入，保留原摘要，不伪造库存或重新亮灯。
                data.Events.Add(new InventoryEvent
                {
                    Id = id, Name = "历史上报", Source = source.Reference, AttemptId = attempt.Id,
                    Kind = "LegacyAttempt", State = attempt.Failed ? "Error" : "Completed", Archived = true,
                    RecordedAt = attempt.CreatedAt, EndedAt = attempt.CreatedAt,
                    Message = "旧版来源上报摘要。", History = attempt.Errors.ToList()
                });
            }
            PersistInventory(data);
        }
        foreach (var source in sources.Values) source.LegacyAttempts.Clear();
        Write(Path.Combine(_root, "sources.json"), sources);
    }
}
