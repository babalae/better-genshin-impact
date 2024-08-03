using System;
using System.Diagnostics;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using System.IO;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Project;

public class ScriptProject
{
    public string ProjectPath { get; set; }
    public string ManifestFile { get; set; }

    public Manifest Manifest { get; set; }

    public ScriptProject(string path)
    {
        ProjectPath = path;
        ManifestFile = Path.GetFullPath(Path.Combine(path, "manifest.json"));
        if (!File.Exists(ManifestFile))
        {
            throw new FileNotFoundException("manifest.json file not found.");
        }

        Manifest = Manifest.FromJson(File.ReadAllText(ManifestFile));
    }

    public IScriptEngine BuildScriptEngine()
    {
        IScriptEngine engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.EnableTaskPromiseConversion);
        EngineExtend.InitHost(engine, ProjectPath, Manifest.Library);
        return engine;
    }

    public async Task ExecuteAsync()
    {
        var code = await LoadCode();
        var engine = BuildScriptEngine();
        try
        {
            await (Task)engine.Evaluate(code);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            throw;
        }
        finally
        {
            engine.Dispose();
        }
    }

    private async Task<string> LoadCode()
    {
        var code = await File.ReadAllTextAsync(Path.Combine(ProjectPath, Manifest.Main));
        if (string.IsNullOrEmpty(code))
        {
            throw new FileNotFoundException("main js is empty.");
        }

        return code;
    }
}
