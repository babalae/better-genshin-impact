using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Dependence.Model;
using Microsoft.ClearScript;
using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script;

public class EngineExtend
{
    public static void InitHost(IScriptEngine engine, string workDir, string[]? searchPaths = null)
    {
        // engine.AddHostObject("xHost", new ExtendedHostFunctions());  // 有越权的安全风险

        // 添加我的自定义实例化对象
        engine.AddHostObject("keyMouseScript", new KeyMouseScript(workDir));
        engine.AddHostObject("autoPathing", new AutoPathing(workDir));
        engine.AddHostObject("genshin", new Dependence.Genshin());
        engine.AddHostObject("log", new Log());
        engine.AddHostObject("file", new LimitedFile(workDir)); // 限制文件访问

        // 实时任务调度器
        engine.AddHostObject("dispatcher", new Dispatcher());
        engine.AddHostType("RealtimeTimer", typeof(RealtimeTimer));

        // 直接添加方法
#pragma warning disable CS8974 // Converting method group to non-delegate type
        engine.AddHostObject("sleep", GlobalMethod.Sleep);
#pragma warning restore CS8974 // Converting method group to non-delegate type

        // 添加C#的类型
        engine.AddHostType(typeof(Task));

        // 导入 CommonJS 模块
        // https://microsoft.github.io/ClearScript/2023/01/24/module-interop.html
        // https://github.com/microsoft/ClearScript/blob/master/ClearScriptTest/V8ModuleTest.cs
        engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading | DocumentAccessFlags.AllowCategoryMismatch;
        if (searchPaths != null)
        {
            engine.DocumentSettings.SearchPath = string.Join(';', searchPaths);
        }
    }
}
