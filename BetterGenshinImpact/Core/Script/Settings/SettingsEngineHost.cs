using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Utils;
using Microsoft.ClearScript;

namespace BetterGenshinImpact.Core.Script.Settings;

public static class SettingsEngineHost
{
    public static void Init(IScriptEngine engine, string workDir, string[] searchPaths, SettingsScriptContext context, HtmlMessageChannel channel, string windowId, bool allowHttp, string[] httpAllowedUrls, Func<bool> windowExists, Func<bool> closeWindow)
    {
        engine.AddHostObject("log", new Log());
        engine.AddHostObject("file", new LimitedFile(workDir));
        engine.AddHostObject("strategyFile", new StrategyFile());
        engine.AddHostObject("http", new Http(allowHttp, httpAllowedUrls));
        engine.AddHostObject("settingsUi", new SettingsUiSession(channel, windowId, windowExists, closeWindow));
        engine.AddHostObject("settings", context.Settings);
        engine.AddHostObject("settingsContext", context);
        engine.AddHostObject("sleep", (Func<int, Task>)(milliseconds => Task.Delay(milliseconds, context.CancellationToken)));
        engine.AddHostType("CancellationToken", typeof(CancellationToken));

        engine.DocumentSettings.AccessFlags = DocumentAccessFlags.AllowCategoryMismatch;
        var normalizedPaths = new List<string>();
        foreach (var path in searchPaths)
        {
            normalizedPaths.Add(ScriptUtils.NormalizePath(workDir, path));
        }

        if (normalizedPaths.Count > 0)
        {
            engine.DocumentSettings.SearchPath = string.Join(';', normalizedPaths);
        }
    }
}
