using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 日志分析页：当前数量带相对上次增减，下拉选择对比几次。
/// </summary>
public static class InventoryMaterialStatsLogHtml
{
    public const int MaxPreviousCount = 10;

    public static string BuildHelpHtml()
    {
        return """
               <!DOCTYPE html>
               <html lang="zh">
               <head>
                   <meta charset="UTF-8">
                   <meta name="viewport" content="width=device-width, initial-scale=1.0">
                   <title>背包材料统计说明</title>
                   <style>
                       body { font-family: Arial, sans-serif; margin: 20px; }
                       .content { padding: 20px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1); }
                       h1 { font-size: 24px; color: #333; }
                       p { font-size: 16px; color: #555; line-height: 1.6; }
                   </style>
               </head>
               <body>
                   <div class="content">
                       <h1>背包材料统计说明</h1>
                       <p>分析页里的背包材料表不会自动出现，需要先勾选本项，并且本地已经有扫描记录。</p>
                       <p>生成方法：打开 BetterGI「独立任务」页，找到「背包材料统计」并运行；也可以在配置组中添加该独立任务，随日常执行。</p>
                       <p>至少成功扫描两次后，才会有「相对上次」的数量增减。</p>
                       <p>表上方的下拉框可以选择对比最近几次扫描。选 1 只显示「当前数量」以及相对上一次有变化的材料；加大次数会多出更早的列，并列出那些只在更早几次才有增减的行。</p>
                   </div>
               </body>
               </html>
               """;
    }

    public static string BuildSection()
    {
        var copies = InventoryMaterialStatsRecordStore.ListCopiesWithHistory();
        var html = new StringBuilder();
        html.AppendLine("<h2>背包材料统计</h2>");
        if (copies.Count == 0)
        {
            html.AppendLine("<p>还没有可对比的扫描记录。请先在「独立任务」中运行背包材料统计，至少成功两次后再分析。</p>");
            return html.ToString();
        }

        foreach (var (folderName, records) in copies)
        {
            AppendCopy(html, folderName, records);
        }

        return html.ToString();
    }

    private static void AppendCopy(StringBuilder html, string folderName, List<InventoryMaterialStatsRecord> records)
    {
        var latest = records[0];
        var previous = records.Skip(1).Take(MaxPreviousCount).ToList();
        if (previous.Count == 0)
        {
            return;
        }

        var copyDir = InventoryMaterialStatsRecordStore.GetCopyDirectory(folderName);
        var icons = IndexIcons(copyDir);
        var unnamed = ListUnnamedIcons(copyDir);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rec in previous.Prepend(latest))
        {
            foreach (var name in rec.Counts.Keys)
            {
                names.Add(name);
            }
        }

        var rows = new List<(string Name, int Current, int LastDelta, int[] Qty, int[] Deltas)>();
        foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            latest.Counts.TryGetValue(name, out var current);
            if (current < 0)
            {
                continue;
            }

            previous[0].Counts.TryGetValue(name, out var lastQty);
            var lastDelta = lastQty < 0 ? int.MinValue : current - lastQty;
            var qty = new int[previous.Count];
            var deltas = new int[previous.Count];
            var hasAnyChange = lastDelta != 0 && lastDelta != int.MinValue;
            for (var i = 0; i < previous.Count; i++)
            {
                previous[i].Counts.TryGetValue(name, out var q);
                if (q < 0)
                {
                    qty[i] = int.MinValue;
                    deltas[i] = int.MinValue;
                    continue;
                }

                qty[i] = q;
                var older = i + 1 < previous.Count ? previous[i + 1] : records.Skip(1 + previous.Count).FirstOrDefault();
                if (older == null)
                {
                    deltas[i] = int.MinValue;
                    continue;
                }

                older.Counts.TryGetValue(name, out var o);
                deltas[i] = o < 0 ? int.MinValue : q - o;
                if (deltas[i] != 0 && deltas[i] != int.MinValue)
                {
                    hasAnyChange = true;
                }
            }

            if (!hasAnyChange)
            {
                continue;
            }

            rows.Add((name, current, lastDelta, qty, deltas));
        }

        html.AppendLine($"<div class=\"ims-copy\">");
        html.AppendLine($"<h3>{WebUtility.HtmlEncode(folderName)}</h3>");
        html.AppendLine(
            $"<p>本次：{FormatTime(latest)}（服务器日 {WebUtility.HtmlEncode(latest.ServerDate)}）。对比次数 1 只显示当前有变化的材料；加大后会列出更早几次才有增减的行，并多出对应列。</p>");
        html.AppendLine("<div class=\"ims-toolbar\">");
        html.AppendLine("    <label>对比次数 ");
        html.AppendLine($"        <select class=\"ims-compare-count\" data-max=\"{previous.Count + 1}\">");
        for (var i = 1; i <= previous.Count + 1; i++)
        {
            var selected = i == 1 ? " selected" : "";
            html.AppendLine($"            <option value=\"{i}\"{selected}>最近 {i} 次</option>");
        }

        html.AppendLine("        </select>");
        html.AppendLine("    </label>");
        html.AppendLine("</div>");

        if (rows.Count == 0)
        {
            html.AppendLine("<p>相对上一次数量无变化。</p>");
        }
        else
        {
            html.AppendLine("<div class=\"sticky-table\">");
            html.AppendLine("<table class=\"ims-table\">");
            html.AppendLine("    <thead>");
            html.AppendLine("    <tr class=\"sticky-header\">");
            html.AppendLine("        <th>图</th>");
            html.AppendLine("        <th data-sort-type=\"string\">物品</th>");
            html.AppendLine("        <th data-sort-type=\"number\">当前数量</th>");
            for (var i = 0; i < previous.Count; i++)
            {
                html.AppendLine(
                    $"        <th class=\"ims-hist\" data-ims-col=\"{i + 1}\" data-sort-type=\"number\" style=\"display:none\">{FormatTime(previous[i])}</th>");
            }

            html.AppendLine("    </tr>");
            html.AppendLine("    </thead>");
            html.AppendLine("    <tbody>");

            foreach (var (name, current, lastDelta, qty, deltas) in rows)
            {
                var deltaParts = new int[previous.Count + 1];
                deltaParts[0] = lastDelta == int.MinValue ? 0 : lastDelta;
                for (var i = 0; i < previous.Count; i++)
                {
                    deltaParts[i + 1] = deltas[i] == int.MinValue ? 0 : deltas[i];
                }

                var rowHidden = lastDelta == 0 || lastDelta == int.MinValue
                    ? " style=\"display:none\""
                    : "";
                html.AppendLine($"    <tr data-ims-deltas=\"{string.Join(",", deltaParts)}\"{rowHidden}>");
                html.AppendLine($"        <td class=\"ims-icon-cell\">{RenderIcon(icons, name)}</td>");
                html.AppendLine($"        <td>{WebUtility.HtmlEncode(name)}</td>");
                html.AppendLine($"        <td data-sort=\"{current}\">{FormatQtyDelta(current, lastDelta)}</td>");
                for (var i = 0; i < previous.Count; i++)
                {
                    var sort = qty[i] == int.MinValue ? 0 : qty[i];
                    html.AppendLine(
                        $"        <td class=\"ims-hist\" data-ims-col=\"{i + 1}\" data-sort=\"{sort}\" style=\"display:none\">{FormatQtyDelta(qty[i], deltas[i])}</td>");
                }

                html.AppendLine("    </tr>");
            }

            html.AppendLine("    </tbody>");
            html.AppendLine("</table>");
            html.AppendLine("</div>");
        }

        if (unnamed.Count > 0)
        {
            html.AppendLine($"<p>未识别（待命名）{unnamed.Count} 张：</p>");
            html.AppendLine("<div class=\"ims-gallery\">");
            foreach (var path in unnamed)
            {
                html.AppendLine(RenderImg(path, Path.GetFileNameWithoutExtension(path)));
            }

            html.AppendLine("</div>");
        }

        html.AppendLine("</div>");
        html.AppendLine("<script>if (typeof initLogParseUi === 'function') initLogParseUi();</script>");
    }

    private static string FormatTime(InventoryMaterialStatsRecord record)
    {
        return record.RecordedAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string FormatQtyDelta(int qty, int delta)
    {
        if (qty == int.MinValue)
        {
            return "<span class=\"ims-skip\">—</span>";
        }

        return $"{qty} {FormatDelta(delta)}";
    }

    private static string FormatDelta(int diff)
    {
        if (diff == int.MinValue)
        {
            return "<span class=\"ims-skip\">—</span>";
        }

        if (diff > 0)
        {
            return $"<span class=\"ims-gain\">(+{diff})</span>";
        }

        if (diff < 0)
        {
            return $"<span class=\"ims-lost\">({diff})</span>";
        }

        return "<span class=\"ims-zero\">(0)</span>";
    }

    private static string RenderIcon(IReadOnlyDictionary<string, string> icons, string name)
    {
        if (!icons.TryGetValue(name, out var path))
        {
            return "<span class=\"ims-zero\">无图</span>";
        }

        return RenderImg(path, name);
    }

    private static string RenderImg(string path, string alt)
    {
        var dataUri = ToDataUri(path);
        if (dataUri == null)
        {
            return "<span class=\"ims-zero\">无图</span>";
        }

        return $"<img class=\"ims-icon\" src=\"{dataUri}\" alt=\"{WebUtility.HtmlEncode(alt)}\">";
    }

    private static Dictionary<string, string> IndexIcons(string copyDir)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in new[]
                 {
                     InventoryMaterialStatsUnrecognizedStore.RecognizedFolderName,
                     InventoryMaterialStatsUnrecognizedStore.FolderName
                 })
        {
            var root = Path.Combine(copyDir, folder);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name) ||
                    name.StartsWith(InventoryMaterialStatsUnrecognizedStore.DumpPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                map.TryAdd(name, file);
            }
        }

        return map;
    }

    private static List<string> ListUnnamedIcons(string copyDir)
    {
        var root = Path.Combine(copyDir, InventoryMaterialStatsUnrecognizedStore.FolderName);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories)
            .Where(f => Path.GetFileNameWithoutExtension(f)
                .StartsWith(InventoryMaterialStatsUnrecognizedStore.DumpPrefix, StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ToDataUri(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
            {
                return null;
            }

            return "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        catch
        {
            return null;
        }
    }
}
