using System;
using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 路线 CD 在界面上的统一文字：状态名称、时间格式与作用域名称；任务树标记、CD 总览、运行预览和设置表单共用。
/// </summary>
internal static class PuloniaCooldownText
{
    /// <summary>状态的中文名称。</summary>
    public static string State(PuloniaCollectionCooldownState state) => state switch
    {
        PuloniaCollectionCooldownState.CoolingDown => "CD 中",
        PuloniaCollectionCooldownState.Completed => "已完成（一次性）",
        PuloniaCollectionCooldownState.Unmatched => "未识别刷新规则",
        PuloniaCollectionCooldownState.NotChecked => "不检查 CD",
        _ => "可执行"
    };

    /// <summary>按本机时区显示的绝对时间；为空时显示“无”。</summary>
    public static string Time(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "无";

    /// <summary>
    /// 距下次可执行的简短说明：24 小时内显示剩余时长，更远显示“明天 04:00”或“10-30 04:00”。
    /// </summary>
    /// <param name="next">下次可执行时间。</param>
    /// <param name="now">当前时间。</param>
    public static string Remaining(DateTimeOffset next, DateTimeOffset now)
    {
        var remaining = next - now;
        if (remaining <= TimeSpan.FromMinutes(1))
            return "即将可执行";
        if (remaining < TimeSpan.FromHours(24))
            return remaining.TotalHours >= 1
                ? $"剩 {(int)remaining.TotalHours} 小时" + (remaining.Minutes > 0 ? $" {remaining.Minutes} 分" : string.Empty)
                : $"剩 {remaining.Minutes} 分";
        var local = next.ToLocalTime();
        return local.Date == now.ToLocalTime().Date.AddDays(1) ? $"明天 {local:HH:mm}" : $"{local:MM-dd HH:mm}";
    }

    /// <summary>
    /// 下次可执行的完整说明：24 小时内为“剩 5 小时 12 分（10-29 04:00）”，更远只显示绝对时间，避免同一时间出现两次。
    /// </summary>
    /// <param name="next">下次可执行时间。</param>
    /// <param name="now">当前时间。</param>
    public static string Next(DateTimeOffset next, DateTimeOffset now)
        => next - now < TimeSpan.FromHours(24)
            ? $"{Remaining(next, now)}（{next.ToLocalTime():MM-dd HH:mm}）"
            : Time(next);

    /// <summary>
    /// 作用域的显示名：默认作用域为“当前登录账号”，指定账号显示账号资料名与 UID。
    /// </summary>
    /// <param name="scopeKey">作用域键。</param>
    /// <param name="accountNamesByUid">UID → 账号资料名称。</param>
    public static string Scope(string scopeKey, IReadOnlyDictionary<string, string> accountNamesByUid)
    {
        var identity = scopeKey.StartsWith(PuloniaCollectionCooldown.ScopeKeyPrefix, StringComparison.Ordinal)
            ? scopeKey[PuloniaCollectionCooldown.ScopeKeyPrefix.Length..] : scopeKey;
        if (identity == PuloniaCollectionCooldown.DefaultScopeIdentity)
            return "当前登录账号";
        return accountNamesByUid.TryGetValue(identity, out var name) ? $"{name}（UID {identity}）" : $"UID {identity}";
    }
}
