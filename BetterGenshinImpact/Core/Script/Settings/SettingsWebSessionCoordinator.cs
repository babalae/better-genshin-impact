using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.View;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Settings;

public static class SettingsWebSessionCoordinator
{
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> Sessions = new();

    public static string Open(ScriptGroupProject project, string groupDirectory)
    {
        if (project.Type != "Javascript" || project.Project == null)
        {
            throw new InvalidOperationException("只有已加载的 JS 脚本可以打开 HTML 配置。");
        }

        var key = $"{project.GroupInfo?.Name}|{project.FolderName}|{project.Name}";
        if (Sessions.ContainsKey(key))
        {
            SettingsWebWindow.Activate(key);
            return "already-open";
        }

        var (htmlPath, scriptPath) = project.Project.Manifest.ValidateSettingsWebUi(project.Project.ProjectPath);
        var cts = new CancellationTokenSource();
        if (!Sessions.TryAdd(key, cts)) return "already-open";

        var windowId = SettingsWebWindow.Show(htmlPath, project.Project.ProjectPath, project.AllowJsHTTP, project.Project.Manifest.HttpAllowedUrls, key);
        var thread = new Thread(() => Run(project, groupDirectory, scriptPath, windowId, key, cts))
        {
            IsBackground = true,
            Name = "SettingsWebUi"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return windowId;
    }

    private static void Run(ScriptGroupProject project, string groupDirectory, string scriptPath, string windowId, string key, CancellationTokenSource cts)
    {
        V8ScriptEngine? engine = null;
        try
        {
            engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.EnableTaskPromiseConversion);
            var root = project.Project!.ProjectPath;
            engine.DocumentSettings.Loader = new PackageDocumentLoader(root);
            var libraries = new HashSet<string>(project.Project.Manifest.Library ?? []) { ".", "./packages" };
            var context = new SettingsScriptContext(project, cts.Token, groupDirectory);
            var channel = SettingsWebWindow.Channel(windowId) ?? throw new InvalidOperationException("HTML配置窗口消息通道不存在。");
            SettingsEngineHost.Init(engine, root, libraries.ToArray(), context, channel, windowId, project.AllowJsHTTP, project.Project.Manifest.HttpAllowedUrls,
                () => SettingsWebWindow.Exists(windowId), () => SettingsWebWindow.Close(windowId));

            var code = File.ReadAllText(scriptPath);
            if (!code.Contains("import ", StringComparison.Ordinal) && !code.Contains("export ", StringComparison.Ordinal))
            {
                code = "export async function __settingsWebMain() {\n" + code + "\n}\nawait __settingsWebMain();\n";
            }

            var info = new DocumentInfo(new Uri(scriptPath)) { Category = ModuleCategory.Standard };
            var result = engine.Evaluate(info, ((PackageDocumentLoader)engine.DocumentSettings.Loader).RewriteScriptCode(code, scriptPath));
            if (result is Task task) task.GetAwaiter().GetResult();

            while (!cts.IsCancellationRequested && SettingsWebWindow.Exists(windowId))
            {
                Thread.Sleep(200);
            }
        }
        catch (OperationCanceledException)
        {
            // 用户关闭窗口时取消会话，不需要额外提示。
        }
        catch (Exception e)
        {
            App.GetLogger<ScriptProject>().LogError(e, "JS HTML配置后端退出");
            SettingsWebWindow.ShowError(windowId, e.Message);
            SettingsWebWindow.Close(windowId);
            return;
        }
        finally
        {
            Sessions.TryRemove(key, out _);
            if (!cts.IsCancellationRequested) cts.Cancel();
            try { engine?.Interrupt(); } catch { }
            engine?.Dispose();
            cts.Dispose();
        }

        SettingsWebWindow.Close(windowId);
    }
}
