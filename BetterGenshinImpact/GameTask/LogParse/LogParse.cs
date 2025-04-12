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
using static BetterGenshinImpact.GameTask.LogParse.LogParse.ConfigGroupEntity;

namespace BetterGenshinImpact.GameTask.LogParse
{
    public class LogParse
    {
        private static readonly string _configPath = Global.Absolute(@"log\logparse\config.json");
        
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
            ConfigGroupEntity? configGroupEntity = null;
            ConfigTask? configTask = null;
            for (int i = 0; i < logLines.Count; i++)
            {
                var logstr = logLines[i].Item1;
                var logrq = logLines[i].Item2;
                //if("配置组 \"${}\" 加载完成，共25个脚本，开始执行")


                // 定义正则表达式

                var result = ParseBgiLine(@"配置组 ""(.+?)"" 加载完成，共(\d+)个脚本", logstr);
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
                    result = ParseBgiLine($"配置组 \"{configGroupEntity.Name}\" 执行结束", logstr);
                    if (result.Item1)
                    {
                        configGroupEntity.EndDate = ParsePreDataTime(logLines, i - 1, logrq);
                        configGroupEntity = null;
                    }
                }


                if (configGroupEntity != null)
                {
                    result = ParseBgiLine(@"→ 开始执行(?:地图追踪任务|JS脚本): ""(.+?)""", logstr);
                    if (result.Item1)
                    {
                        configTask = new();
                        configTask.Name = result.Item2[1];
                        configTask.StartDate = ParsePreDataTime(logLines, i - 1, logrq);
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
                        result = ParseBgiLine(@"传送失败，重试 (\d+) 次", logstr);
                        if (result.Item1)
                        {
                            configTask.Fault.TeleportFailCount = int.Parse(result.Item2[1]);

                        }

                        //战斗超时结束
                        if (logstr == "战斗超时结束")
                        {
                            configTask.Fault.BattleTimeoutCount++;
                        }

                        //重试一次路线或放弃此路线！
                        if (logstr.EndsWith("重试一次路线或放弃此路线！"))
                        {
                            configTask.Fault.RetryCount++;
                        }

                        //疑似卡死，尝试脱离...
                        if (logstr == "疑似卡死，尝试脱离...")
                        {
                            configTask.Fault.StuckCount++;
                        }

                        //One or more errors occurred
                        result = ParseBgiLine(@"执行脚本时发生异常: ""(.+?)""", logstr);
                        if (result.Item1)
                        {
                            configTask.Fault.ErrCount++;
                        }

                        if (logstr.StartsWith("→ 脚本执行结束: \"" + configTask.Name + "\""))
                        {
                            configTask.EndDate = ParsePreDataTime(logLines, i - 1, logrq);
                            configTask = null;
                        }

                        result = ParseBgiLine(@"交互或拾取：""(.+?)""", logstr);
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
            (string name, Func<ConfigTask, string> value, string sortType)[] colConfigs =
            [
                (name: "任务名称", value: task => Path.GetFileNameWithoutExtension(task.Name), sortType: "string"),
                (name: "开始时间", value: task => task.StartDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", sortType: "date"),
                (name: "结束时间", value: task => task.EndDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", sortType: "date"),
                (name: "任务耗时", value: task => ConvertSecondsToTime((task.EndDate - task.StartDate)?.TotalSeconds ?? 0),
                    sortType: "number")
            ];
            List<(string name, Func<ConfigTask, string> value, string sortType)>
                colConfigList = new();
            colConfigList.AddRange(colConfigs);
            if (scriptGroupLogParseConfig.FaultStatsSwitch)
            {
                colConfigList.Add((name: "复活次数", value: task => FormatNumberWithStyle(task.Fault.ReviveCount),
                    sortType: "number"));
                colConfigList.Add((name: "重试次数", value: task => FormatNumberWithStyle(task.Fault.RetryCount),
                    sortType: "number"));
                colConfigList.Add((name: "疑似卡死次数", value: task => FormatNumberWithStyle(task.Fault.StuckCount),
                    sortType: "number"));
                colConfigList.Add((name: "战斗超时次数", value: task => FormatNumberWithStyle(task.Fault.BattleTimeoutCount),
                    sortType: "number"));
                colConfigList.Add((name: "传送失败次数", value: task => FormatNumberWithStyle(task.Fault.TeleportFailCount),
                    sortType: "number"));
                colConfigList.Add((name: "异常发生次数", value: task => FormatNumberWithStyle(task.Fault.ErrCount),
                    sortType: "number"));
            }


            var msColConfigs = new (string name, Func<MoraStatistics, string> value, string sortType)[]
            {
                ("日期", ms => ms.Name, "date"),
                ("小怪数量", ms => GetNumberOrEmptyString(ms.SmallMonsterStatistics), "number"),
                ("小怪详细(摩拉/10)", ms => ms.SmallMonsterDetails, "string"),
                ("最后小怪时间", ms => ms.LastSmallTime, "date"),
                ("精英数量", ms => GetNumberOrEmptyString(ms.EliteGameStatistics), "number"),
                ("精英详细", ms => ms.EliteDetails, "string"),
                ("最后精英时间", ms => ms.LastEliteTime, "date"),
                ("总计锄地摩拉", ms => ms.TotalMoraKillingMonstersMora.ToString(), "number"),
                ("突发事件获取摩拉", ms => ms.EmergencyBonus, "number")
            };

            //锄地部分新曾字段
            var col2Configs = new (string name, Func<MoraStatistics, string> value, string sortType)[]
            {
                ("小怪", ms => GetNumberOrEmptyString(ms.SmallMonsterStatistics), "number"),
                ("小怪详细(摩拉/10)", ms => ms.SmallMonsterDetails, "string"),
                ("精英", ms => GetNumberOrEmptyString(ms.EliteGameStatistics), "number"),
                ("精英详细", ms => ms.EliteDetails, "string"),
                ("锄地摩拉", ms => ms.TotalMoraKillingMonstersMora.ToString(), "number"),
                (
                    name: "摩拉（每秒）",
                    value: ms => (ms.TotalMoraKillingMonstersMora /
                            (ms.StatisticsEnd - ms.StatisticsStart)?.TotalSeconds ?? 0)
                        .ToString("F2"),
                    sortType: "number"
                )
            };
            
            StringBuilder html = new StringBuilder();
            //从文件解析札记数据
            NotifyHtmlGenerationStatus("正在解析札记数据...");
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
            }
            NotifyHtmlGenerationStatus("正在生成日志分析内容...");
            string htmlContent = GenerHtmlByConfigGroupEntity(configGroups, "日志分析", colConfigList.ToArray(),
                col2Configs, actionItems,
                msColConfigs);

            // 检查HTML内容大小，如果超过阈值则保存为文件
            const int maxHtmlSize = 1 * 1024 * 1024; // 1MB 阈值，可以根据实际情况调整
            if (htmlContent.Length > maxHtmlSize)
            {
                NotifyHtmlGenerationStatus($"日志分析较大({htmlContent.Length / 1024}KB)，正在保存为文件...");
                return SaveHtmlToTempFile(htmlContent);
            }
            NotifyHtmlGenerationStatus("日志分析生成完成！");
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
                NotifyHtmlGenerationStatus($"日志分析文件已保存至: .\\log\\logparse\\{fileName}");
                // 返回文件的URI
                return new Uri(filePath).AbsoluteUri;
            }
            catch (Exception ex)
            {
                NotifyHtmlGenerationStatus($"保存日志分析到临时文件时出错: {ex.Message}");
                Console.WriteLine($"保存日志分析到临时文件时出错: {ex.Message}");
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

        public static string GenerHtmlByConfigGroupEntity(
            List<ConfigGroupEntity> configGroups,
            string title,
            (string name, Func<ConfigTask, string> value, string sortType)[] colConfigs,
            (string name, Func<MoraStatistics, string> value, string sortType)[] col2Configs,
            List<ActionItem> actionItems,
            (string name, Func<MoraStatistics, string> value, string sortType)[] msColConfigs)
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
            html.AppendLine("        body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; padding: 16px; background-color: #fbfaef;}");
            html.AppendLine("        table { border-collapse: separate; border-spacing: 0; width: 100%; margin-bottom: 20px; }");
            html.AppendLine("        th, td { border: 1.5px solid #cce3e5; padding: 8px; text-align: left; }");
            html.AppendLine("        th { background-color: #3f51b5; color: white; font-weight: 500; cursor: pointer; position: relative; text-align: center; vertical-align: middle; }");
            html.AppendLine("        tr:nth-child(odd) { background-color: #f5fbef; }");
            html.AppendLine("        tr:nth-child(even) { background-color: #f2faea; }");
            html.AppendLine("        tr:hover { background-color: #cadbb8; transition: background-color 0.2s ease; }");
            
            // 修改排序指示器样式，确保不影响表头文本对齐
            html.AppendLine("        th::after { content: ''; display: block; position: absolute; right: 10px; top: 50%; transform: translateY(-50%); width: 0; height: 0; opacity: 0; transition: opacity 0.2s ease; }");
            html.AppendLine("        th.sort-asc::after, th.sort-desc::after { opacity: 1; }");
            html.AppendLine("        th.sort-asc::after { border-left: 5px solid transparent; border-right: 5px solid transparent; border-bottom: 5px solid white; }");
            html.AppendLine("        th.sort-desc::after { border-left: 5px solid transparent; border-right: 5px solid transparent; border-top: 5px solid white; }");
            
            // 改进的表格容器和固定表头样式
            html.AppendLine("        .table-container { position: relative; max-height: 80vh; overflow-y: auto; border: 1px solid ##cce3e5; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.05); }");
            html.AppendLine("        th, td { border: 1.5px solid #cce3e5; padding: 8px; text-align: left; }");
            html.AppendLine("        .sticky-header { position: sticky; top: 0; z-index: 100; }");
            html.AppendLine("        .sticky-header th { ");
            html.AppendLine("            position: sticky; ");
            html.AppendLine("            top: 0; ");
            html.AppendLine("            background-color: #59a2ab; ");
            html.AppendLine("            z-index: 100; ");
            html.AppendLine("            border-width: 0; ");
            html.AppendLine("            outline: 1.5px solid #cce3e5; ");
            html.AppendLine("            text-align: center; ");
            html.AppendLine("            vertical-align: middle; ");
            html.AppendLine("        }");
            html.AppendLine("        .sticky-header th:first-child { ");
            html.AppendLine("            border-top-left-radius: 8px; ");
            html.AppendLine("        }");
            html.AppendLine("        .sticky-header th:last-child { ");
            html.AppendLine("            border-top-right-radius: 8px; ");
            html.AppendLine("        }");
            html.AppendLine("        .sticky-header::after {");
            html.AppendLine("            content: '';");
            html.AppendLine("            position: absolute;");
            html.AppendLine("            left: 0;");
            html.AppendLine("            right: 0;");
            html.AppendLine("            top: 0;");
            html.AppendLine("            height: 100%;");
            html.AppendLine("            pointer-events: none;");
            html.AppendLine("            z-index: 99;");
            html.AppendLine("        }");
            html.AppendLine("        tbody tr:first-child td { border-top-color: transparent; }");
            html.AppendLine("        tbody tr:last-child td:first-child { border-bottom-left-radius: 8px; }");
            html.AppendLine("        tbody tr:last-child td:last-child { border-bottom-right-radius: 8px; }");
            html.AppendLine("        .table-container table { margin-bottom: 0; box-shadow: 0 2px 5px rgba(0,0,0,0.05); }");
            html.AppendLine("    </style>");
            html.AppendLine("    <script>");
                        html.AppendLine(@"        
document.addEventListener('DOMContentLoaded', function() {
    document.querySelectorAll('th').forEach(function(th) {
        th.removeAttribute('onclick'); 
        th.addEventListener('click', function() {
            const table = this.closest('table');
            const columnIndex = Array.from(this.parentNode.children).indexOf(this);
            const sortType = this.getAttribute('data-sort-type') || 'string';
            sortTable(table, columnIndex, sortType);
        });
    });
});

function getCellValue(row, columnIndex, sortType) {
    try {
        if (!row || !row.cells || columnIndex >= row.cells.length) {
            return sortType === 'number' || sortType === 'date' ? 0 : '';
        }
        
        const cell = row.cells[columnIndex];
        if (!cell) return sortType === 'number' || sortType === 'date' ? 0 : '';
        
        // 优先使用data-sort属性值
        const sortValue = cell.getAttribute('data-sort');
        if (sortValue !== null) {
            return sortType === 'number' || sortType === 'date' ? parseFloat(sortValue) : sortValue;
        }
        
        const value = cell.textContent ? cell.textContent.trim() : '';
        
        // 根据排序类型转换值
        if (sortType === 'number') {
            // 提取数字部分
            const numMatch = value.match(/[\d\.]+/);
            return numMatch ? parseFloat(numMatch[0]) : 0;
                } else if (sortType === 'date') {
            // 修改日期解析逻辑，优先处理 yyyy-MM-dd 格式
            if (!value) return 0;
            
            // 尝试解析 yyyy-MM-dd 格式
            const dateOnlyMatch = value.match(/^(\d{4})-(\d{2})-(\d{2})$/);
            if (dateOnlyMatch) {
                const year = parseInt(dateOnlyMatch[1]);
                const month = parseInt(dateOnlyMatch[2]) - 1; // 月份从0开始
                const day = parseInt(dateOnlyMatch[3]);
                return new Date(year, month, day).getTime();
            }
            
            // 尝试解析标准日期时间格式 yyyy-MM-dd HH:mm:ss
            const dateTimeMatch = value.match(/(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2})/);
            if (dateTimeMatch) {
                const year = parseInt(dateTimeMatch[1]);
                const month = parseInt(dateTimeMatch[2]) - 1; // 月份从0开始
                const day = parseInt(dateTimeMatch[3]);
                const hour = parseInt(dateTimeMatch[4]);
                const minute = parseInt(dateTimeMatch[5]);
                const second = parseInt(dateTimeMatch[6]);
                return new Date(year, month, day, hour, minute, second).getTime();
            }
            
            // 如果无法解析，尝试直接使用Date构造函数
            return new Date(value).getTime() || 0;
        } else if (sortType === 'time') {
            // 处理时间格式（小时、分钟、秒）
            let seconds = 0;
            if (value.includes('小时')) {
                const hoursMatch = value.match(/(\d+)小时/);
                if (hoursMatch) {
                    seconds += parseInt(hoursMatch[1]) * 3600;
                }
            }
            if (value.includes('分钟')) {
                const minutesMatch = value.match(/(\d+)分钟/);
                if (minutesMatch) {
                    seconds += parseInt(minutesMatch[1]) * 60;
                }
            }
            if (value.includes('秒')) {
                const secondsMatch = value.match(/([\d\.]+)秒/);
                if (secondsMatch) {
                    seconds += parseFloat(secondsMatch[1]);
                }
            }
            return seconds;
        }
        return value;
    } catch (e) {
        console.error('获取单元格值时出错:', e);
        return sortType === 'number' || sortType === 'date' ? 0 : '';
    }
}

function sortTable(table, columnIndex, sortType) {
    let loadingDiv = null;
    let loadingTimer = null;
    
    try {
        if (!table) return;
        const tbody = table.querySelector('tbody');
        if (!tbody) return;
        
        // 创建排序中的提示，但不立即显示
        loadingDiv = document.createElement('div');
        loadingDiv.style.position = 'fixed';
        loadingDiv.style.top = '50%';
        loadingDiv.style.left = '50%';
        loadingDiv.style.transform = 'translate(-50%, -50%)';
        loadingDiv.style.padding = '20px';
        loadingDiv.style.background = 'rgba(0,0,0,0.7)';
        loadingDiv.style.color = 'white';
        loadingDiv.style.borderRadius = '5px';
        loadingDiv.style.zIndex = '1000';
        loadingDiv.textContent = '排序中，请稍候...';
        
        // 设置延迟显示提示，只有排序超过500毫秒才显示
        loadingTimer = setTimeout(function() {
            document.body.appendChild(loadingDiv);
        }, 1000);
        
        // 使用setTimeout让UI有机会更新
        setTimeout(function() {
            try {
                // 保存汇总行
                const summaryRows = Array.from(tbody.querySelectorAll('tr.ignore-sort') || []);
                
                // 获取所有行并创建映射
                const allRows = Array.from(tbody.querySelectorAll('tr') || []);
                if (!allRows.length) {
                    clearTimeout(loadingTimer);
                    if (loadingDiv && loadingDiv.parentNode) {
                        document.body.removeChild(loadingDiv);
                    }
                    return;
                }
                
                // 首先标记所有行
                for (let i = 0; i < allRows.length; i++) {
                    if (allRows[i]) {
                        allRows[i].setAttribute('data-original-index', i.toString());
                    }
                }
                
                // 获取需要排序的行（排除汇总行和子行）
                const rows = [];
                for (let i = 0; i < allRows.length; i++) {
                    const row = allRows[i];
                    if (row && row.classList && 
                        !row.classList.contains('ignore-sort') && 
                        !row.classList.contains('sub-row')) {
                        rows.push(row);
                    }
                }
                
                // 创建行和其对应的附属行的映射
                const rowPairs = [];
                for (let i = 0; i < rows.length; i++) {
                    try {
                        const row = rows[i];
                        if (!row || !row.getAttribute) continue;
                        
                        const originalIndexStr = row.getAttribute('data-original-index');
                        if (!originalIndexStr) continue;
                        
                        const originalIndex = parseInt(originalIndexStr);
                        if (isNaN(originalIndex)) continue;
                        
                        // 安全地获取下一行，确保它存在
                        let nextRow = null;
                        if (originalIndex + 1 < allRows.length) {
                            nextRow = allRows[originalIndex + 1];
                        }
                        
                        // 安全地检查nextRow是否存在且是否有classList属性
                        if (nextRow && nextRow.classList && 
                            typeof nextRow.classList.contains === 'function' && 
                            nextRow.classList.contains('sub-row')) {
                            rowPairs.push({main: row, sub: nextRow});
                        } else {
                            rowPairs.push({main: row, sub: null});
                        }
                    } catch (e) {
                        console.error('创建行对时出错:', e);
                        continue;
                    }
                }
        
                // 确定排序方向
                let sortDirection = 'asc';
                const headerCells = table.querySelectorAll('th');
                if (!headerCells || columnIndex >= headerCells.length) {
                    if (loadingDiv && loadingDiv.parentNode) {
                        document.body.removeChild(loadingDiv);
                    }
                    return;
                }
                
                const headerCell = headerCells[columnIndex];
                if (!headerCell || !headerCell.classList) {
                    if (loadingDiv && loadingDiv.parentNode) {
                        document.body.removeChild(loadingDiv);
                    }
                    return;
                }
                
                // 如果已经按这列排序，则切换方向
                if (headerCell.classList.contains('sort-asc')) {
                    sortDirection = 'desc';
                } else if (headerCell.classList.contains('sort-desc')) {
                    sortDirection = 'asc';
                }
                
                // 清除所有表头的排序指示器
                for (let i = 0; i < headerCells.length; i++) {
                    const th = headerCells[i];
                    if (th && th.classList) {
                        th.classList.remove('sort-asc', 'sort-desc');
                    }
                }
                
                // 添加新的排序指示器
                headerCell.classList.add('sort-' + sortDirection);
        
                // 特殊处理耗时列
                const isTimeColumn = headerCell.textContent && headerCell.textContent.trim() === '任务耗时';
                const actualSortType = isTimeColumn ? 'time' : sortType;
        
                // 排序行对 - 使用稳定的排序算法
                rowPairs.sort((pairA, pairB) => {
                    try {
                        // 确保main对象存在
                        if (!pairA || !pairA.main || !pairB || !pairB.main) {
                            return 0;
                        }
                        
                        const valueA = getCellValue(pairA.main, columnIndex, actualSortType);
                        const valueB = getCellValue(pairB.main, columnIndex, actualSortType);
                        
                        let result;
                        if (actualSortType === 'number' || actualSortType === 'date' || actualSortType === 'time') {
                            result = sortDirection === 'asc' ? valueA - valueB : valueB - valueA;
                        } else {
                            result = sortDirection === 'asc' 
                                ? String(valueA).localeCompare(String(valueB), 'zh-CN') 
                                : String(valueB).localeCompare(String(valueA), 'zh-CN');
                        }
                        
                        // 如果值相等，保持原始顺序（稳定排序）
                        if (result === 0) {
                            const indexA = parseInt(pairA.main.getAttribute('data-original-index') || '0');
                            const indexB = parseInt(pairB.main.getAttribute('data-original-index') || '0');
                            return indexA - indexB;
                        }
                        
                        return result;
                    } catch (e) {
                        console.error('排序比较时出错:', e);
                        return 0;
                    }
                });
        
                // 创建文档片段以提高性能
                const fragment = document.createDocumentFragment();
                
                // 先添加排序后的数据行和附属行
                for (let i = 0; i < rowPairs.length; i++) {
                    const pair = rowPairs[i];
                    // 确保main对象存在
                    if (pair && pair.main) {
                        fragment.appendChild(pair.main);
                        // 确保sub对象存在
                        if (pair.sub) {
                            fragment.appendChild(pair.sub);
                        }
                    }
                }
                
                // 最后添加汇总行
                for (let i = 0; i < summaryRows.length; i++) {
                    const row = summaryRows[i];
                    if (row) {
                        fragment.appendChild(row);
                    }
                }
                
                // 清空tbody
                while (tbody.firstChild) {
                    tbody.removeChild(tbody.firstChild);
                }
                
                // 一次性添加所有行
                tbody.appendChild(fragment);
            } catch (error) {
                console.error('排序过程中发生错误:', error);
                alert('排序过程中发生错误: ' + error.message);
            } finally {
                // 清除定时器并移除加载提示
                clearTimeout(loadingTimer);
                if (loadingDiv && loadingDiv.parentNode) {
                    document.body.removeChild(loadingDiv);
                }
            }
        }, 50); // 短暂延迟让UI更新
    } catch (error) {
        console.error('排序初始化时发生错误:', error);
        // 清除定时器并确保加载提示被移除
        clearTimeout(loadingTimer);
        if (loadingDiv && loadingDiv.parentNode) {
            document.body.removeChild(loadingDiv);
        }
    }
}");
            html.AppendLine("    </script>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            // 修改 colspan 计算逻辑
            int colspan = colConfigs.Length;

            if (actionItems.Count > 0)
            {
                colspan = colspan + col2Configs.Length;
                // 按时间分组，考虑每天凌晨4点为新的一天
                var groupedByCustomDay = actionItems.GroupBy(item => GetCustomDay(item.Time))
                    .OrderBy(group => group.Key)
                    .Reverse().ToList();

                html.AppendLine("<h2>按日摩拉收益统计</h2>");
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


            // 遍历每个配置组生成表格
            foreach (var group in configGroups)
            {
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
                html.AppendLine($"<h2>配置组：{group.Name}</h2>");
                html.AppendLine(
                    $"<h3>{group.StartDate?.ToString("yyyy-MM-dd HH:mm:ss")}-{group.EndDate?.ToString("yyyy-MM-dd HH:mm:ss")}</h3>");
                html.AppendLine($"<h3>耗时{ConvertSecondsToTime(totalSeconds)}</h3>");
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
                        $"        <td colspan=\"{actualColspan}\">拾取物: {string.Join(", ", taskSortedPicks)}</td>");
                    html.AppendLine("    </tr>");
                }

                // 按 Value 倒序排列 Picks
                var sortedPicks = mergedPicks.OrderByDescending(p => p.Value)
                    .Select(p => $"{p.Key} ({p.Value})");

                // 修改拾取物行添加 ignore-sort 类
                html.AppendLine("    <tr class=\"ignore-sort\">");
                html.AppendLine($"        <td colspan=\"{colspan}\">拾取物: {string.Join(", ", sortedPicks)}</td>");
                html.AppendLine("    </tr>");

                if (actionItems.Count > 0)
                {
                    html.AppendLine("    <tr class=\"ignore-sort\">");
                    html.AppendLine(
                        $"        <td colspan=\"{colspan}\">锄地总计:{ConcatenateStrings("小怪：", groupms.SmallMonsterStatistics.ToString()) +
                                                                  /*ConcatenateStrings(",最后一只小怪挂于", groupms.LastSmallTime) +*/
                                                                  ConcatenateStrings(",精英怪数量：", groupms.EliteGameStatistics.ToString()) +
                                                                  ConcatenateStrings(",精英详细:", groupms.EliteDetails) +
                                                                  /*ConcatenateStrings(",最后一只精英挂于", groupms.LastEliteTime) +*/
                                                                  ConcatenateStrings(",合计锄地摩拉：", groupms.TotalMoraKillingMonstersMora.ToString()) +
                                                                  ConcatenateStrings("，每秒摩拉", (groupms.TotalMoraKillingMonstersMora / (groupms.StatisticsEnd - groupms.StatisticsStart)?.TotalSeconds ?? 0).ToString("F2"))}");
                    html.AppendLine("    </tr>");

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