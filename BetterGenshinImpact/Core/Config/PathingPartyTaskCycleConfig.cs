using System;
using BetterGenshinImpact.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class PathingPartyTaskCycleConfig : ObservableObject
{
    //启用执行周期配置
    [ObservableProperty]
    private bool _enable = false;
    
    //周期分界时间点（小时），如果负数则不启用
    [ObservableProperty]
    private int _boundaryTime = 0;
    // 分界时间是否基于服务器时间（否则基于本地时间）
    [ObservableProperty]
    private bool _isBoundaryTimeBasedOnServerTime = false;

    
    //不同材料有不同的周期，按需配置，如矿石类是3、突破材料是2，或按照自已想几天执行一次配置即可
    [ObservableProperty]
    private int _cycle = 1;
    
    
    
    //执行周期序号，按时间戳对应的天数，对周期求余值加1，得出的值和配置执一致就会执行，否则跳过任务。
    [ObservableProperty]
    private int _index = 1;
    
    
    public int GetExecutionOrder(DateTimeOffset now)
    {
        try
        {
            if (_cycle <= 0 || _boundaryTime <0  || _boundaryTime > 24 )
                return -1;

            // 修正时间：如果当前时间小于当天的分界时间，则视为前一天
            DateTime boundaryTimeToday = new DateTime(now.Year, now.Month, now.Day, _boundaryTime, 0, 0);
            if (now < boundaryTimeToday)
            {
                now = now.AddDays(-1); // 归属到前一天
            }

            // 获取从某个固定点（如 Unix 纪元）起的“修正天数”
            // 可以使用 DateTime.UnixEpoch（从 1970-01-01 00:00:00 开始）
            DateTime baseDate = DateTime.UnixEpoch; // 即 1970-01-01
            TimeSpan daysSinceBase = now.Date - baseDate.Date;
            int totalDays = (int)daysSinceBase.TotalDays;

            // 执行序号 = (修正天数 % 周期) + 1
            int executionOrder = (totalDays % _cycle) + 1;
            return executionOrder;
        }
        catch (Exception e)
        {
            return -1;
        }

    }
    public int GetExecutionOrder()
    {
        return GetExecutionOrder(IsBoundaryTimeBasedOnServerTime
            ? ServerTimeHelper.GetServerTimeNow()
            : DateTimeOffset.Now);
    }
    
}