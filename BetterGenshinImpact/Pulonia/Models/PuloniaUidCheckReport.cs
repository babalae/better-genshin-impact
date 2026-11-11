using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 本实例一次 UID 比对的结果：跟随当前账户的运行开始时读取游戏中的 UID，与实例当前账户比较。
/// 只保存在内存中，供标题栏账户徽标与不一致提示使用，不写入任何文件。
/// </summary>
/// <param name="AccountId">比对时的实例当前账户 ID</param>
/// <param name="Result">比对结果：一致、不一致或未比对</param>
/// <param name="ObservedUid">不一致时游戏中读到的完整 UID；其他结果为 null</param>
/// <param name="CheckedAt">比对时间</param>
public sealed record PuloniaUidCheckReport(string AccountId, PuloniaTaskUidCheck Result, string? ObservedUid, DateTimeOffset CheckedAt);
