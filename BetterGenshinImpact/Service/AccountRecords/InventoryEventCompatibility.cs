using System;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>旧事件结构的读取兼容；文本解析仅保留在此边界。</summary>
internal static class InventoryEventCompatibility
{
    /// <summary>从旧版明确的 Id 字段补齐关联，不修改已有结构化标识或原始历史。</summary>
    internal static void RestoreErrorId(InventoryEvent item)
    {
        if (!string.IsNullOrEmpty(item.ErrorId) || item.Kind != "Report") return;
        foreach (var line in item.History)
        {
            if (!line.StartsWith("Id=", StringComparison.Ordinal)) continue;
            var end = line.IndexOf('；', 3);
            item.ErrorId = end < 0 ? line[3..] : line[3..end];
            return;
        }
    }
}
