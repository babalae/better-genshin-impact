namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>旧入口字段与统一树脂设置之间的纯计算映射，不读取配置或记录日志。</summary>
public static class ResinUsageLegacyMapper
{
    /// <summary>转换秘境旧字段；20、40 和不分档位次数合计为原粹领奖次数。</summary>
    /// <param name="specifyResinUse">是否分别指定树脂次数。</param>
    /// <param name="original">不分档位的原粹次数。</param>
    /// <param name="condensed">浓缩次数。</param>
    /// <param name="transient">须臾次数。</param>
    /// <param name="fragile">脆弱次数。</param>
    /// <param name="original20">20 点原粹次数。</param>
    /// <param name="original40">40 点原粹次数。</param>
    /// <param name="warning">混用档位时返回的警告，由调用方按需记录。</param>
    /// <returns>统一树脂设置。</returns>
    public static ResinUsage FromDomain(bool specifyResinUse, int original, int condensed, int transient,
        int fragile, int original20, int original40, out string? warning)
    {
        var usage = FromStygian(specifyResinUse, original, condensed, transient, fragile);
        usage.Counts.Original = original + original20 + original40;
        warning = null;

        // 单一旧档位可表达为新策略；混用时无法保留各档位配额，只保留总次数。
        if (specifyResinUse && (original20 != 0 || original40 != 0))
        {
            if (original != 0 || (original20 != 0 && original40 != 0))
                warning = "旧秘境设置混用了原粹树脂档位，已合并领奖次数并改用默认档位。";
            else
                usage.OriginalTier = original40 != 0 ? OriginalResinTier.Prefer40 : OriginalResinTier.Only20;
        }
        return usage;
    }

    /// <summary>转换幽境旧字段；关闭分别指定时使用默认的浓缩、原粹耗尽规则。</summary>
    /// <param name="specifyResinUse">是否分别指定树脂次数。</param>
    /// <param name="original">原粹次数。</param>
    /// <param name="condensed">浓缩次数。</param>
    /// <param name="transient">须臾次数。</param>
    /// <param name="fragile">脆弱次数。</param>
    /// <returns>统一树脂设置；隐藏次数仍保留，供切回方式时使用。</returns>
    public static ResinUsage FromStygian(bool specifyResinUse, int original, int condensed, int transient, int fragile) => new()
    {
        Mode = specifyResinUse ? ResinUsageMode.PerType : ResinUsageMode.Exhaust,
        Counts = new() { Original = original, Condensed = condensed, Transient = transient, Fragile = fragile }
    };

    /// <summary>转换首领旧字段；耗尽模式也保留两个补充开关。</summary>
    /// <param name="specifyRunCount">是否指定领奖次数。</param>
    /// <param name="runCount">成功领奖目标次数。</param>
    /// <param name="useTransient">是否使用须臾补充。</param>
    /// <param name="useFragile">是否使用脆弱补充。</param>
    /// <returns>统一树脂设置。</returns>
    public static ResinUsage FromBoss(bool specifyRunCount, int runCount, bool useTransient, bool useFragile) => new()
    {
        Mode = specifyRunCount ? ResinUsageMode.RunCount : ResinUsageMode.Exhaust,
        RunCount = runCount,
        Allowed = new() { Original = true, Transient = useTransient, Fragile = useFragile }
    };

    /// <summary>转换地脉花旧字段；耗尽并取小值等价于指定次数、树脂不足时提前结束。</summary>
    /// <param name="isResinExhaustionMode">是否耗尽模式。</param>
    /// <param name="openModeCountMin">是否与手动次数取小值。</param>
    /// <param name="count">手动领奖次数。</param>
    /// <param name="useTransient">是否使用须臾。</param>
    /// <param name="useFragile">是否使用脆弱。</param>
    /// <returns>统一树脂设置。</returns>
    public static ResinUsage FromLeyLine(bool isResinExhaustionMode, bool openModeCountMin, int count,
        bool useTransient, bool useFragile) => new()
    {
        Mode = isResinExhaustionMode && !openModeCountMin ? ResinUsageMode.Exhaust : ResinUsageMode.RunCount,
        RunCount = count,
        Allowed = new() { Condensed = true, Original = true, Transient = useTransient, Fragile = useFragile }
    };

    /// <summary>统一旧合成保留量；小于等于 0 都表示按默认数量合成。</summary>
    /// <param name="value">旧保留量。</param>
    /// <returns>非负保留量。</returns>
    public static int NormalizeMinResinToKeep(int value) => value <= 0 ? 0 : value;

    /// <summary>投影秘境、幽境旧开关；RunCount 是新增方式，旧布尔只能投影为 false，不能往返恢复。</summary>
    /// <param name="usage">统一设置。</param>
    /// <returns>是否按树脂分别指定。</returns>
    public static bool ToSpecifyResinUse(ResinUsage usage) => usage.Mode == ResinUsageMode.PerType;

    /// <summary>投影首领旧开关；仅 RunCount 为 true，无法用旧布尔表达 PerType。</summary>
    /// <param name="usage">统一设置。</param>
    /// <returns>是否指定总领奖次数。</returns>
    public static bool ToBossSpecifyRunCount(ResinUsage usage) => usage.Mode == ResinUsageMode.RunCount;

    /// <summary>投影地脉花耗尽开关；分别指定是新增方式，投影会丢失其各类目标。</summary>
    /// <param name="usage">统一设置。</param>
    /// <returns>是否刷至耗尽。</returns>
    public static bool ToLeyLineIsResinExhaustionMode(ResinUsage usage) => usage.Mode == ResinUsageMode.Exhaust;

    /// <summary>投影地脉花取小值开关为 false；旧“耗尽且取小值”已归一化，无法恢复原开关组合。</summary>
    /// <param name="usage">统一设置。</param>
    /// <returns>规范化旧入口不再启用取小值。</returns>
    public static bool ToLeyLineOpenModeCountMin(ResinUsage usage) => false;
}
