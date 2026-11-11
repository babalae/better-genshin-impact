using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 计划“运行账户”入口的规则与文案，任务计划页与新一条龙页共用，保证两处一致：
/// 选项为“跟随当前账户”加填写了 UID 的账户；账户目录只有一个账户、且计划跟随当前账户时不显示入口。
/// </summary>
public static class PuloniaRunAccountText
{
    /// <summary>运行账户选项中“跟随当前账户”的键；下拉框的选中值不能区分 null，因此用空字符串表示。</summary>
    public const string FollowCurrentKey = "";

    /// <summary>
    /// 建立运行账户下拉框的选项：“跟随当前账户（不切换）”加填写了 UID 的账户，顺序与账户列表一致。
    /// </summary>
    /// <param name="accounts">已保存的账户目录</param>
    /// <returns>键为账户 ID（跟随当前账户为空字符串）、值为显示文字的选项</returns>
    public static IReadOnlyList<KeyValuePair<string, string>> BuildOptions(IEnumerable<PuloniaGenshinAccountProfile> accounts)
    {
        var options = new List<KeyValuePair<string, string>> { new(FollowCurrentKey, "跟随当前账户（不切换）") };
        // 只能指定填写了 UID 的账户，因为切换和核验都要按 UID 找到目标。
        options.AddRange(accounts.Where(account => !string.IsNullOrEmpty(account.Uid))
            .Select(account => new KeyValuePair<string, string>(account.Id, $"{account.Name}（UID {account.Uid}）")));
        return options;
    }

    /// <summary>
    /// 计划标题旁是否显示“运行账户”：计划指定了账户，或账户目录有多个账户。
    /// </summary>
    /// <param name="accountId">计划的运行账户设置；为空表示跟随当前账户</param>
    /// <param name="accountCount">账户目录中的账户数</param>
    public static bool ShouldShow(string? accountId, int accountCount) => accountId is not null || accountCount > 1;

    /// <summary>
    /// 能否把这个账户设为计划的运行账户：账户在目录中且填写了 UID。
    /// </summary>
    /// <param name="accountId">要指定的账户 ID</param>
    /// <param name="accounts">已保存的账户目录</param>
    public static bool CanSpecify(string accountId, IEnumerable<PuloniaGenshinAccountProfile> accounts)
        => accounts.Any(account => account.Id == accountId && !string.IsNullOrEmpty(account.Uid));

    /// <summary>
    /// 计划标题旁显示的运行账户文字；指定的账户已删除或没有 UID 时明确说明。
    /// </summary>
    /// <param name="accountId">计划的运行账户设置；为空表示跟随当前账户</param>
    /// <param name="accounts">已保存的账户目录</param>
    public static string Describe(string? accountId, IEnumerable<PuloniaGenshinAccountProfile> accounts)
    {
        if (accountId is null)
            return "运行账户：跟随当前账户";
        if (accounts.FirstOrDefault(account => account.Id == accountId) is not { } account)
            return "运行账户：账户已删除，请重新选择";
        return string.IsNullOrEmpty(account.Uid)
            ? $"运行账户：“{account.Name}”没有 UID，请重新选择"
            : "运行账户：" + account.Name;
    }

    /// <summary>
    /// 指定的运行账户不可用时的提示；跟随当前账户或指定的账户可用时为 null。
    /// </summary>
    /// <param name="accountId">计划的运行账户设置；为空表示跟随当前账户</param>
    /// <param name="accounts">已保存的账户目录</param>
    public static string? GetProblem(string? accountId, IEnumerable<PuloniaGenshinAccountProfile> accounts)
    {
        if (accountId is null)
            return null;
        if (accounts.FirstOrDefault(account => account.Id == accountId) is not { } account)
            return "指定的账户已删除，请重新选择运行账户。";
        return string.IsNullOrEmpty(account.Uid)
            ? $"“{account.Name}”没有 UID，不能切换和核验，请重新选择运行账户。"
            : null;
    }
}
