using System;

namespace BetterGenshinImpact.Core.Plugins;

/// <summary>
/// 声明插件提供的一种界面语言。标在插件入口程序集上，一个插件可以声明多个，例如：
/// <c>[assembly: UiLanguage("it", "Italiano")]</c>。
/// 语言代码与主程序内置语言相同时，插件的翻译优先，缺少的条目再取内置翻译。
/// </summary>
/// <remarks>设计见 Docs/design/ui-language-plugin.md。</remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class UiLanguageAttribute(string code, string displayName) : Attribute
{
    /// <summary>
    /// 语言代码，保存在界面语言配置中，如 it、pt-BR。
    /// </summary>
    public string Code { get; } = code;

    /// <summary>
    /// 界面语言下拉框中显示的名称，使用该语言的自称，如 Italiano。
    /// </summary>
    public string DisplayName { get; } = displayName;

    /// <summary>
    /// 语言文件相对插件目录的路径，为空时为 I18n/&lt;语言代码&gt;.json。
    /// 文件格式与主程序的语言文件相同：Key 为简体中文原文，值为翻译，空字符串表示未翻译。
    /// </summary>
    public string? File { get; init; }
}
