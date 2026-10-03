using BetterGenshinImpact.Core.Config;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.View;
using Microsoft.ClearScript.JavaScript;
using Microsoft.Extensions.Logging;

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

    public ScrollViewer? LoadSettingUi(dynamic context)
    {
        var settingItems = Manifest.LoadSettingItems(ProjectPath);
        if (settingItems.Count == 0)
        {
            return null;
        }

        var stackPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 20, 0) // 给右侧滚动条留出位置
        };
        foreach (var item in settingItems)
        {
            var controls = item.ToControl(context);
            foreach (var control in controls)
            {
                stackPanel.Children.Add(control);
            }
        }

        var scrollViewer = new ScrollViewer
        {
            Content = stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 350 // 设置最大高度
        };

        return scrollViewer;
    }

    /// <summary>
    /// 为本次脚本执行创建并初始化独立的 V8 引擎。
    /// </summary>
    /// <param name="partyConfig">脚本使用的队伍配置。</param>
    /// <param name="ct">本次脚本执行的取消令牌。</param>
    private V8ScriptEngine BuildScriptEngine(PathingPartyConfig? partyConfig, CancellationToken ct)
    {
        V8ScriptEngine engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.EnableTaskPromiseConversion);

        // packages 依赖和资源重载
        var loader = new PackageDocumentLoader(ProjectPath);
        engine.DocumentSettings.Loader = loader;

        // 添加 packages 到搜索路径
        var libraries = new HashSet<string>(Manifest.Library ?? Array.Empty<string>())
        {
            ".",
            "./packages"
        };

        var libraryList = libraries.ToList();

        EngineExtend.InitHost(engine, ProjectPath, libraryList.ToArray(), partyConfig, ct);
        return engine;
    }

    /// <summary>
    /// 使用调用方提供的取消令牌执行当前 JS 项目。
    /// </summary>
    public async Task ExecuteAsync(dynamic? context, PathingPartyConfig? partyConfig, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 默认值
        GlobalMethod.SetGameMetrics(1920, 1080);
        // 加载代码
        var code = await LoadCode(ct);
        using var engine = BuildScriptEngine(partyConfig, ct);
        using var cancellationRegistration = ct.Register(() => TryInterrupt(engine));

        try
        {
            ct.ThrowIfCancellationRequested();

            // 使用自定义加载器解析脚本文件
            var loader = (PackageDocumentLoader)engine.DocumentSettings.Loader;

            if (context != null)
            {
                // 写入配置的内容
                engine.AddHostObject("settings", context);
            }

            bool useModule = Manifest.Library.Length != 0 ||
                             code.Contains("import ", StringComparison.Ordinal) ||
                             code.Contains("export ", StringComparison.Ordinal);

            if (useModule)
            {
                // 清除Document缓存
                DocumentLoader.Default.DiscardCachedDocuments();

                string mainScriptPath = Path.Combine(ProjectPath, Manifest.Main);
                string runtimeCode = loader.RewriteScriptCode(code, mainScriptPath);
                
                var documentInfo = new DocumentInfo(new Uri(mainScriptPath)) { Category = ModuleCategory.Standard };
                var evaluation = engine.Evaluate(documentInfo, runtimeCode);
                if (evaluation is Task task) await task;
            }
            else
            {
                var evaluation = engine.Evaluate(code);
                if (evaluation is Task task) await task;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException("JS 脚本已取消。", e, ct);
            throw;
        }
        finally
        {
            // 终止代码执行
            TryInterrupt(engine);
        }
    }

    public async Task<string> LoadCode(CancellationToken ct = default)
    {
        var code = await File.ReadAllTextAsync(Path.Combine(ProjectPath, Manifest.Main), ct);
        if (string.IsNullOrEmpty(code))
        {
            throw new FileNotFoundException("main js is empty.");
        }

        return code;
    }

    /// <summary>
    /// 尽力中断 V8 执行；引擎已经结束或释放时无需覆盖原始执行结果。
    /// </summary>
    private static void TryInterrupt(V8ScriptEngine engine)
    {
        try
        {
            engine.Interrupt();
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "中断脚本执行异常：{Message}", e.Message);
        }
    }
}
