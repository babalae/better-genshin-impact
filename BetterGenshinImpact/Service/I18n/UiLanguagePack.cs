using System.Collections.Generic;

namespace BetterGenshinImpact.Service.I18n;

/// <summary>
/// 插件提供的一种界面语言，翻译已读入内存。
/// </summary>
/// <param name="Code">语言代码，如 it。</param>
/// <param name="DisplayName">显示名称，如 Italiano。</param>
/// <param name="Translations">翻译：Key 为简体中文原文，值为译文。</param>
public sealed record UiLanguagePack(string Code, string DisplayName, IReadOnlyDictionary<string, string> Translations);

/// <summary>
/// 界面语言下拉框中的一项。
/// </summary>
/// <param name="Code">语言代码。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record UiLanguageOption(string Code, string DisplayName);
