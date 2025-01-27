using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Config;
using Wpf.Ui.Violeta.Controls;
using static LogParse.LogParse.ConfigGroupEntity;

namespace LogParse
{
    public class LogParse
    {
        public static List<string> SafeReadAllLines(string filePath)
        {
            var lines = new List<string>();
            try
            {
                // 使用 FileStream 和 StreamReader，允许共享读取
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fileStream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"无法读取文件 {filePath}: {ex.Message}");
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
            ConfigGroupEntity configGroupEntity = null;
            ConfigTask configTask = null;
            for (int i = 0; i < logLines.Count; i++)
            {
                var logstr = logLines[i].Item1;
                var logrq = logLines[i].Item2;
                //if("配置组 \"${}\" 加载完成，共25个脚本，开始执行")


                // 定义正则表达式

                var result = parseBgiLine(@"配置组 ""(.+?)"" 加载完成，共(\d+)个脚本", logstr);
                if (result.Item1)
                {
                    configGroupEntity = new();
                    configGroupEntity.Name = result.Item2[1];
                    configGroupEntity.StartDate = parsePreDataTime(logLines, i - 1, logrq);
                    configGroupEntities.Add(configGroupEntity);
                }

                if (configGroupEntity != null)
                {
                    //配置组 "战斗" 执行结束
                    result = parseBgiLine($"配置组 \"{configGroupEntity.Name}\" 执行结束", logstr);
                    if (result.Item1)
                    {
                        configGroupEntity.EndDate = parsePreDataTime(logLines, i - 1, logrq);
                        configGroupEntity = null;
                    }
                }


                if (configGroupEntity != null)
                {
                    result = parseBgiLine(@"→ 开始执行路径追踪任务: ""(.+?)""", logstr);
                    if (result.Item1)
                    {
                        configTask = new();
                        configTask.Name = result.Item2[1];
                        configTask.StartDate = parsePreDataTime(logLines, i - 1, logrq);
                        configGroupEntity.ConfigTaskList.Add(configTask);
                    }

                    if (configTask != null)
                    {

                        //前往七天神像复活
                        if (logstr.EndsWith("前往七天神像复活"))
                        {
                            configTask.Fault.ReviveCount++;
                        }
                        //传送失败，重试 n 次
                        result = parseBgiLine($@"传送失败，重试 (\d+) 次", logstr);
                        if (result.Item1)
                        {
                            configTask.Fault.TeleportFailCount = int.Parse(result.Item2[1]);
               
                        }
                        //战斗超时结束
                        if (logstr == "战斗超时结束")
                        {
                            configTask.Fault.BattleTimeoutCount ++;
                        }                
                        
                        //重试一次路线或放弃此路线！
                        if (logstr.EndsWith("重试一次路线或放弃此路线！"))
                        {
                            configTask.Fault.RetryCount++;
                        }
                       
                        
                        
                        if (logstr.StartsWith("→ 脚本执行结束: \"" + configTask.Name + "\""))
                        {
                            configTask.EndDate = parsePreDataTime(logLines, i - 1, logrq);
                            configTask = null;
                        }

                        result = parseBgiLine(@"交互或拾取：""(.+?)""", logstr);
                        if (result.Item1)
                        {
                            configTask.addPick(result.Item2[1]);
                        }
                    }
                }


            }

            //无论如何给个结束时间
            if (configGroupEntity != null && configGroupEntity.EndDate == null)
            {
                if ( configGroupEntity.ConfigTaskList.Count>0)
                {
                    ConfigTask ct = configGroupEntity.ConfigTaskList[^1];
                    if (ct != null)
                    {
                        configGroupEntity.EndDate = ct.EndDate;
                        if (configGroupEntity.EndDate == null)
                        {
                            configGroupEntity.EndDate = ct.StartDate;
                        }
                    }
                }

            }

            return configGroupEntities;
        }

        private static (bool, List<string>) parseBgiLine(string pattern, string str)
        {
            Match match = Regex.Match(str, pattern);
            if (match.Success)
            {
                return (true, match.Groups.Cast<Group>().Select(g => g.Value).ToList());
            }

            return (false, []);
        }

        private static DateTime? parsePreDataTime(List<(string, string)> list, int index, string logrq)
        {
            if (index < 0)
            {
                return null;
            }

            (bool, List<string>) result = parseBgiLine(@"\[(\d{2}:\d{2}:\d{2})\.\d+\]", list[index].Item1);
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
            public List<ConfigTask> ConfigTaskList { get; } = new();



            public class ConfigTask
            {
                public string Name { get; set; }

                //开始日期
                public DateTime? StartDate { get; set; }

                //结束日期
                public DateTime? EndDate { get; set; }

                //拾取字典
                public Dictionary<string, int> Picks { get; } = new();

                public void addPick(string val)
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
                    //复活次数
                    public int ReviveCount { get; set; } = 0;
                    //传送失败次数
                    public int TeleportFailCount { get; set; } = 0;
                    //重试次数
                    public int RetryCount { get; set; } = 0;
                    //战斗超时
                    public int BattleTimeoutCount { get; set; } = 0;
                
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
                Console.WriteLine("指定的文件夹不存在。");
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
                result += $"{hours}小时";
            }

            if (minutes > 0 || hours > 0)
            {
                result += $"{minutes}分钟";
            }

            if (seconds > 0 || (hours == 0 && minutes == 0))
            {
                // 根据小数点后是否为0决定是否保留小数
                if (seconds % 1 == 0)
                {
                    result += $"{(int)seconds}秒";
                }
                else
                {
                    result += $"{seconds:F2}秒"; // 保留两位小数
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

        public static string FormatNumberWithStyle(int a, int b=3)
        {
            if (a== 0)
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
        public static string SubtractFiveSeconds(string inputTime,int seconds)
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
        public static string GenerHtmlByConfigGroupEntity(List<ConfigGroupEntity> configGroups, GameInfo gameInfo,LogParseConfig.ScriptGroupLogParseConfig scriptGroupLogParseConfig)
        {
            (string name, Func<ConfigTask, string> value)[] colConfigs =
            [
                (name: "名称", value: task => task.Name),
                (name: "开始日期", value: task => task.StartDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""),
                (name: "结束日期", value: task => task.EndDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""),
                (name: "耗时", value: task => ConvertSecondsToTime((task.EndDate - task.StartDate)?.TotalSeconds ?? 0))
            ];
            List<(string name, Func<ConfigTask, string> value)> colConfigList = new();
            colConfigList.AddRange(colConfigs);
            if (scriptGroupLogParseConfig.FaultStatsSwitch)
            {
                colConfigList.Add((name: "复活次数", value: task => FormatNumberWithStyle(task.Fault.ReviveCount)));
                colConfigList.Add((name: "重试次数", value: task => FormatNumberWithStyle(task.Fault.RetryCount)));
                colConfigList.Add((name: "战斗超时次数", value: task => FormatNumberWithStyle(task.Fault.BattleTimeoutCount)));
                colConfigList.Add((name: "传送失败次数", value: task => FormatNumberWithStyle(task.Fault.TeleportFailCount)));
            }

            
            (string name, Func<MoraStatistics, string> value)[] msColConfigs =
            [
                (name: "日期", value: ms => ms.Name), (name: "小怪", value: ms => GetNumberOrEmptyString(ms.SmallMonsterStatistics)),
                (name: "最后小怪日期", value: ms => ms.LastSmallTime),
                (name: "精英", value: ms => GetNumberOrEmptyString(ms.EliteGameStatistics)),
                (name: "精英详细", value: ms => ms.EliteDetails), (name: "最后精英日期", value: ms => ms.LastEliteTime),
                (name: "总计锄地摩拉", value: ms => ms.TotalMoraKillingMonstersMora.ToString()),
                (name: "突发事件获取摩拉", value: ms => ms.EmergencyBonus)
            ];
            //锄地部分新曾字段
            (string name, Func<MoraStatistics, string> value)[] col2Configs=[..msColConfigs.ToList().Where(item=>item.name!="日期" && item.name!="最后小怪日期" && item.name!="最后精英日期" && item.name!="突发事件获取摩拉"),
                (name: "摩拉（每秒）", value: ms => (ms.TotalMoraKillingMonstersMora/(ms.StatisticsEnd-ms.StatisticsStart)?.TotalSeconds ?? 0).ToString("F2")),
            ];
                
                
                
            StringBuilder html = new StringBuilder();
            //从文件解析札记数据
            List<ActionItem> actionItems = new();
            if (gameInfo != null)
            {
                actionItems = TravelsDiaryDetailManager.loadAllActionItems(gameInfo, configGroups);
                int hoeingDelay;
                if (int.TryParse(scriptGroupLogParseConfig.HoeingDelay, out hoeingDelay))
                {
                    foreach (var actionItem in actionItems)
                    {
                        actionItem.Time = SubtractFiveSeconds(actionItem.Time,hoeingDelay);
                    }
                }
                
            }

            return GenerHtmlByConfigGroupEntity(configGroups, "日志分析", colConfigList.ToArray(),col2Configs, actionItems, msColConfigs);
        }
        public static string ConcatenateStrings(string a, string b)
        {
            if (string.IsNullOrEmpty(b) || b == "0")
            {
                return "";
            }
            return a + b;
        }
        public static string GenerHtmlByConfigGroupEntity(List<ConfigGroupEntity> configGroups, string title,
            (string name, Func<ConfigTask, string> value)[] colConfigs,
            (string name, Func<MoraStatistics, string> value)[] col2Configs,
            List<ActionItem> actionItems,
            (string name, Func<MoraStatistics, string> value)[] msColConfigs)
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
            html.AppendLine("        table { border-collapse: collapse; width: 100%; margin-bottom: 20px; }");
            html.AppendLine("        th, td { border: 1px solid black; padding: 8px; text-align: left; }");
            html.AppendLine("        th { background-color: #f2f2f2; }");
            html.AppendLine("    tr:nth-child(odd) {   background-color: #eaeaea; /* 奇数行颜色 */    }");
            html.AppendLine("    tr:nth-child(even) { background-color: #f9f9f9; /* 偶数行颜色 */}");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            int colspan = colConfigs.Length;

            if (actionItems.Count > 0)
            {
                
                colspan = colspan +col2Configs.Length;
                // 按时间分组，考虑每天凌晨4点为新的一天
                var groupedByCustomDay = actionItems.GroupBy(item => GetCustomDay(item.Time))
                    .OrderBy(group => group.Key)
                    .Reverse().ToList();

                html.AppendLine($"<h2>按日摩拉收益统计</h2>");
                html.AppendLine("<table>");
                
                html.AppendLine("    <tr>");
                foreach (var item in msColConfigs)
                {
                    html.AppendLine($"        <th>{item.name}</th>");
                }

                html.AppendLine("    </tr>");


                foreach (var group in groupedByCustomDay)
                {
                    //按天统计
                    MoraStatistics ms = new MoraStatistics();
                    ms.Name = group.Key.ToString("D");
                    ms.ActionItems.AddRange(group.ToList());
                    html.AppendLine("    <tr>");
                    foreach (var item in msColConfigs)
                    {
                        html.AppendLine($"        <td >{item.value.Invoke(ms)}</td>");
                    }

                    html.AppendLine("    </tr>");
                }
                html.AppendLine("</table>");
            }

            MoraStatistics allms = new MoraStatistics();
            allms.ActionItems.AddRange(actionItems);
         
            
            // 遍历每个配置组生成表格
            foreach (var group in configGroups)
            {
                TimeSpan? timeDiff = group.EndDate - group.StartDate;
                double totalSeconds = timeDiff?.TotalSeconds ?? 0;
                MoraStatistics groupms = allms.GetFilterMoraStatistics(item =>
                    {
                        DateTime dt = DateTime.Parse(item.Time);
                        if (dt>=group.StartDate && dt<=group.EndDate)
                        {
                            return true;
                        }
                        return false;
                    }
                );
                groupms.StatisticsStart=group.StartDate;
                groupms.StatisticsEnd=group.EndDate;
                html.AppendLine(
                $"<h2>配置组：{group.Name}({group.StartDate?.ToString("yyyy-MM-dd HH:mm:ss")}-{group.EndDate?.ToString("yyyy-MM-dd HH:mm:ss")})，耗时{ConvertSecondsToTime(totalSeconds)}</h2>");
                
                html.AppendLine("<table>");
                html.AppendLine("    <tr>");
                foreach (var item in colConfigs)
                {
                    html.AppendLine($"        <th>{item.name}</th>");
                }

                if (actionItems.Count > 0)
                {
                    foreach (var item in col2Configs)
                    {
                        html.AppendLine($"        <th>{item.name}</th>");
                    }
                }

                html.AppendLine("    </tr>");
                
                
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
                    foreach (var item in colConfigs)
                    {
                        html.AppendLine($"        <td>{item.value.Invoke(task)}</td>");
                    }
                    if (actionItems.Count > 0)
                    {
                        MoraStatistics configTaskMs = groupms.GetFilterMoraStatistics(item =>
                            {
                                DateTime dt = DateTime.Parse(item.Time);
                                if (dt>=task.StartDate && dt<=task.EndDate)
                                {
                                    return true;
                                }
                                return false;
                            }
                        );
                        configTaskMs.StatisticsStart=task.StartDate;
                        configTaskMs.StatisticsEnd=task.EndDate;
                        foreach (var item in col2Configs)
                        {

                            html.AppendLine($"        <td>{item.value.Invoke(configTaskMs)}</td>");
                        }
                    }
                    html.AppendLine("    </tr>");
                }

                // 按 Value 倒序排列 Picks
                var sortedPicks = mergedPicks.OrderByDescending(p => p.Value)
                    .Select(p => $"{p.Key} ({p.Value})");

                // Picks 行
                html.AppendLine("    <tr>");

                html.AppendLine(
                    $"        <td colspan=\"{colspan}\">拾取物: {string.Join(", ", sortedPicks)}</td>");
                html.AppendLine("    </tr>");

                if (actionItems.Count > 0)
                {
                    //锄地总计
                    html.AppendLine("    <tr>");

                    html.AppendLine(
                        $"        <td colspan=\"{colspan}\">锄地总计:{ ConcatenateStrings("小怪：", groupms.SmallMonsterStatistics.ToString()) +
                                                                   /*ConcatenateStrings(",最后一只小怪挂于", groupms.LastSmallTime) +*/
                                                                   ConcatenateStrings(",精英怪数量：", groupms.EliteGameStatistics.ToString()) +
                                                                   ConcatenateStrings(",精英详细:", groupms.EliteDetails) +
                                                                   /*ConcatenateStrings(",最后一只精英挂于", groupms.LastEliteTime) +*/
                                                                   ConcatenateStrings(",合计锄地摩拉：", groupms.TotalMoraKillingMonstersMora.ToString())+
                                                                   ConcatenateStrings("，每秒摩拉", (groupms.TotalMoraKillingMonstersMora/(groupms.StatisticsEnd-groupms.StatisticsStart)?.TotalSeconds ?? 0).ToString("F2"))}");
                    html.AppendLine("    </tr>");

                }
                
                
                html.AppendLine("</table>");
            }

            // HTML尾部
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }

        private static string configPath = Global.Absolute(@"log\logparse\config.json");

        public static void WriteConfigFile(LogParseConfig config)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true // 启用格式化（缩进）
            };
            var content = JsonSerializer.Serialize(config, options);
            string directoryPath = Path.GetDirectoryName(configPath);

            if (!Directory.Exists(directoryPath))
            {
                // 如果文件夹不存在，创建文件夹
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(configPath, content);
        }

        public static LogParseConfig LoadConfig()
        {
            LogParseConfig config = null;
            if (File.Exists(configPath))
            {
                try
                {
                    config = JsonSerializer.Deserialize<LogParseConfig>(File.ReadAllText(configPath));
                }
                catch (Exception e)
                {
                    Toast.Warning("读取日志分析配置文件失败！");
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