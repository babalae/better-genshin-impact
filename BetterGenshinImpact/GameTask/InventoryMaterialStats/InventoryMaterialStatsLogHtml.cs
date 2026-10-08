using BetterGenshinImpact.Core.Config;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 日志分析页：两条扫描记录做差；时间用双下拉选择，食物默认隐藏。
/// 图标以相对路径写入 JSON，由页面按需加载（不内嵌 base64）。
/// </summary>
public static class InventoryMaterialStatsLogHtml
{
    public const int MaxRecordCount = 11;

    /// <summary>与日志分析 HTML 同级的图标缓存目录名（位于 log/logparse 下）。</summary>
    public const string IconCacheFolderName = "ims-icons";

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
                       <p>至少成功扫描两次后，才能对比增减。上方两个时间下拉默认是「最近一次」对比「上一次」，可自行改选。</p>
                       <p>表格首行是原石、摩拉；下面按材料、养成道具、食物各占一行格子，格子里铺开有变化的物品（图+名字 数量（增量））。食物默认不显示，勾选「显示食物增量」后才会出现。</p>
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
        var slice = records.Take(MaxRecordCount).ToList();
        if (slice.Count < 2)
        {
            return;
        }

        var copyDir = InventoryMaterialStatsRecordStore.GetCopyDirectory(folderName);
        var icons = IndexIcons(copyDir);
        var usedNames = CollectNames(slice);
        var iconRels = PublishIconsForWeb(folderName, icons, usedNames);
        var payload = BuildPayload(folderName, slice, usedNames, icons, iconRels);
        var json = JsonConvert.SerializeObject(payload);

        html.AppendLine("<div class=\"ims-copy\">");
        html.AppendLine($"<h3>{WebUtility.HtmlEncode(folderName)}</h3>");
        html.AppendLine("<div class=\"ims-toolbar\">");
        html.AppendLine("    <label>较新 ");
        html.AppendLine("        <select class=\"ims-time-newer\"></select>");
        html.AppendLine("    </label>");
        html.AppendLine("    <label>对比 ");
        html.AppendLine("        <select class=\"ims-time-older\"></select>");
        html.AppendLine("    </label>");
        html.AppendLine("    <label class=\"ims-food-toggle\">");
        html.AppendLine("        <input type=\"checkbox\" class=\"ims-show-food\"> 显示食物增量");
        html.AppendLine("    </label>");
        html.AppendLine("</div>");
        html.AppendLine("<div class=\"sticky-table\">");
        html.AppendLine("<table class=\"ims-table\">");
        html.AppendLine("    <tbody class=\"ims-tbody\"></tbody>");
        html.AppendLine("</table>");
        html.AppendLine("</div>");
        html.AppendLine($"<script type=\"application/json\" class=\"ims-data\">{json}</script>");
        html.AppendLine("</div>");
        html.AppendLine("<script>if (typeof initLogParseUi === 'function') initLogParseUi();</script>");
    }

    private static HashSet<string> CollectNames(List<InventoryMaterialStatsRecord> records)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rec in records)
        {
            foreach (var name in rec.Counts.Keys)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static object BuildPayload(
        string folderName,
        List<InventoryMaterialStatsRecord> records,
        HashSet<string> names,
        Dictionary<string, IconEntry> icons,
        Dictionary<string, string> iconRels)
    {
        var items = names
            .Select(name =>
            {
                icons.TryGetValue(name, out var entry);
                iconRels.TryGetValue(name, out var iconRel);
                var page = ResolvePage(name, entry);
                return new
                {
                    name,
                    page,
                    icon = iconRel
                };
            })
            .OrderBy(i => PageOrder(i.page))
            .ThenBy(i => i.name, StringComparer.Ordinal)
            .ToList();

        return new
        {
            folder = folderName,
            records = records.Select((r, index) => new
            {
                index,
                label = FormatTime(r),
                serverDate = r.ServerDate,
                counts = r.Counts
                    .Where(kv => kv.Value >= 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value)
            }).ToList(),
            items
        };
    }

    private static string ResolvePage(string name, IconEntry? entry)
    {
        if (name is "原石" or "摩拉")
        {
            return "货币";
        }

        return entry?.Page switch
        {
            "材料" => "材料",
            "养成道具" => "养成道具",
            "食物" => "食物",
            _ => "其他"
        };
    }

    private static int PageOrder(string page) => page switch
    {
        "货币" => 0,
        "材料" => 1,
        "养成道具" => 2,
        "食物" => 3,
        _ => 4
    };

    private static string FormatTime(InventoryMaterialStatsRecord record)
    {
        return record.RecordedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private sealed record IconEntry(string Path, string Page);

    private static Dictionary<string, IconEntry> IndexIcons(string copyDir)
    {
        var map = new Dictionary<string, IconEntry>(StringComparer.Ordinal);
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

                var page = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
                map.TryAdd(name, new IconEntry(file, page));
            }
        }

        return map;
    }

    /// <summary>
    /// 将本次用到的图标同步到 log/logparse/ims-icons，返回物品名 → 相对 HTML 的路径（仅路径，不内嵌图片）。
    /// </summary>
    private static Dictionary<string, string> PublishIconsForWeb(
        string folderName,
        Dictionary<string, IconEntry> icons,
        HashSet<string> usedNames)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (icons.Count == 0 || usedNames.Count == 0)
        {
            return result;
        }

        var safeFolder = SanitizePathSegment(folderName);
        var destRoot = Path.Combine(Global.Absolute(@"log\logparse"), IconCacheFolderName, safeFolder);
        Directory.CreateDirectory(destRoot);

        foreach (var name in usedNames)
        {
            if (!icons.TryGetValue(name, out var entry))
            {
                continue;
            }

            try
            {
                if (!File.Exists(entry.Path))
                {
                    continue;
                }

                var page = SanitizePathSegment(string.IsNullOrWhiteSpace(entry.Page) ? "其他" : entry.Page);
                var fileName = Path.GetFileName(entry.Path);
                if (string.IsNullOrEmpty(fileName))
                {
                    continue;
                }

                var destDir = Path.Combine(destRoot, page);
                Directory.CreateDirectory(destDir);
                var destPath = Path.Combine(destDir, fileName);
                if (!File.Exists(destPath))
                {
                    File.Copy(entry.Path, destPath, overwrite: false);
                }

                // 相对 log/logparse 下 HTML 的路径；浏览器只会对实际渲染的 img 发起加载
                result[name] = string.Join('/', IconCacheFolderName, safeFolder, page, fileName);
            }
            catch
            {
                // 单个图标失败不影响整体
            }
        }

        return result;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var name = new string(chars).Trim();
        return string.IsNullOrEmpty(name) ? "default" : name;
    }
}
