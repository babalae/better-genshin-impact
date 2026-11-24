using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.I18n;

/// <summary>
/// 插件提供的一种游戏语言，文本已读入内存。
/// </summary>
/// <param name="Code">语言代码，如 it。</param>
/// <param name="DisplayName">显示名称，如 Italiano。</param>
/// <param name="Table">游戏文本：Key 为 <see cref="GameTextKeys"/> 中的语义标识，值为游戏客户端中的文本或匹配模式。</param>
public sealed record GameTextPack(string Code, string DisplayName, IReadOnlyDictionary<string, string> Table);

/// <summary>
/// 一种可选的游戏语言，即游戏语言下拉框中的一项。
/// </summary>
/// <param name="Code">语言代码。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record GameLanguageOption(string Code, string DisplayName);
