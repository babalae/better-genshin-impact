using System;

namespace BetterGenshinImpact.Core.Plugins;

/// <summary>
/// 声明插件提供的一种游戏语言（原神客户端的语言）的识别文本。标在插件入口程序集上，一个插件可以声明多个，例如：
/// <c>[assembly: GameLanguage("it", "Italiano")]</c>。
/// 与 <see cref="UiLanguageAttribute"/> 相互独立：同一个插件可以只提供其中一种，也可以两种都提供。
/// 语言代码与主程序内置游戏语言相同时，插件的文本优先，缺少的条目再取内置文本，仍缺少时取简体中文的文本。
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GameLanguageAttribute(string code, string displayName) : Attribute
{
    /// <summary>
    /// 语言代码，保存在游戏语言配置中，如 it、pt-BR。必须是 .NET 能识别的文化名，识别数字等场景会按它解析。
    /// </summary>
    public string Code { get; } = code;

    /// <summary>
    /// 游戏语言下拉框中显示的名称，使用该语言的自称，如 Italiano。
    /// </summary>
    public string DisplayName { get; } = displayName;

    /// <summary>
    /// 文本文件相对插件目录的路径，为空时为 GameText/&lt;语言代码&gt;.json。
    /// 文件格式与主程序的 GameTask/I18n/*.json 相同：Key 为 GameTextKeys 中的语义标识，值为游戏客户端中的文本或匹配模式。
    /// </summary>
    public string? File { get; init; }
}
