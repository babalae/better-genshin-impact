using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.LogParse;

public class TravelsDiaryDetailManager
{
    public static List<(int year, int month)> GetInvolvedMonths(List<LogParse.ConfigGroupEntity> configGroups)
    {
        // HashSet 用于存储不重复的年份和月份
        HashSet<(int year, int month)> involvedMonths = new HashSet<(int year, int month)>();

        foreach (var group in configGroups)
        {
            // 如果 StartDate 有值，添加对应的年份和月份
            if (group.StartDate.HasValue)
            {
                involvedMonths.Add((group.StartDate.Value.Year, group.StartDate.Value.Month));
            }

            // 如果 EndDate 有值，添加对应的年份和月份
            if (group.EndDate.HasValue)
            {
                involvedMonths.Add((group.EndDate.Value.Year, group.EndDate.Value.Month));
            }
        }

        // 返回按年份和月份排序的列表
        return involvedMonths.OrderBy(m => m.year).ThenBy(m => m.month).ToList();
    }

    public static string basePath = Global.Absolute(@"log\logparse");

    public static List<ActionItem> loadAllActionItems(GameInfo gameInfo, List<LogParse.ConfigGroupEntity> configGroups)
    {
      return loadAllActionItems(gameInfo,GetInvolvedMonths(configGroups));
    }

    public static List<ActionItem> loadAllActionItems(GameInfo gameInfo,List<(int year, int month)> ms)
    {
        //List<(int year, int month)> ms = GetInvolvedMonths(configGroups);
        string tddPath = Global.Absolute(@$"{basePath}\{gameInfo.GameUid}\travelsdiarydetail");
        List<ActionItem> actionItems = new List<ActionItem>();
        foreach (var m in ms)
        {
            string tddfile = Global.Absolute($@"{tddPath}\{m.year}_{m.month}.json");
            if (File.Exists(tddfile))
            {
                var _temp = JsonSerializer.Deserialize<ApiResponse<ActionItem>>(File.ReadAllText(tddfile));
                if (_temp != null)
                {
                    //统计杀怪或突发事件奖励
                    actionItems.AddRange(_temp.Data.List.Where(item => item.ActionId == 37 || item.ActionId == 28 || item.ActionId == 39));
                }
            }
        }

        return actionItems.OrderBy(m => DateTime.Parse(m.Time)).ToList();
    }
    private static List<(int year, int month)> GetMonthPairs()
    {
        DateTime now = DateTime.Now;

        List<(int year, int month)> result = new List<(int, int)>();

        if (now.Day == 1 && now.Hour < 4)
        {
            // 上个月
            DateTime lastMonth = now.AddMonths(-1);
            result.Add((lastMonth.Year, lastMonth.Month));
        }

        // 当前月
        result.Add((now.Year, now.Month));

        return result;
    }
    
    
    //取今天的札记数据
    public static List<ActionItem> loadNowDayActionItems(GameInfo gameInfo)
    {
        //正序的
        var sortedList = loadAllActionItems(gameInfo, GetMonthPairs());
        DateTime now = DateTime.Now;
        DateTime today4am = now.Date.AddHours(4);

        DateTime startTime, endTime;

        if (now < today4am)
        {
            // 现在是今天凌晨4点前，区间是昨天4点 ~ 今天4点前
            startTime = today4am.AddDays(-1);
            endTime = today4am;
        }
        else
        {
            // 现在是今天4点后，区间是今天4点 ~ 明天4点
            startTime = today4am;
            endTime = today4am.AddDays(1);
        }

        // 取出符合时间段的数据
        var dayItems = sortedList
            .Where(m =>
            {
                var time = DateTime.Parse(m.Time);
                return time >= startTime && time < endTime;
            })
            .ToList();
        return dayItems;
    }

    /*
     * 增量更新，米游社札记摩拉记录
     */
    public static async Task<GameInfo> UpdateTravelsDiaryDetailManager(string cookie,bool skipToast = false)
    {
        List<(int year, int month)> months = GetCurrentAndPreviousTwoMonths();
        months.Reverse();

        YsClient ys = new YsClient();
        var apiResponse = await ys.GetGenshinGameRolesAsync(cookie);
        GameInfo gameInfo = apiResponse.Data.List[0];
        string tddPath = Global.Absolute(@$"{basePath}\{gameInfo.GameUid}\travelsdiarydetail");

        try
        {
            for (int i = 0; i < months.Count; i++)
            {
                var month = months[i];
                string tddfile = Global.Absolute($@"{tddPath}\{month.year}_{month.month}.json");
                var fileExists = File.Exists(tddfile);

                //文件存在，进行增量更新
                if (i > 0 && fileExists)
                {
                    bool canUpdate = true;
                    //上个月的如果这个月更新过，就不再更新了
                    if (i == 1 && IsFileModifiedThisMonth(tddfile))
                    {
                        canUpdate = false;
                    }

                    if (canUpdate)
                    {
                        // 读取文件内容
                        string jsonString2 = File.ReadAllText(tddfile);
                        //文件内容
                        var _temp = JsonSerializer.Deserialize<ApiResponse<ActionItem>>(jsonString2);
                        //增量
                        var _temp2 = await ys.GetTravelsDiaryDetailAsync(gameInfo, cookie, month.month, 2, 100, default,
                            _temp.Data.List[0]);
                        var addList = _temp2.Data.List;
                        _temp2.Data.List.AddRange(_temp.Data.List);
                        writeFile(tddfile, _temp2);
                    }
                }

                //文件不存在，全量更新
                if (!fileExists)
                {
                    var _temp2 = await ys.GetTravelsDiaryDetailAsync(gameInfo, cookie, month.month, 2);
                    writeFile(tddfile, _temp2);
                    if (!skipToast)
                    {
                        Toast.Information($"{month.year}_{month.month}数据获取成功！");
                    }
                    else
                    {
                        TaskControl.Logger.LogError($"米游社札记数据:{month.year}_{month.month}获取成功！");

                    }

                }
                /*else
                {
                    var _temp = JsonSerializer.Deserialize<ApiResponse<ActionItem>>(File.ReadAllText(tddfile));

                }*/
            }
        }
        catch (NoLoginException e)
        {
            if (!skipToast)
            {
                Toast.Warning("token未登录，请重新登录获取，此次将不新最新数据！");
            }
            else
            {
                TaskControl.Logger.LogError($"token未登录，请重新登录获取，此次将不新最新数据！");
            }


        }

        return gameInfo;
    }

    static void writeFile(string path, ApiResponse<ActionItem> apiResponse)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true // 启用格式化（缩进）
        };
        string jsonString = JsonSerializer.Serialize(apiResponse, options);
        string directory = Path.GetDirectoryName(path);

        // 如果目录不存在，则创建它
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 将格式化后的 JSON 写入文件
        File.WriteAllText(path, jsonString);
    }

    static bool IsFileModifiedThisMonth(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("文件未找到", filePath);
        }

        DateTime lastModified = File.GetLastWriteTime(filePath);

        // 获取当前月份的开始和结束日期
        DateTime startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        DateTime endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

        // 判断文件最后修改时间是否在本月
        return lastModified >= startOfMonth && lastModified <= endOfMonth;
    }

    static List<(int year, int month)> GetCurrentAndPreviousTwoMonths()
    {
        List<(int year, int month)> months = new List<(int year, int month)>();
        DateTime now = DateTime.Now;

        for (int i = 0; i < 3; i++)
        {
            int year = now.Year;
            int month = now.Month - i;

            // 如果月份小于 1，则向前推一年并调整月份
            if (month < 1)
            {
                month += 12;
                year -= 1;
            }

            months.Add((year, month));
        }

        return months;
    }

    public static string generHtmlMessage()
    {
        string htmlContent = @"
                        <!DOCTYPE html>
                        <html lang='zh'>
                        <head>
                            <meta charset='UTF-8'>
                            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                            <title>锄地统计说明</title>
                            <style>
                                body { font-family: Arial, sans-serif; margin: 20px; }
                                .content { padding: 20px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1); }
                                h1 { font-size: 24px; color: #333; }
                                p { font-size: 16px; color: #555; line-height: 1.6; }
                                a { color: #007bff; text-decoration: none; }
                                a:hover { text-decoration: underline; }
                                .cookie-input { margin-top: 15px; padding: 8px; width: 100%; max-width: 300px; border: 1px solid #ccc; border-radius: 4px; }
                            </style>
                        </head>
                        <body>
                            <div class='content'>
                                <h1>锄地统计说明</h1>
                                <p>锄地统计基于米游社旅行札记（不实时，有误差但不大），需要获取米游社cookie，参照下面地址获取：</p>
                                <p><a href='https://www.bilibili.com/video/BV1Cr4y1e7wJ' target='_blank'>点此查看如何获取米游社Cookie</a></p>
                                <p>PC端获取一样，登录后，按F12，输入上面页面中的代码(javascript:(function(){prompt('', document.cookie)})();)，能更快的拿到。</p>
                                <p>按步骤获取cookie，填入前面文本框。一次可管好多天，如果提示未登录，再次获取即可。</p>
                                <p>首次获取是全量获取最近3个月的数据，会比较慢，后续增量更新会快。</p>
                            </div>
                        </body>
                        </html>";
        return htmlContent;
    }
}