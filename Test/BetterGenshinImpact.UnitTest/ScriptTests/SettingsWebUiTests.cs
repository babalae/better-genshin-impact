using System.Dynamic;
using System.Text.Json;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Core.Script.Settings;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace BetterGenshinImpact.UnitTest.ScriptTests;

public class SettingsWebUiTests
{
    [Fact]
    public void Manifest_LoadsSettingsWebUiWithoutChangingMain()
    {
        const string json = """
                            {
                              "name": "demo",
                              "version": "1.0.0",
                              "main": "main.js",
                              "settings_ui": "settings.json",
                              "settings_web_ui": { "html": "web/index.html", "script": "settings-web.js" }
                            }
                            """;

        var manifest = Manifest.FromJson(json);

        Assert.Equal("main.js", manifest.Main);
        Assert.Equal("web/index.html", manifest.SettingsWebUi?.Html);
        Assert.Equal("settings-web.js", manifest.SettingsWebUi?.Script);
    }

    [Fact]
    public void SettingsWebUi_RejectsPathEscapeAndWrongExtension()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "ok.html"), "<html></html>");
            Assert.EndsWith("ok.html", SettingsWebUi.ResolveProjectFile(root, "ok.html", ".html"));
            Assert.Throws<ArgumentException>(() => SettingsWebUi.ResolveProjectFile(root, "../ok.html", ".html"));
            Assert.Throws<ArgumentException>(() => SettingsWebUi.ResolveProjectFile(root, "ok.html", ".js"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MessageChannel_KeepsWindowsIsolated()
    {
        var first = "settings-" + Guid.NewGuid().ToString("N");
        var second = "settings-" + Guid.NewGuid().ToString("N");
        var channel = new HtmlMessageChannel();
        channel.Register(first, _ => { });
        channel.Register(second, _ => { });
        try
        {
            channel.Send(first, "/loadConfig", """{"value":1}""");
            var messages = new List<string>();
            channel.Flush(first, messages.Add);
            Assert.Contains("\"value\":1", messages.Single());
            channel.Flush(second, _ => Assert.Fail("另一个窗口不应收到消息"));
        }
        finally
        {
            channel.Unregister(first);
            channel.Unregister(second);
        }
    }

    [Fact]
    public void SaveSettings_ReplacesOnlyCurrentProject()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var group = new ScriptGroup { Name = "group" };
            var current = new ScriptGroupProject { Name = "current", Type = "Javascript", JsScriptSettingsObject = new ExpandoObject() };
            var other = new ScriptGroupProject { Name = "other", Type = "Javascript", JsScriptSettingsObject = new ExpandoObject() };
            group.AddProject(current);
            group.AddProject(other);
            var runningSnapshot = current.JsScriptSettingsObject;

            var saved = ScriptGroupSettingsStore.Save(current, """{"enabled":true,"items":[1,null]}""", directory);

            Assert.NotSame(runningSnapshot, current.JsScriptSettingsObject);
            Assert.Same(saved, current.JsScriptSettingsObject);
            Assert.NotSame(saved, other.JsScriptSettingsObject);
            var json = File.ReadAllText(Path.Combine(directory, "group.json"));
            using var document = JsonDocument.Parse(json);
            Assert.True(document.RootElement.GetProperty("projects")[0].GetProperty("jsScriptSettingsObject").GetProperty("enabled").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SettingsUi_DoesNotExposeHtmlMaskOrWindowId()
    {
        using var engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.EnableTaskPromiseConversion);
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var project = new ScriptGroupProject { Name = "current", Type = "Javascript", JsScriptSettingsObject = new ExpandoObject() };
            var context = new SettingsScriptContext(project, CancellationToken.None, root);
            var channel = new HtmlMessageChannel();
            channel.Register("settings-window", _ => { });
            SettingsEngineHost.Init(engine, root, [], context, channel, "settings-window", false, [], () => true, () => true);

            Assert.Equal("{}", engine.Evaluate("settingsContext.loadSettings()"));
            engine.Evaluate("settingsUi.post('/state', { enabled: true, items: [1, null] })");
            var messages = new List<string>();
            channel.Flush("settings-window", messages.Add);
            Assert.Contains("\"enabled\":true", messages.Single());
            Assert.Equal(false, engine.Evaluate("typeof htmlMask !== 'undefined'"));
            Assert.Equal(false, engine.Evaluate("typeof settingsContext.windowId !== 'undefined'"));
            var error = Assert.Throws<ScriptEngineException>(() => engine.Evaluate("settingsUi.reply('window-id', 'request-id', '{}')"));
            Assert.Contains("reply", error.Message, StringComparison.OrdinalIgnoreCase);
            channel.Unregister("settings-window");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
