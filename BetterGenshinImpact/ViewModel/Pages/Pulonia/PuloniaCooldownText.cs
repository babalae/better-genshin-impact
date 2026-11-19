using System;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 路线 CD 在界面上的统一文字：状态名称、时间格式与账户说明；任务树标记、CD 总览、运行预览和设置表单共用。
/// </summary>
internal static class PuloniaCooldownText
{
    /// <summary>状态的中文名称。</summary>
    public static string State(PuloniaRouteCooldownState state) => state switch
    {
        PuloniaRouteCooldownState.CoolingDown => I18nService.Instance.Translate("CD 中"),
        PuloniaRouteCooldownState.Completed => I18nService.Instance.Translate("已完成（一次性）"),
        PuloniaRouteCooldownState.Unmatched => I18nService.Instance.Translate("未识别刷新规则"),
        PuloniaRouteCooldownState.NotChecked => I18nService.Instance.Translate("不检查 CD"),
        _ => I18nService.Instance.Translate("可执行")
    };

    /// <summary>按本机时区显示的绝对时间；为空时显示“无”。</summary>
    public static string Time(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? I18nService.Instance.Translate("无");

    /// <summary>
    /// 距下次可执行的简短说明：24 小时内显示剩余时长，更远显示“明天 04:00”或“10-30 04:00”。
    /// </summary>
    /// <param name="next">下次可执行时间。</param>
    /// <param name="now">当前时间。</param>
    public static string Remaining(DateTimeOffset next, DateTimeOffset now)
    {
        var remaining = next - now;
        if (remaining <= TimeSpan.FromMinutes(1))
            return I18nService.Instance.Translate("即将可执行");
        if (remaining < TimeSpan.FromHours(24))
            return remaining.TotalHours >= 1
                ? remaining.Minutes > 0
                    ? string.Format(I18nService.Instance.Translate("剩 {0} 小时 {1} 分"), (int)remaining.TotalHours, remaining.Minutes)
                    : string.Format(I18nService.Instance.Translate("剩 {0} 小时"), (int)remaining.TotalHours)
                : string.Format(I18nService.Instance.Translate("剩 {0} 分"), remaining.Minutes);
        var local = next.ToLocalTime();
        return local.Date == now.ToLocalTime().Date.AddDays(1)
            ? string.Format(I18nService.Instance.Translate("明天 {0}"), local.ToString("HH:mm"))
            : $"{local:MM-dd HH:mm}";
    }

    /// <summary>
    /// 下次可执行的完整说明：24 小时内为“剩 5 小时 12 分（10-29 04:00）”，更远只显示绝对时间，避免同一时间出现两次。
    /// </summary>
    /// <param name="next">下次可执行时间。</param>
    /// <param name="now">当前时间。</param>
    public static string Next(DateTimeOffset next, DateTimeOffset now)
        => next - now < TimeSpan.FromHours(24)
            ? string.Format(I18nService.Instance.Translate("{0}（{1}）"), Remaining(next, now), next.ToLocalTime().ToString("MM-dd HH:mm"))
            : Time(next);

    /// <summary>
    /// 路线 CD 所按账户的说明，例如“CD 按账户：小号”。完成记录按账户存放，界面始终说明按哪个账户显示。
    /// </summary>
    /// <param name="accountName">账户名称。</param>
    public static string Account(string accountName) => string.Format(I18nService.Instance.Translate("CD 按账户：{0}"), accountName);
}
