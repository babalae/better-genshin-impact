using System.Collections.Generic;
using System;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using static LogParse.LogParse.ConfigGroupEntity;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vanara.PInvoke;

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




                Console.WriteLine(logstr);
            }

            //if (configGroupEntity != null)
            //{
            //    configGroupEntities.Add(configGroupEntity);
            //}


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
                    if (DateTime.TryParseExact(dateString, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
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
        public static string GenerHtmlByConfigGroupEntity(List<ConfigGroupEntity> configGroups) {
            (string name, Func<ConfigTask, string> value)[] colConfigs = [
         (name: "名称", value: task => task.Name)
                ,(name: "开始日期", value: task => task.StartDate?.ToString("yyyy-MM-dd HH:mm:ss")??"")
                ,(name: "结束日期", value: task => task.EndDate?.ToString("yyyy-MM-dd HH:mm:ss")??"")
                ,(name: "耗时", value: task => ConvertSecondsToTime((task.EndDate - task.StartDate)?.TotalSeconds ?? 0))
         ];
            return GenerHtmlByConfigGroupEntity(configGroups, "日志分析", colConfigs);
        }
        public static string GenerHtmlByConfigGroupEntity(List<ConfigGroupEntity> configGroups,string title, (string name, Func<ConfigTask, string> value)[] colConfigs)
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
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");


 
            // 遍历每个配置组生成表格
            foreach (var group in configGroups)
            {
                TimeSpan? timeDiff = group.EndDate - group.StartDate;
                double totalSeconds = timeDiff?.TotalSeconds ?? 0;
                
                html.AppendLine($"<h2>配置组：{group.Name}({group.StartDate?.ToString("yyyy-MM-dd HH:mm:ss")}-{group.EndDate?.ToString("yyyy-MM-dd HH:mm:ss")})，耗时{ConvertSecondsToTime(totalSeconds)}</h2>");
                html.AppendLine("<table>");
                html.AppendLine("    <tr>");
                foreach (var item in colConfigs)
                {
                    html.AppendLine($"        <th>{item.name}</th>");
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
                    html.AppendLine("    </tr>");
                }

                // 按 Value 倒序排列 Picks
                var sortedPicks = mergedPicks.OrderByDescending(p => p.Value)
                                             .Select(p => $"{p.Key} ({p.Value})");

                // Picks 行
                html.AppendLine("    <tr>");
                html.AppendLine($"        <td colspan=\"4\">拾取物: {string.Join(", ", sortedPicks)}</td>");
                html.AppendLine("    </tr>");

                html.AppendLine("</table>");
            }

            // HTML尾部
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }
    }
}
