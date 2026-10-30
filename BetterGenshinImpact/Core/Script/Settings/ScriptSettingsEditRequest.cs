using System;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Core.Script.Settings;

/// <summary>
/// 打开一次 HTML 设置页所需的信息：页面、打开入口，以及读取和保存设置值的方法。
/// </summary>
/// <param name="Page">脚本的设置页。</param>
/// <param name="Host">打开设置的入口，scheduler 或 pulonia，写入页面上下文。</param>
/// <param name="IsCreation">是否在添加任务时打开。</param>
/// <param name="Load">读取当前已保存的设置值，从未保存时返回空对象。</param>
/// <param name="Save">用页面提交的对象整体替换设置值；拒绝写入时抛出 InvalidOperationException，消息会返回给页面。</param>
public sealed record ScriptSettingsEditRequest(
    ScriptSettingsHtmlPage Page,
    string Host,
    bool IsCreation,
    Func<JObject> Load,
    Action<JObject> Save);
