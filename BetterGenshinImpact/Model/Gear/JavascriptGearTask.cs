using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Model.Gear.Parameter;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace BetterGenshinImpact.Model.Gear;

public class JavascriptGearTask : BaseGearTask
{
    public string ProjectPath { get; set; }
    
    public Manifest Manifest { get; set; }

    public string FolderName { get; set; }

    private JavascriptGearTaskParams _params;

    public JavascriptGearTask(JavascriptGearTaskParams paramsObj)
    {
        _params = paramsObj;
        FolderName = _params.FolderName;
        ProjectPath = Path.Combine(Global.ScriptPath(), _params.FolderName);
        if (!Directory.Exists(ProjectPath))
        {
            throw new DirectoryNotFoundException("脚本文件夹不存在:" + ProjectPath);
        }

        var manifestFile = Path.GetFullPath(Path.Combine(ProjectPath, "manifest.json"));
        if (!File.Exists(manifestFile))
        {
            throw new FileNotFoundException("manifest.json文件不存在，请确认此脚本是JS脚本类型。" + manifestFile);
        }

        Manifest = Manifest.FromJson(File.ReadAllText(manifestFile));
        Manifest.Validate(ProjectPath);

        // 基础属性
        Name = _params.FolderName;
        FilePath = Path.Combine(Global.ScriptPath(), _params.FolderName);
    }

    public override async Task Run()
    {
        await ExecuteScriptAsync(_params.Context, _params.PathingPartyConfig);
    }

    // public override string ReadDetail()
    // {
    //     throw new NotImplementedException();
    // }

    private async Task ExecuteScriptAsync(dynamic? context = null, PathingPartyConfig? partyConfig = null)
    {
        // 默认值
        GlobalMethod.SetGameMetrics(1920, 1080);
        // 加载代码
        var code = await LoadCode();
        var engine = BuildScriptEngine(partyConfig);
        if (context != null)
        {
            // 写入配置的内容
            engine.AddHostObject("settings", context);
        }

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

    private IScriptEngine BuildScriptEngine(PathingPartyConfig? partyConfig = null)
    {
        IScriptEngine engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding |
                                                  V8ScriptEngineFlags.EnableTaskPromiseConversion);
        EngineExtend.InitHost(engine, ProjectPath, Manifest.Library, partyConfig);
        return engine;
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