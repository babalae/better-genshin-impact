namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 运行账户的来源，写入运行记录的 <c>account_source</c>（序列化为 snake_case 字符串）。
/// </summary>
public enum PuloniaTaskAccountSource
{
    /// <summary>计划跟随当前账户，运行账户是实例当前账户（<c>instance_current</c>）。</summary>
    InstanceCurrent,
    /// <summary>计划指定了账户，必要时切换并核验 UID（<c>specified</c>）。</summary>
    Specified
}

/// <summary>
/// 跟随当前账户的运行开始时的 UID 比对结果，写入运行记录的 <c>uid_check</c>；指定账户的运行不比对。
/// </summary>
public enum PuloniaTaskUidCheck
{
    /// <summary>游戏中读到的 UID 与当前账户一致（<c>matched</c>）。</summary>
    Matched,
    /// <summary>游戏中读到的 UID 与当前账户不一致，运行照常进行（<c>mismatched</c>）。</summary>
    Mismatched,
    /// <summary>当前账户没有 UID、游戏不在主界面或识别失败，未比对（<c>skipped</c>）。</summary>
    Skipped
}
