using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Localization;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;

/// <summary>
/// 地脉花任务识别游戏内文字所用的匹配模式，按游戏语言解析。
/// 与秘境、幽境共用 AutoDomainTask 资源；资源缺少对应语言时使用此处的中文模式，保持原有中文客户端行为。
/// </summary>
internal sealed class LeyLineGameText
{
    /// <summary>
    /// 秘境资源本地化器，三类领奖任务共用。
    /// </summary>
    private readonly IStringLocalizer<AutoDomainTask> _localizer;

    /// <summary>
    /// 当前游戏语言。
    /// </summary>
    private readonly CultureInfo _culture;

    /// <summary>
    /// 按游戏语言创建匹配模式。
    /// </summary>
    /// <param name="culture">游戏语言。</param>
    /// <param name="localizer">秘境资源本地化器；为空时从容器获取，测试可注入替身。</param>
    public LeyLineGameText(CultureInfo culture, IStringLocalizer<AutoDomainTask>? localizer = null)
    {
        _culture = culture;
        _localizer = localizer
                     ?? App.GetService<IStringLocalizer<AutoDomainTask>>()
                     ?? throw new InvalidOperationException("未初始化秘境本地化服务。");

        Outcrop = Pattern("地脉花_溢口", "溢口");
        InteractPrompt = Resolve("地脉花_交互提示", "接触|地脉|之花");
        Flower = Pattern("地脉花_地脉之花", "地脉.*之花|之花.*地脉");
        RewardPromptTitle = Pattern("地脉花_领奖标题", "激活地脉之花|选择激活方式|地脉之花");
        RewardPromptContent = Pattern("地脉花_领奖内容", "原粹树脂|浓缩树脂|须臾树脂|脆弱树脂|激活地脉之花|选择激活方式|树脂.*使用|使用.*树脂|补充");
        UseAction = Pattern("使用", "使用");
        OriginalResin = Pattern("地脉花_原粹", "原粹");
        DoubleReward = Pattern("地脉花_双倍", "双倍|2倍");
        Revive = Pattern("地脉花_复苏", "复苏");
        StopTracking = Pattern("地脉花_停止追踪", "停止");
        TrackedLeyLine = Pattern("地脉花_追踪目标", "地脉|衍出");
        Fight = Pattern("地脉花_战斗提示", "打倒|所有|敌人");
    }

    /// <summary>地脉溢口交互提示。</summary>
    public Regex Outcrop { get; }

    /// <summary>F 交互提示文字的匹配模式，供 Bv.FindF 使用。</summary>
    public string InteractPrompt { get; }

    /// <summary>地脉之花（领奖状态）文字。</summary>
    public Regex Flower { get; }

    /// <summary>领奖弹窗标题。</summary>
    public Regex RewardPromptTitle { get; }

    /// <summary>领奖弹窗内容行。</summary>
    public Regex RewardPromptContent { get; }

    /// <summary>“使用”按钮文字。</summary>
    public Regex UseAction { get; }

    /// <summary>原粹树脂行。</summary>
    public Regex OriginalResin { get; }

    /// <summary>双倍奖励活动提示。</summary>
    public Regex DoubleReward { get; }

    /// <summary>复苏按钮。</summary>
    public Regex Revive { get; }

    /// <summary>地图上的停止追踪按钮。</summary>
    public Regex StopTracking { get; }

    /// <summary>地图上追踪中的地脉目标。</summary>
    public Regex TrackedLeyLine { get; }

    /// <summary>战斗目标提示（打倒所有敌人）。</summary>
    public Regex Fight { get; }

    /// <summary>
    /// 冒险之证国家下拉列表中的国家名匹配模式。
    /// </summary>
    /// <param name="country">配置中的中文国家名。</param>
    /// <returns>当前游戏语言下的国家名模式。</returns>
    public Regex Country(string country)
    {
        // 挪德卡莱在下拉列表中可能被截断，中文沿用原先的前缀匹配。
        var chinese = country == "挪德卡莱" ? "挪德卡" : Regex.Escape(country);
        return Pattern("地脉花_国家_" + country, chinese);
    }

    /// <summary>
    /// 解析匹配模式并编译为不区分大小写的正则。
    /// </summary>
    private Regex Pattern(string key, string chinesePattern)
        => new(Resolve(key, chinesePattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 从资源读取当前游戏语言的模式；资源不存在时回退中文模式。
    /// </summary>
    private string Resolve(string key, string chinesePattern)
    {
        using var _ = CultureHelper.Use(_culture);
        var value = _localizer[key];
        return value.ResourceNotFound || string.IsNullOrWhiteSpace(value.Value) ? chinesePattern : value.Value;
    }
}
