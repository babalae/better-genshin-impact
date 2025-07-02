using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;

namespace BetterGenshinImpact.GameTask.LogParse
{
    public class ItemData
    {
        public string ItemName { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
    }

    public class DurationData
    {
        public string Date { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
    }

    public class LogAnalyzer
    {
        private static readonly string[] ForbiddenItems = { "调查", "直接拾取" };
        private static List<ItemData> itemDataList = new();
        private static List<DurationData> durationDataList = new();
        private static List<string>? logList = null;
        private static string bgiLogDir = string.Empty;

        private static readonly ILogger<LogAnalyzer> _logger = App.GetLogger<LogAnalyzer>();

        public static void Initialize()
    {
        // 查找BetterGI安装路径
        var betterGiPath = FindBetterGiPath();
        if (!string.IsNullOrEmpty(betterGiPath))
        {
            bgiLogDir = Path.Combine(betterGiPath, "log");
        }
        else
        {
            _logger.LogError("无法找到BetterGI安装路径，使用默认路径");
            bgiLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterGI", "log");
        }
    }



        private static string FindBetterGiPath()
        {
            try
            {
                // Windows注册表查找
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            var displayName = subKey?.GetValue("DisplayName")?.ToString();
                            if (displayName != null && displayName.Contains("BetterGI"))
                            {
                                var installLocation = subKey?.GetValue("InstallLocation")?.ToString();
                                if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                                {
                                    return installLocation;
                                }
                            }
                        }
                    }
                }

                // Linux下的查找逻辑
                var commonPaths = new[]
                {
                    "/opt/BetterGI",
                    "/usr/local/BetterGI",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "BetterGI")
                };

                foreach (var path in commonPaths)
                {
                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"查找BetterGI路径时出错: {ex.Message}");
            }

            return string.Empty;
        }

        // 获取文件的Content-Type
    public static string GetContentType(string filename)
    {
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        return extension switch
        {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

        private static string FormatTimedelta(int? seconds)
        {
            if (seconds == null || seconds == 0)
                return "0分钟";

            var hours = seconds.Value / 3600;
            var minutes = (seconds.Value % 3600) / 60;

            var parts = new List<string>();
            if (hours > 0)
                parts.Add($"{hours}小时");
            if (minutes > 0)
                parts.Add($"{minutes}分钟");

            return parts.Count > 0 ? string.Join("", parts) : "0分钟";
        }

        private static Dictionary<string, object> ParseLog(string logContent, string dateStr)
        {
            var logPattern = @"\[([^]]+)\] \[([^]]+)\] ([^\n]+)\n?([^\n[]*)"; 
            var matches = Regex.Matches(logContent, logPattern);

            var itemCount = new Dictionary<string, int>();
            var duration = 0;
            var cacheDict = new Dictionary<string, List<string>>
            {
                ["物品名称"] = new(),
                ["时间"] = new(),
                ["日期"] = new()
            };

            DateTime? currentStart = null;
            DateTime? currentEnd = null;

            foreach (Match match in matches)
            {
                var timestamp = match.Groups[1].Value;
                var level = match.Groups[2].Value;
                var logType = match.Groups[3].Value;
                var details = match.Groups[4].Value.Trim();

                // 过滤禁用的关键词
                if (ForbiddenItems.Any(keyword => details.Contains(keyword)))
                    continue;

                // 转换时间戳
                if (DateTime.TryParseExact(timestamp, "HH:mm:ss.fff", null, System.Globalization.DateTimeStyles.None, out var currentTime))
                {
                    // 提取拾取内容
                    if (details.Contains("交互或拾取"))
                    {
                        var parts = details.Split('：');
                        if (parts.Length > 1)
                        {
                            var item = parts[1].Trim('"');
                            itemCount[item] = itemCount.GetValueOrDefault(item, 0) + 1;

                            // 检查是否存在匹配的行
                            var existingItem = itemDataList.FirstOrDefault(x => 
                                x.ItemName == item && x.Time == timestamp && x.Date == dateStr);

                            if (existingItem == null)
                            {
                                cacheDict["物品名称"].Add(item);
                                cacheDict["时间"].Add(timestamp);
                                cacheDict["日期"].Add(dateStr);
                            }
                        }
                    }

                    // 处理时间段
                    if (currentStart == null)
                    {
                        currentStart = currentTime;
                        currentEnd = currentTime;
                    }
                    else
                    {
                        var delta = (currentTime - currentEnd.Value).TotalSeconds;
                        if (delta <= 300)
                        {
                            currentEnd = currentTime;
                        }
                        else
                        {
                            if (delta > 0)
                            {
                                duration += (int)delta;
                            }
                            currentStart = currentTime;
                            currentEnd = currentTime;
                        }
                    }
                }
            }

            // 处理最后一段时间
            if (currentStart != null && currentEnd != null && currentStart != currentEnd)
            {
                var delta = (currentEnd.Value - currentStart.Value).TotalSeconds;
                duration += (int)delta;
            }

            return new Dictionary<string, object>
            {
                ["item_count"] = itemCount,
                ["duration"] = duration,
                ["cache_dict"] = cacheDict
            };
        }

        private static Dictionary<string, object> ReadLogFile(string filePath, string dateStr)
        {
            try
            {
                var logContent = File.ReadAllText(filePath, Encoding.UTF8);
                return ParseLog(logContent, dateStr);
            }
            catch (FileNotFoundException)
            {
                return new Dictionary<string, object> { ["error"] = "文件未找到" };
            }
            catch (Exception e)
            {
                return new Dictionary<string, object> { ["error"] = $"发生了一个未知错误: {e.Message}" };
            }
        }

        private static List<string> GetLogList()
        {
            if (!Directory.Exists(bgiLogDir))
            {
                return new List<string>();
            }

            var logFiles = Directory.GetFiles(bgiLogDir, "better-genshin-impact*.log")
                .Select(f => Path.GetFileName(f))
                .Where(f => f.StartsWith("better-genshin-impact"))
                .Select(f => f.Replace("better-genshin-impact", "").Replace(".log", ""))
                .ToList();

            var filteredLogs = new List<string>();
            var durationDict = new Dictionary<string, List<object>>
            {
                ["日期"] = new(),
                ["持续时间（秒）"] = new()
            };
            var cachedDict = new Dictionary<string, List<string>>
            {
                ["物品名称"] = new(),
                ["时间"] = new(),
                ["日期"] = new()
            };

            foreach (var file in logFiles)
            {
                var filePath = Path.Combine(bgiLogDir, $"better-genshin-impact{file}.log");
                var result = ReadLogFile(filePath, file);

                if (result.ContainsKey("error"))
                    continue;

                // 过滤掉不需要的物品
                var items = new Dictionary<string, int>((Dictionary<string, int>)result["item_count"]);
                foreach (var forbiddenItem in ForbiddenItems)
                {
                    items.Remove(forbiddenItem);
                }

                // 只保留有物品的日志
                if (items.Count > 0)
                {
                    filteredLogs.Add(file);
                    durationDict["日期"].Add(file);
                    durationDict["持续时间（秒）"].Add(result["duration"]);
                    
                    var cacheDict = (Dictionary<string, List<string>>)result["cache_dict"];
                    cachedDict["物品名称"].AddRange(cacheDict["物品名称"]);
                    cachedDict["时间"].AddRange(cacheDict["时间"]);
                    cachedDict["日期"].AddRange(cacheDict["日期"]);
                }
            }

            // 更新全局数据
            durationDataList.Clear();
            for (int i = 0; i < durationDict["日期"].Count; i++)
            {
                durationDataList.Add(new DurationData
                {
                    Date = durationDict["日期"][i].ToString()!,
                    DurationSeconds = (int)durationDict["持续时间（秒）"][i]
                });
            }

            itemDataList.Clear();
            for (int i = 0; i < cachedDict["物品名称"].Count; i++)
            {
                itemDataList.Add(new ItemData
                {
                    ItemName = cachedDict["物品名称"][i],
                    Time = cachedDict["时间"][i],
                    Date = cachedDict["日期"][i]
                });
            }

            return filteredLogs;
        }

        // API方法
        public static object GetLogListData()
        {
            if (logList == null)
            {
                logList = GetLogList();
            }
            logList.Reverse(); // 最新的日志排在前面
            
            return new { list = logList };
        }

        public static object GetAnalyseData(string date = "all")
        {
            if (date == "all")
            {
                return AnalyseAllLogs();
            }
            else
            {
                return AnalyseSingleLog(date);
            }
        }

        public static object GetItemTrendData(string itemName)
        {
            if (!string.IsNullOrEmpty(itemName))
            {
                return AnalyseItemHistory(itemName);
            }
            else
            {
                return new { };
            }
        }

        public static object GetDurationTrendData()
        {
            return AnalyseDurationHistory();
        }

        public static object GetTotalItemsTrendData()
        {
            return AnalyseAllItems();
        }

        // 分析方法
        private static object AnalyseAllLogs()
        {
            if (durationDataList.Count == 0 || itemDataList.Count == 0)
            {
                return new { duration = "0分钟", item_count = new Dictionary<string, int>() };
            }

            var totalDuration = durationDataList.Sum(x => x.DurationSeconds);
            var totalItemCount = itemDataList
                .GroupBy(x => x.ItemName)
                .ToDictionary(g => g.Key, g => g.Count());

            return new
            {
                duration = FormatTimedelta(totalDuration),
                item_count = totalItemCount
            };
        }

        private static object AnalyseSingleLog(string date)
        {
            var filteredItems = itemDataList.Where(x => x.Date == date).ToList();
            var filteredDuration = durationDataList.Where(x => x.Date == date).ToList();

            if (filteredDuration.Count == 0 || filteredItems.Count == 0)
            {
                return new { duration = "0分钟", item_count = new Dictionary<string, int>() };
            }

            var totalDuration = filteredDuration.Sum(x => x.DurationSeconds);
            var totalItemCount = filteredItems
                .GroupBy(x => x.ItemName)
                .ToDictionary(g => g.Key, g => g.Count());

            return new
            {
                duration = FormatTimedelta(totalDuration),
                item_count = totalItemCount
            };
        }

        private static object AnalyseItemHistory(string itemName)
        {
            if (itemDataList.Count == 0)
            {
                return new { msg = "no data." };
            }

            var filteredData = itemDataList.Where(x => x.ItemName == itemName);
            var dataCounts = filteredData
                .GroupBy(x => x.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            return new { data = dataCounts };
        }

        private static object AnalyseDurationHistory()
        {
            if (durationDataList.Count == 0)
            {
                return new { msg = "no data." };
            }

            var totalMinutes = durationDataList
                .GroupBy(x => x.Date)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DurationSeconds) / 60);

            return new { data = totalMinutes };
        }

        private static object AnalyseAllItems()
        {
            if (itemDataList.Count == 0)
            {
                return new { msg = "no data." };
            }

            var dataCounts = itemDataList
                .GroupBy(x => x.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            return new { data = dataCounts };
        }
    }
}