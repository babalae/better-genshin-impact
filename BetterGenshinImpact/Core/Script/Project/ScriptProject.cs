using BetterGenshinImpact.Core.Config;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Core.Script.Dependence;
using Microsoft.ClearScript.JavaScript;
using BetterGenshinImpact.Core.Script.Repositories;

namespace BetterGenshinImpact.Core.Script.Project;

public class ScriptProject
{
    /// <summary>Pulonia 固定的来源资源上下文；传统脚本入口为空。</summary>
    private readonly ScriptRepositoryResourceContext? _repositoryResources;
    public string ProjectPath { get; set; }
    public string ManifestFile { get; set; }

    public Manifest Manifest { get; set; }

    public string FolderName { get; set; }

    /// <summary>使用原有 User/JsScript 根目录建立项目，保留旧调用入口。</summary>
    public ScriptProject(string folderName) : this(folderName, Global.ScriptPath())
    {
    }

    /// <summary>在调用方已经校验的资源或工作目录中建立项目，并继承固定来源上下文。</summary>
    public ScriptProject(string folderName, string baseDirectory, ScriptRepositoryResourceContext? repositoryResources = null)
    {
        FolderName = folderName;
        ProjectPath = Path.GetFullPath(Path.Combine(baseDirectory, folderName));
        _repositoryResources = repositoryResources;
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
        try
        {
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

            EngineExtend.InitHost(engine, ProjectPath, libraryList.ToArray(), partyConfig, ct, _repositoryResources);
            return engine;
        }
        catch
        {
            // 初始化宿主或包加载器失败时，也必须释放尚未交给执行入口的引擎。
            engine.Dispose();
            throw;
        }
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
        await ScriptExecution.ExecuteAsync(token => BuildScriptEngine(partyConfig, token), engine =>
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
                return engine.Evaluate(documentInfo, runtimeCode);
            }
            else
            {
                return engine.Evaluate(code);
            }
        }, ct).ConfigureAwait(false);
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

}
