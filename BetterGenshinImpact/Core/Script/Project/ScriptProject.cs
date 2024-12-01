using BetterGenshinImpact.Core.Config;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using BetterGenshinImpact.Core.Script.Dependence;

namespace BetterGenshinImpact.Core.Script.Project;

public class ScriptProject
{
    public string ProjectPath { get; set; }
    public string ManifestFile { get; set; }

    public Manifest Manifest { get; set; }

    public string FolderName { get; set; }

    public ScriptProject(string folderName)
    {
        FolderName = folderName;
        ProjectPath = Path.Combine(Global.ScriptPath(), folderName);
        if (!Directory.Exists(ProjectPath))
        {
            throw new DirectoryNotFoundException("脚本文件夹不存在:" + ProjectPath);
        }
        ManifestFile = Path.GetFullPath(Path.Combine(ProjectPath, "manifest.json"));
        if (!File.Exists(ManifestFile))
        {
            throw new FileNotFoundException("manifest.json文件不存在，请确认此脚本是JS脚本类型。" + ManifestFile);
        }

        Manifest = Manifest.FromJson(File.ReadAllText(ManifestFile));
        Manifest.Validate(ProjectPath);
    }

    public StackPanel? LoadSettingUi(dynamic context)
    {
        var settingItems = Manifest.LoadSettingItems(ProjectPath);
        if (settingItems.Count == 0)
        {
            return null;
        }
        var stackPanel = new StackPanel();
        foreach (var item in settingItems)
        {
            var controls = item.ToControl(context);
            foreach (var control in controls)
            {
                stackPanel.Children.Add(control);
            }
        }

        return stackPanel;
    }

    public IScriptEngine BuildScriptEngine()
    {
        IScriptEngine engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.EnableTaskPromiseConversion);
        EngineExtend.InitHost(engine, ProjectPath, Manifest.Library);
        return engine;
    }

    public async Task ExecuteAsync(dynamic? context = null)
    {
        // 默认值
        GlobalMethod.SetGameMetrics(1920, 1080);
        // 加载代码
        var code = await LoadCode();
        var engine = BuildScriptEngine();
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

    public async Task<string> LoadCode()
    {
        var code = await File.ReadAllTextAsync(Path.Combine(ProjectPath, Manifest.Main));
        if (string.IsNullOrEmpty(code))
        {
            throw new FileNotFoundException("main js is empty.");
        }

        return code;
    }
}
