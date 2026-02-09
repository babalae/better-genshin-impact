using BetterGenshinImpact.Helpers;
﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.FarmingPlan;
using Newtonsoft.Json;
using Wpf.Ui.Violeta.Controls;
using static BetterGenshinImpact.GameTask.LogParse.LogParse.ConfigGroupEntity;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BetterGenshinImpact.GameTask.LogParse
{
    public class LogParse
    {
        private static readonly string _configPath = Global.Absolute(@"log\logparse\config.json");
        private static readonly string _assets_dir = Global.Absolute($@"GameTask\LogParse\Assets");
        // 添加一个静态事件用于通知日志的生成状态
        public static event Action<string> HtmlGenerationStatusChanged = delegate { };
        private static void NotifyHtmlGenerationStatus(string status)
        {
            HtmlGenerationStatusChanged.Invoke(status);
        }

        private static List<string> SafeReadAllLines(string filePath)
        {
            var lines = new List<string>();
            try
            {
                // 使用 FileStream 和 StreamReader，允许共享读取
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream);
                while (reader.ReadLine() is { } line)
                {
                    lines.Add(line);
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"{Lang.S["GameTask_11814_ccb203"]});
            }

            return lines;
        }

        public static List<ConfigGroupEntity> ParseFile(List<(string, string)> logFiles)
        {
            List<(string, string)> logLines = new();
            foreach (var logFile in logFiles)
            {
                string[] logstrs = SafeReadAllLines(logFile.Item1).ToArray();
                foreach (var logstr in logstrs)
                {
                    logLines.Add((logstr, logFile.Item2));
                }
            }

            return Parse(logLines);
        }

        public static List<ConfigGroupEntity> Parse(List<(string, string)> logLines)
        {
            // var logstrs = log.Item1;
            List<ConfigGroupEntity> configGroupEntities = new();
            ConfigGroupEntity? configGroupEntity = null;
            ConfigTask? configTask = null;
            for (int i = 0; i < logLines.Count; i++)
            {
                var logstr = logLines[i].Item1;
                var logrq = logLines[i].Item2;
                //if("配置组 \"${}\" 加载完成，共25个脚本，开始执行")


                // 定义正则表达式

                var result = ParseBgiLine(Lang.S["Script_001_22f6f8"]"(.+?)"" 加载完成，共(\d+)个脚本", logstr);
                if (result.Item1)
                {
                    configGroupEntity = new()
                    {
                        Name = result.Item2[1],
                        StartDate = ParsePreDataTime(logLines, i - 1, logrq)
                    };
                    configGroupEntities.Add(configGroupEntity);
                }

                if (configGroupEntity != null)
                {
                    //配置组 "战斗" 执行结束
                    result = ParseBgiLine(Lang.S["GameTask_11811_bc9bd3"]{configGroupEntity.Name}\" 执行结束", logstr);
                    if (result.Item1)
                    {
                        configGroupEntity.EndDate = ParsePreDataTime(logLines, i - 1, logrq);
                        configGroupEntity = null;
                    }
                }


                if (configGroupEntity != null)
                {
                    result = ParseBgiLine(Lang.S["GameTask_11810_ca9c5f"]"(.+?)""", logstr);
                    if (result.Item1)
                    {
                        configTask = new();
                        configTask.Name = result.Item2[1];
                        configTask.StartDate = ParsePreDataTime(logLines, i - 1, logrq);
                        configGroupEntity.ConfigTaskList.Add(configTask);
                    }

                    if (configTask != null)
                    {
                        
                        if (logstr.Contains(Lang.S["Gen_10261_0929f0"]))
                        {
                            configTask.Fault.PathingSuccessEnd = false;
                        }
                        
                        //前往七天神像复活
                        if (logstr.EndsWith(Lang.S["GameTask_11809_a7fe05"]))
                        {
                            configTask.Fault.ReviveCount++;
                        }

                        //传送失败，重试 n 次
                        result = ParseBgiLine(Lang.S["GameTask_11808_eb8caa"], logstr);
                        if (result.Item1)
                        {
                            configTask.Fault.TeleportFailCount = int.Parse(result.Item2[1]);

                        }

                        //战斗超时结束
                        if (logstr == Lang.S["GameTask_10558_7bb8b3"])
                        {
                            configTask.Fault.BattleTimeoutCount++;
                        }

                        //重试一次路线或放弃此路线！
                        if (logstr.EndsWith(Lang.S["GameTask_11807_4fce93"]))
                        {
                            configTask.Fault.RetryCount++;
                        }

                        //疑似卡死，尝试脱离...
                        if (logstr == Lang.S["GameTask_11017_16f1f4"])
                        {
                            configTask.Fault.StuckCount++;
                        }

                        //One or more errors occurred
                        result = ParseBgiLine(Lang.S["GameTask_11806_997e88"]"(.+?)""", logstr);
                        if (result.Item1)
                        {
                            configTask.Fault.ErrCount++;
                        }

                        if (logstr.StartsWith(Lang.S["GameTask_11805_1dec4e"]" + configTask.Name + "\""))
                        {
                            configTask.EndDate = ParsePreDataTime(logLines, i - 1, logrq);
                            configTask = null;
                        }

                        result = ParseBgiLine(Lang.S["GameTask_11804_c0ba4b"]"(.+?)""", logstr);
                        if (result.Item1)
                        {
                            configTask.AddPick(result.Item2[1]);
                        }
                        
                    }
                }


            }
            foreach (var groupEntity in configGroupEntities)
            {
                //无论如何给个结束时间
                if (groupEntity is { EndDate: null })
                {
                    if (groupEntity.ConfigTaskList.Count > 0)
                    {
                        ConfigTask ct = groupEntity.ConfigTaskList[^1];
                        if (ct != null)
                        {
                            groupEntity.EndDate = ct.EndDate ?? ct.StartDate;
                        }
                    }

                }
            }


            return configGroupEntities;
        }

        private static (bool, List<string>) ParseBgiLine(string pattern, string str)
        {
            Match match = Regex.Match(str, pattern);
            if (match.Success)
            {
                return (true, match.Groups.Cast<Group>().Select(g => g.Value).ToList());
            }

            return (false, []);
        }

        private static DateTime? ParsePreDataTime(List<(string, string)> list, int index, string logrq)
        {
            if (index < 0)
            {
                return null;
            }

            (bool, List<string>) result = ParseBgiLine(@"\[(\d{2}:\d{2}:\d{2})\.\d+\]", list[index].Item1);
            if (result.Item1)
            {
                DateTime dateTime = DateTime.ParseExact(logrq + " " + result.Item2[1], "yyyy-MM-dd HH:mm:ss", null);
                return dateTime;
            }

            return null;
        }

        public class ConfigGroupEntity
        {
            //配置组名字
            public string Name { get; set; }

            //开始日期
            public DateTime? StartDate { get; set; }

            //结束日期
            public DateTime? EndDate { get; set; }

            //配置人物列表xxx.json
            public List<ConfigTask> ConfigTaskList { get; set; } = new();
            
            public class ConfigTask
            {
                public bool IsMerger { get; set; } = false;
                public string Name { get; set; }

                //开始日期
                public DateTime? StartDate { get; set; }

                //结束日期
                public DateTime? EndDate { get; set; }

                //拾取字典
                public Dictionary<string, int> Picks { get; set; } = new();

                

                public void AddPick(string val)
                {
                    if (!Picks.ContainsKey(val))
                    {
                        Picks.Add(val, 0);
                    }

                    Picks[val] = Picks[val] + 1;
                }

                public FaultScenario Fault { get; set; } = new();

                public class FaultScenario
                {
                    public bool PathingSuccessEnd { get; set; } = true;
                    //复活次数
                    public int ReviveCount { get; set; } = 0;

                    //传送失败次数
                    public int TeleportFailCount { get; set; } = 0;

                    //疑似卡死次数
                    public int StuckCount { get; set; } = 0;

                    //重试次数
                    public int RetryCount { get; set; } = 0;

                    //战斗超时
                    public int BattleTimeoutCount { get; set; } = 0;

                    //异常发生次数
                    public int ErrCount { get; set; } = 0;
                }

            }
        }

        public static List<(string FileName, string Date)> GetLogFiles(string folderPath)
        {
            // 定义返回的元组列表
            var result = new List<(string FileName, string Date)>();

            // 确认文件夹是否存在
            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine(Lang.S["GameTask_11803_388f9e"]);
                return result;
            }

            // 定义文件名匹配的正则表达式
            string pattern = @"^better-genshin-impact(\d{8})(_\d{3})*\.log$";
            Regex regex = new Regex(pattern);

            // 遍历文件夹中的所有文件
            var files = Directory.GetFiles(folderPath);
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);

                // 检查文件名是否匹配模式
                var match = regex.Match(fileName);
                if (match.Success)
                {
                    string dateString = match.Groups[1].Value;

                    // 尝试将日期字符串格式化为 yyyy-MM-dd
                    if (DateTime.TryParseExact(dateString, "yyyyMMdd", null, DateTimeStyles.None,
                            out DateTime parsedDate))
                    {
                        result.Add((folderPath + "\\" + fileName, parsedDate.ToString("yyyy-MM-dd")));
                    }
                }
            }

            // 按日期排序
            result = result.OrderBy(r => r.Date).ToList();

            return result;
        }

        public static string TaskNameRender (string taskName, bool enable , bool success)
        {
            if (enable && !success)
            {
                return $"<span style='color:red;'>{taskName}</span>";
            }
            return taskName;
        }

        public static string ConvertSecondsToTime(double totalSeconds)
        {
            if (totalSeconds < 0)
                throw new ArgumentException("Seconds cannot be negative.");

            int hours = (int)(totalSeconds / 3600);
            int minutes = (int)((totalSeconds % 3600) / 60);
            double seconds = totalSeconds % 60;

            string result = "";
            if (hours > 0)
            {
                result += $"{Lang.S["GameTask_11802_582b30"]};
            }

            if (minutes > 0 || hours > 0)
            {
                result += $"{Lang.S["GameTask_11801_88c82a"]};
            }

            if (seconds > 0 || (hours == 0 && minutes == 0))
            {
                // 根据小数点后是否为0决定是否保留小数
                if (seconds % 1 == 0)
                {
                    result += $"{Lang.S["GameTask_11800_155bb6"]};
                }
                else
                {
                    result += $"{Lang.S["GameTask_11799_74e283"]}; // 保留两位小数
                }
            }

            return result;
        }

        // 根据时间获取对应的“自定义天”，即以凌晨 4 点为分组的开始
        static DateTime GetCustomDay(string timeStr)
        {
            // 解析字符串为 DateTime 对象
            DateTime time = DateTime.ParseExact(timeStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            // 获取当天的午夜 00:00 时间
            DateTime midnight = time.Date;

            // 计算自定义“天”的起始时间（午夜时间 + 4小时）
            DateTime customDayStart = midnight.AddHours(4);

            // 如果当前时间早于自定义天的起始时间，则属于前一天
            if (time < customDayStart)
            {
                customDayStart = customDayStart.AddDays(-1);
            }

            return customDayStart;
        }

        public static string FormatNumberWithStyle(int a, int b = 3)
        {
            if (a == 0)
            {
                return "";
            }

            // Determine the style based on the condition
            string colorStyle = a >= b ? "color:red;" : string.Empty;

            // Return the formatted HTML string
            return $"<span style=\"font-weight:bold;{colorStyle}\">{a}</span>";
        }

        public static string GetNumberOrEmptyString(int number)
        {
            // 如果数字为0，返回空字符串，否则返回数字的字符串形式
            return number == 0 ? string.Empty : number.ToString();
        }

        public static string SubtractFiveSeconds(string inputTime, int seconds)
        {
            try
            {
                // 将输入的字符串解析为 DateTime
                DateTime parsedTime = DateTime.ParseExact(inputTime, "yyyy-MM-dd HH:mm:ss", null);

                // 减去 5 秒
                DateTime resultTime = parsedTime.AddSeconds(-seconds);

                // 转换回指定格式的字符串并返回
                return resultTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (FormatException)
            {
                return "Invalid input time format. Please use 'yyyy-MM-dd HH:mm:ss'.";
            }
        }

        public static string GenerHtmlByConfigGroupEntity(
            List<ConfigGroupEntity> configGroups,
            GameInfo? gameInfo,
            LogParseConfig.ScriptGroupLogParseConfig scriptGroupLogParseConfig)
        {
            //移除空的记录
            configGroups.RemoveAll(group => group.ConfigTaskList == null || group.ConfigTaskList.Count == 0);
            if (scriptGroupLogParseConfig.MergerStatsSwitch)
            {
                configGroups = new ConfigGroupMerger().MergeConfigGroups(configGroups);
                configGroups.Reverse();
            }
            
            (string name, Func<ConfigTask, string> value, string sortType)[] colConfigs =
            [
                (name: Lang.S["GameTask_11798_78caf7"], value: task => TaskNameRender(Path.GetFileNameWithoutExtension(task.Name),scriptGroupLogParseConfig.FaultStatsSwitch,task.Fault.PathingSuccessEnd), sortType: "string"),
                (name: Lang.S["GameTask_11797_592c59"], value: task => (task.IsMerger?"<span style='color:blue'>":"")+ (task.StartDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")+(task.IsMerger?"</span>":""), sortType: "date"),
                (name: Lang.S["GameTask_11796_f78277"], value: task => task.EndDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", sortType: "date"),
                (name: Lang.S["GameTask_11795_92df86"], value: task => ConvertSecondsToTime((task.EndDate - task.StartDate)?.TotalSeconds ?? 0),
                    sortType: "number")
            ];
            List<(string name, Func<ConfigTask, string> value, string sortType)>
                colConfigList = new();
            colConfigList.AddRange(colConfigs);
            if (scriptGroupLogParseConfig.FaultStatsSwitch)
            {
                colConfigList.Add((name: Lang.S["GameTask_11794_e4937f"], value: task => FormatNumberWithStyle(task.Fault.ReviveCount),
                    sortType: "number"));
                colConfigList.Add((name: Lang.S["GameTask_11793_d5f413"], value: task => FormatNumberWithStyle(task.Fault.RetryCount),
                    sortType: "number"));
                colConfigList.Add((name: Lang.S["GameTask_11792_13f050"], value: task => FormatNumberWithStyle(task.Fault.StuckCount),
                    sortType: "number"));
                colConfigList.Add((name: Lang.S["GameTask_11791_c737c1"], value: task => FormatNumberWithStyle(task.Fault.BattleTimeoutCount),
                    sortType: "number"));
                colConfigList.Add((name: Lang.S["GameTask_11790_7dee21"], value: task => FormatNumberWithStyle(task.Fault.TeleportFailCount),
                    sortType: "number"));
                colConfigList.Add((name: Lang.S["GameTask_11789_452034"], value: task => FormatNumberWithStyle(task.Fault.ErrCount),
                    sortType: "number"));
            }


            var msColConfigs = new (string name, Func<MoraStatistics, string> value, string sortType)[]
            {
                (Lang.S["GameTask_11788_4ff1e7"], ms => ms.Name, "date"),
                (Lang.S["GameTask_11787_434fd7"], ms => GetNumberOrEmptyString(ms.SmallMonsterStatistics), "number"),
                (Lang.S["GameTask_11779_6e49d7"], ms => ms.SmallMonsterDetails, "string"),
                (Lang.S["GameTask_11786_984e39"], ms => ms.LastSmallTime, "date"),
                (Lang.S["GameTask_11785_373ddb"], ms => GetNumberOrEmptyString(ms.EliteGameStatistics), "number"),
                (Lang.S["GameTask_11777_373386"], ms => ms.EliteDetails, "string"),
                (Lang.S["GameTask_11784_a20c17"], ms => ms.LastEliteTime, "date"),
                (Lang.S["GameTask_11783_a0c8e5"], ms => ms.TotalMoraKillingMonstersMora.ToString(), "number"),
                (Lang.S["GameTask_11782_4994b1"], ms => ms.EmergencyBonus, "number"),
                (Lang.S["GameTask_11781_2810be"], ms => ms.ChestReward, "number")
            };

            //锄地部分新曾字段
            var col2Configs = new (string name, Func<MoraStatistics, string> value, string sortType)[]
            {
                (Lang.S["GameTask_11780_fbd6a7"], ms => GetNumberOrEmptyString(ms.SmallMonsterStatistics), "number"),
                (Lang.S["GameTask_11779_6e49d7"], ms => ms.SmallMonsterDetails, "string"),
                (Lang.S["GameTask_11778_805ca5"], ms => GetNumberOrEmptyString(ms.EliteGameStatistics), "number"),
                (Lang.S["GameTask_11777_373386"], ms => ms.EliteDetails, "string"),
                (Lang.S["GameTask_11776_545b66"], ms => ms.TotalMoraKillingMonstersMora.ToString(), "number"),
                (
                    name: Lang.S["GameTask_11775_5be2aa"],
                    value: ms => (ms.TotalMoraKillingMonstersMora /
                            (ms.StatisticsEnd - ms.StatisticsStart)?.TotalSeconds ?? 0)
                        .ToString("F2"),
                    sortType: "number"
                )
            };
            
            
            
            StringBuilder html = new StringBuilder();
            //从文件解析札记数据
            NotifyHtmlGenerationStatus(Lang.S["GameTask_11774_32a250"]);
            List<ActionItem> actionItems = new();
            if (gameInfo != null)
            {
                actionItems = TravelsDiaryDetailManager.loadAllActionItems(gameInfo, configGroups);
                int hoeingDelay;
                if (int.TryParse(scriptGroupLogParseConfig.HoeingDelay, out hoeingDelay))
                {
                    foreach (var actionItem in actionItems)
                    {
                        actionItem.Time = SubtractFiveSeconds(actionItem.Time, hoeingDelay);
                    }
                }
                
                //  FarmingPlanData
                
            }
            NotifyHtmlGenerationStatus(Lang.S["GameTask_11773_7ae6c5"]);
            string htmlContent = GenerHtmlByConfigGroupEntity(configGroups, Lang.S["Script_014_409074"], colConfigList.ToArray(),
                col2Configs, actionItems,
                msColConfigs,scriptGroupLogParseConfig);

            // 检查HTML内容大小，如果超过阈值则保存为文件
            const int maxHtmlSize = 1 * 1024 * 1024; // 1MB 阈值，可以根据实际情况调整
            if (htmlContent.Length > maxHtmlSize)
            {
                NotifyHtmlGenerationStatus($"{Lang.S["GameTask_11772_03dddb"]});
                return SaveHtmlToTempFile(htmlContent);
            }
            NotifyHtmlGenerationStatus(Lang.S["GameTask_11771_2e5c75"]);
            return htmlContent;
        }

        private static string SaveHtmlToTempFile(string htmlContent)
        {
            try
            {
                // 创建临时文件夹（如果不存在）
                string tempFolder = Global.Absolute(@"log\logparse\");
                if (!Directory.Exists(tempFolder))
                {
                    Directory.CreateDirectory(tempFolder);
                }

                // 创建唯一的文件名
                string fileName = $"LogAnalysis_{DateTime.Now:yyyyMMdd_HHmmss}.html";
                string filePath = Path.Combine(tempFolder, fileName);

                // 写入HTML内容
                File.WriteAllText(filePath, htmlContent, Encoding.UTF8);
                NotifyHtmlGenerationStatus($"{Lang.S["GameTask_11770_14a6a1"]});
                // 返回文件的URI
                return new Uri(filePath).AbsoluteUri;
            }
            catch (Exception ex)
            {
                NotifyHtmlGenerationStatus($"{Lang.S["GameTask_11769_355a84"]});
                Console.WriteLine($"{Lang.S["GameTask_11769_355a84"]});
                return htmlContent; // 如果保存失败，返回原始HTML内容
            }
        }

        public static string ConcatenateStrings(string a, string b)
        {
            if (string.IsNullOrEmpty(b) || b == "0")
            {
                return "";
            }

            return a + b;
        }

        public static string LogAssertsFileContent(string fileName)
        {
            string filepath = Path.Combine(_assets_dir, fileName);
            return File.ReadAllText(filepath);
        }

        public static string GenerHtmlByConfigGroupEntity(
            List<ConfigGroupEntity> configGroups,
            string title,
            (string name, Func<ConfigTask, string> value, string sortType)[] colConfigs,
            (string name, Func<MoraStatistics, string> value, string sortType)[] col2Configs,
            List<ActionItem> actionItems,
            (string name, Func<MoraStatistics, string> value, string sortType)[] msColConfigs
            ,LogParseConfig.ScriptGroupLogParseConfig scriptGroupLogParseConfig
            )
        {
            StringBuilder html = new StringBuilder();

            // HTML头部
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine($"    <title>{title}</title>");
            html.AppendLine("    <style>");
            html.AppendLine(LogAssertsFileContent("log.css"));
            html.AppendLine("    </style>");
            html.AppendLine("    <script>");
            html.AppendLine(LogAssertsFileContent("log.js"));
            html.AppendLine("    </script>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            if (scriptGroupLogParseConfig.GenerateFarmingPlanData)
            {
                DailyFarmingData dailyData =  FarmingStatsRecorder.ReadDailyFarmingData();
                var ft = dailyData.getFinalTotalMobCount();
                var cap = dailyData.getFinalCap();
                // 保存更新后的数据
                html.AppendLine($"{Lang.S["GameTask_11768_6deabd"]} +
                                $"{Lang.S["GameTask_11711_404c96"]}+(dailyData.EnableMiyousheStats()?"(合并米游社数据)":""));
            }
            
            // 修改 colspan 计算逻辑
            int colspan = colConfigs.Length;

            if (actionItems.Count > 0)
            {
                colspan = colspan + col2Configs.Length;
                // 按时间分组，考虑每天凌晨4点为新的一天
                var groupedByCustomDay = actionItems.GroupBy(item => GetCustomDay(item.Time))
                    .OrderBy(group => group.Key)
                    .Reverse().ToList();

                html.AppendLine(Lang.S["GameTask_11767_b456ba"]);
                html.AppendLine("<div class=\"sticky-table\">");
                html.AppendLine("<table>");
                html.AppendLine("    <thead>");
                html.AppendLine("    <tr class=\"sticky-header\">");
                foreach (var item in msColConfigs)
                {
                    html.AppendLine($"        <th data-sort-type=\"{item.sortType}\">{item.name}</th>");
                }
                html.AppendLine("    </tr>");
                html.AppendLine("    </tr>");
                html.AppendLine("</thead>");
                html.AppendLine("<tbody>");

                foreach (var group in groupedByCustomDay)
                {
                    //按天统计
                    MoraStatistics ms = new MoraStatistics();
                    ms.Name = group.Key.ToString("yyyy-MM-dd");
                    ms.ActionItems.AddRange(group.ToList());
                    html.AppendLine("    <tr>");
                    foreach (var item in msColConfigs)
                    {
                        html.AppendLine($"        <td >{item.value.Invoke(ms)}</td>");
                    }

                    html.AppendLine("    </tr>");
                }

                html.AppendLine("</tbody>");
                html.AppendLine("</table>");
                html.AppendLine("</div>");
            }

            MoraStatistics allms = new MoraStatistics();
            allms.ActionItems.AddRange(actionItems);

            
            int groupIndex = 0;
            // 遍历每个配置组生成表格
            foreach (var group in configGroups)
            {
                List<Dictionary<string, object>> farmingPlanJsonList = new();
                groupIndex++;
                TimeSpan? timeDiff = group.EndDate - group.StartDate;
                double totalSeconds = timeDiff?.TotalSeconds ?? 0;
                MoraStatistics groupms = allms.GetFilterMoraStatistics(item =>
                    {
                        DateTime dt = DateTime.Parse(item.Time);
                        if (dt >= group.StartDate && dt <= group.EndDate)
                        {
                            return true;
                        }

                        return false;
                    }
                );
                groupms.StatisticsStart = group.StartDate;
                groupms.StatisticsEnd = group.EndDate;
                html.AppendLine($"{Lang.S["GameTask_11766_76a8c2"]});
                html.AppendLine(
                    $"<h3>{group.StartDate?.ToString("yyyy-MM-dd HH:mm:ss")}-{group.EndDate?.ToString("yyyy-MM-dd HH:mm:ss")}</h3>");
                html.AppendLine($"{Lang.S["GameTask_11765_5a1508"]});
                html.AppendLine("<div class=\"sticky-table\">");
                html.AppendLine("<table>");
                html.AppendLine("    <thead>");
                html.AppendLine("    <tr class=\"sticky-header\">");
                foreach (var col in colConfigs)
                {
                    html.AppendLine($"        <th data-sort-type=\"{col.sortType}\">{col.name}</th>");
                }
                if (actionItems.Count > 0)
                {
                    foreach (var col in col2Configs)
                    {
                        html.AppendLine($"        <th data-sort-type=\"{col.sortType}\">{col.name}</th>");
                    }
                }
                html.AppendLine("    </tr>");
                html.AppendLine("    </thead>");
                html.AppendLine("    <tbody>");

                // 合并所有任务的 Picks
                Dictionary<string, int> mergedPicks = new Dictionary<string, int>();
                foreach (var task in group.ConfigTaskList)
                {
                    foreach (var pick in task.Picks)
                    {
                        if (!mergedPicks.ContainsKey(pick.Key))
                        {
                            mergedPicks[pick.Key] = 0;
                        }

                        mergedPicks[pick.Key] += pick.Value;
                    }

                    // 任务行
                    timeDiff = task.EndDate - task.StartDate;
                    totalSeconds = timeDiff?.TotalSeconds ?? 0;
                    html.AppendLine("    <tr>");

                    // 修改第一列（名称列）的样式
                    for (int i = 0; i < colConfigs.Length; i++)
                    {
                        var item = colConfigs[i];
                        if (i == 0) // 名称列
                        {
                            html.AppendLine(
                                $"        <td class=\"main-row-name\" rowspan=\"2\">{item.value.Invoke(task)}</td>");
                        }
                        else
                        {
                            html.AppendLine($"        <td>{item.value.Invoke(task)}</td>");
                        }
                    }

                    if (actionItems.Count > 0)
                    {
                        MoraStatistics configTaskMs = groupms.GetFilterMoraStatistics(item =>
                            {
                                DateTime dt = DateTime.Parse(item.Time);
                                if (dt >= task.StartDate && dt <= task.EndDate)
                                {
                                    return true;
                                }

                                return false;
                            }
                        );
                        configTaskMs.StatisticsStart = task.StartDate;
                        configTaskMs.StatisticsEnd = task.EndDate;

                        if (configTaskMs.ActionItems.Count >0)
                        {
                            //farmingPlanJsonList
                            // 构建配置
                            var fp = new Dictionary<string, object>
                            {
                                ["name"] = task.Name.Replace(".json",""),
                                ["cover"] = new Dictionary<string, object>
                                {
                                    ["info"] = new Dictionary<string, object>(),
                                    ["farming_info"] = new Dictionary<string, object>
                                    {
                                        ["normal_mob_count"] = configTaskMs.SmallMonsterStatistics,
                                        ["elite_mob_count"] = configTaskMs.EliteGameStatistics,
                                        ["duration_seconds"] = timeDiff.HasValue ? timeDiff.Value.TotalSeconds : 0,
                                        ["elite_details"] = configTaskMs.EliteDetails,
                                        ["total_mora"] = configTaskMs.TotalMoraKillingMonstersMora
                                    }
                                } };
                            if (configTaskMs.EliteGameStatistics == 0)
                            {
                                //纯小怪给与区分怪物标志
                                ((Dictionary<string, object>)((Dictionary<string, object>)fp["cover"])["info"])
                                    ["enable_monster_loot_split"] = true;
                            }
                            farmingPlanJsonList.Add(fp);
                        }
                        
    
                        foreach (var item in col2Configs)
                        {
                            html.AppendLine($"        <td>{item.value.Invoke(configTaskMs)}</td>");
                        }
                    }

                    html.AppendLine("    </tr>");

                    // 添加附属行显示该任务的拾取物
                    var taskSortedPicks = task.Picks.OrderByDescending(p => p.Value)
                        .Select(p => $"{p.Key} ({p.Value})");

                    html.AppendLine("    <tr class=\"sub-row\">");
                    // 跳过第一列，因为已经在主行中使用了rowspan="2"
                    // 计算实际的 colspan 值
                    int actualColspan = colConfigs.Length - 1;
                    if (actionItems.Count > 0)
                    {
                        actualColspan += col2Configs.Length;
                    }

                    html.AppendLine(
                        $"        <td colspan=\"{actualColspan}\Lang.S["GameTask_11764_8c0456"], ", taskSortedPicks)}</td>");
                    html.AppendLine("    </tr>");
                }

                // 按 Value 倒序排列 Picks
                var sortedPicks = mergedPicks.OrderByDescending(p => p.Value)
                    .Select(p => $"{p.Key} ({p.Value})");

                // 修改拾取物行添加 ignore-sort 类
                html.AppendLine("    <tr class=\"ignore-sort\">");
                html.AppendLine($"        <td colspan=\"{colspan}\Lang.S["GameTask_11764_8c0456"], ", sortedPicks)}</td>");
                html.AppendLine("    </tr>");

                if (actionItems.Count > 0)
                {
                    html.AppendLine("    <tr class=\"ignore-sort\">");
                    html.AppendLine(
                        $"        <td colspan=\"{colspan}\Lang.S["GameTask_11763_85a571"]小怪：", groupms.SmallMonsterStatistics.ToString()) +
                                                                  /*ConcatenateStrings(",最后一只小怪挂于", groupms.LastSmallTime) +*/
                                                                  ConcatenateStrings(Lang.S["GameTask_11762_4514b9"], groupms.EliteGameStatistics.ToString()) +
                                                                  ConcatenateStrings(Lang.S["GameTask_11761_3588d3"], groupms.EliteDetails) +
                                                                  /*ConcatenateStrings(",最后一只精英挂于", groupms.LastEliteTime) +*/
                                                                  ConcatenateStrings(Lang.S["GameTask_11760_5b0e15"], groupms.TotalMoraKillingMonstersMora.ToString()) +
                                                                  ConcatenateStrings(Lang.S["GameTask_11759_2e5f5b"], (groupms.TotalMoraKillingMonstersMora / (groupms.StatisticsEnd - groupms.StatisticsStart)?.TotalSeconds ?? 0).ToString("F2"))}");
                    html.AppendLine("    </tr>");

                }
                if (farmingPlanJsonList.Count > 0)
                {
                    html.AppendLine("    <tr class=\"ignore-sort\">");

                    html.AppendLine(
                        $"        <td colspan=\"{colspan}\Lang.S["GameTask_11756_253bb5"]togglePre('farmingPlan{groupIndex}', this)\">显示 JSON</button><button id=\"copyBtn\" onclick=\"copyPreContent('farmingPlan{groupIndex}')\">复制到剪贴板</button>\n<pre style=\"display:none;\" id=\"farmingPlan{groupIndex}\">");
                    var controlMap = new Dictionary<string, object>
                    {
                        ["global_cover"] = new Dictionary<string, object>
                        {
                            ["farming_info"] = new Dictionary<string, object>
                            {
                                ["allow_farming_count"] = true,
                                ["primary_target"] = ""
                            }
                        },
                        ["json_list"] = farmingPlanJsonList
                    };
                    html.AppendLine(JsonConvert.SerializeObject(controlMap, Formatting.Indented));

                    html.AppendLine(" </pre> </div></tr>");     
                }


                html.AppendLine("</table>");
                html.AppendLine("</div>"); // 关闭 sticky-table div
            }

            // HTML尾部
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }



        public static void WriteConfigFile(LogParseConfig config)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true // 启用格式化（缩进）
            };
            var content = JsonSerializer.Serialize(config, options);
            string directoryPath = Path.GetDirectoryName(_configPath);

            if (!Directory.Exists(directoryPath))
            {
                // 如果文件夹不存在，创建文件夹
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(_configPath, content);
        }

        public static LogParseConfig LoadConfig()
        {
            LogParseConfig? config = null;
            if (File.Exists(_configPath))
            {
                try
                {
                    config = JsonSerializer.Deserialize<LogParseConfig>(File.ReadAllText(_configPath)) ??
                             throw new NullReferenceException();
                }
                catch (NullReferenceException)
                {
                    Toast.Warning(Lang.S["GameTask_11755_21baab"]);
                    config = new LogParseConfig();
                }
            }
            else
            {
                config = new LogParseConfig();
            }

            return config;
        }
    }
}