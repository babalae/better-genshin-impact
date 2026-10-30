using System;
using System.Collections.Generic;
using BetterGenshinImpact.Core.Script.Settings;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 按需读取的一项资源详情及推荐任务名称。
/// 仓库 JS 预览持有一次性预览目录的租约，界面不再显示时释放，README 文件随之删除。
/// </summary>
public sealed class PuloniaTaskResourcePreview : IDisposable
{
    /// <summary>
    /// 预览目录租约；本地资源和非 JS 预览为空。
    /// </summary>
    private readonly IDisposable? _lease;

    /// <summary>
    /// 面向用户展示的详情文本。
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// 资源元数据提供的推荐任务名称。
    /// </summary>
    public string? SuggestedName { get; }

    /// <summary>
    /// JS 项目的 README.md 完整路径；由项目原生 Markdown 控件直接读取，释放预览后不能再访问。
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
    /// JS 项目是否通过 manifest.settings_html 声明了 HTML 设置页。
    /// </summary>
    public bool HasSettingsHtml { get; }

    /// <summary>
    /// 已准备好的 HTML 设置页；只在读取时要求准备设置页且解析成功时存在，随预览释放后不能再打开。
    /// </summary>
    public ScriptSettingsHtmlPage? SettingsPage { get; }

    /// <summary>
    /// 要求准备设置页但无法打开时的原因。
    /// </summary>
    public string? SettingsPageError { get; }

    /// <summary>
    /// 建立不可变资源预览。
    /// </summary>
    /// <param name="lease">预览目录租约，随本对象释放</param>
    /// <param name="hasSettingsHtml">是否声明了 HTML 设置页</param>
    /// <param name="settingsPage">已准备好的 HTML 设置页</param>
    /// <param name="settingsPageError">HTML 设置页无法打开的原因</param>
    public PuloniaTaskResourcePreview(string text, string? suggestedName, string? markdownFilePath = null,
        IReadOnlyList<SettingItem>? javaScriptSettingItems = null, string? javaScriptSettingsError = null,
        IDisposable? lease = null, bool hasSettingsHtml = false, ScriptSettingsHtmlPage? settingsPage = null,
        string? settingsPageError = null)
    {
        Text = text;
        SuggestedName = suggestedName;
        MarkdownFilePath = markdownFilePath;
        JavaScriptSettingItems = javaScriptSettingItems ?? [];
        JavaScriptSettingsError = javaScriptSettingsError;
        HasSettingsHtml = hasSettingsHtml;
        SettingsPage = settingsPage;
        SettingsPageError = settingsPageError;
        _lease = lease;
    }

    /// <summary>
    /// 释放预览目录租约；界面应先清除 README 路径再调用。
    /// </summary>
    public void Dispose() => _lease?.Dispose();
}
