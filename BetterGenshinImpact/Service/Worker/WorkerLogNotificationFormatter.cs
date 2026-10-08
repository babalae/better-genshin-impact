using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// 把聚合间隔内的日志行拼成一条通知文本。部分渠道对消息长度有限制，
/// 超长时保留最新的行并标注省略条数，保证推送不会因为过长被渠道拒绝。
/// </summary>
internal static class WorkerLogNotificationFormatter
{
    /// <summary>消息长度上限，留出各渠道自身的模板余量</summary>
    internal const int MaxMessageLength = 3000;

    internal static string? Format(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
        }

        if (builder.Length == 0)
        {
            return null;
        }

        if (builder.Length <= MaxMessageLength)
        {
            return builder.ToString();
        }

        return FormatTruncated(lines);
    }

    /// <summary>
    /// 从尾部（最新）开始保留整行，直到接近长度上限
    /// </summary>
    private static string FormatTruncated(IReadOnlyList<string> lines)
    {
        var kept = new List<string>();
        var length = 0;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            var required = line.Length + Environment.NewLine.Length;
            if (length + required > MaxMessageLength)
            {
                break;
            }

            kept.Insert(0, line);
            length += required;
        }

        if (kept.Count == 0)
        {
            // 单行就超长：保留行首（时间戳与级别在最前面，信息量最大）
            var last = lines[^1];
            var head = last.Length > MaxMessageLength ? last[..MaxMessageLength] : last;
            return $"{head}……";
        }

        var skipped = lines.Count - kept.Count;
        return $"……（已省略 {skipped} 条较早日志）{Environment.NewLine}{string.Join(Environment.NewLine, kept)}";
    }
}
