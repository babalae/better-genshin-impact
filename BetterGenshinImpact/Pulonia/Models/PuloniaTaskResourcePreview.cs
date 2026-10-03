using System.Collections.Generic;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 按需读取的一项资源详情及推荐任务名称。
/// </summary>
public sealed class PuloniaTaskResourcePreview
{
    /// <summary>
    /// 面向用户展示的详情文本。
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// 资源元数据提供的推荐任务名称。
    /// </summary>
    public string? SuggestedName { get; }

    /// <summary>
    /// JS 项目的 README.md 完整路径；由项目原生 Markdown 控件直接读取。
    /// </summary>
    public string? MarkdownFilePath { get; }

    /// <summary>
    /// JS 项目通过 manifest.settings_ui 声明的脚本设置项。
    /// </summary>
    public IReadOnlyList<SettingItem> JavaScriptSettingItems { get; }

    /// <summary>
    /// 脚本设置文件无法解析时的错误信息；README 仍可独立展示。
    /// </summary>
    public string? JavaScriptSettingsError { get; }

    /// <summary>
    /// 建立不可变资源预览。
    /// </summary>
    public PuloniaTaskResourcePreview(string text, string? suggestedName, string? markdownFilePath = null,
        IReadOnlyList<SettingItem>? javaScriptSettingItems = null, string? javaScriptSettingsError = null)
    {
        Text = text;
        SuggestedName = suggestedName;
        MarkdownFilePath = markdownFilePath;
        JavaScriptSettingItems = javaScriptSettingItems ?? [];
        JavaScriptSettingsError = javaScriptSettingsError;
    }
}
