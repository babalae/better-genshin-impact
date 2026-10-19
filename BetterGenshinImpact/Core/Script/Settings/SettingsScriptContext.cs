using System;
using System.Dynamic;
using System.Text.Json;
using System.Threading;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Core.Script.Settings;

public sealed class SettingsScriptContext
{
    private readonly ScriptGroupProject _project;
    private readonly string _groupDirectory;
    private ExpandoObject _settings;

    public SettingsScriptContext(ScriptGroupProject project, CancellationToken cancellationToken, string groupDirectory)
    {
        _project = project;
        _groupDirectory = groupDirectory;
        CancellationToken = cancellationToken;
        _settings = Clone(project.JsScriptSettingsObject);
    }

    public CancellationToken CancellationToken { get; }

    public ExpandoObject Settings => _settings;

    public string LoadSettings() => JsonSerializer.Serialize(_settings, ConfigService.JsonOptions);

    public ExpandoObject SaveSettings(string json)
    {
        var saved = ScriptGroupSettingsStore.Save(_project, json, _groupDirectory);
        _settings = Clone(saved);
        return _settings;
    }

    private static ExpandoObject Clone(ExpandoObject? source)
    {
        return ScriptGroupSettingsStore.CloneSettings(source);
    }
}
