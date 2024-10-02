using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Dependence.Model;
using Microsoft.ClearScript;
using System.Reflection;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script;

public class EngineExtend
{
    public static void InitHost(IScriptEngine engine, string workDir, string[]? searchPaths = null)
    {
        // engine.AddHostObject("xHost", new ExtendedHostFunctions());  // 有越权的安全风险

        // 添加我的自定义实例化对象
        engine.AddHostObject("keyMouseScript", new KeyMouseScript(workDir));
        engine.AddHostObject("autoPathingScript", new AutoPathingScript(workDir));
        engine.AddHostObject("genshin", new Dependence.Genshin());
        engine.AddHostObject("log", new Log());
        engine.AddHostObject("file", new LimitedFile(workDir)); // 限制文件访问

        // 任务调度器
        engine.AddHostObject("dispatcher", new Dispatcher());
        engine.AddHostType("RealtimeTimer", typeof(RealtimeTimer));
        engine.AddHostType("SoloTask", typeof(SoloTask));

        // 直接添加方法
        AddAllGlobalMethod(engine);

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

    public static void AddAllGlobalMethod(IScriptEngine engine)
    {
        // 获取GlobalMethod类的所有静态方法
        var methods = typeof(GlobalMethod).GetMethods(BindingFlags.Static | BindingFlags.Public);

        foreach (var method in methods)
        {
            // 使用方法名首字母小写作为HostObject的名称
            var methodName = char.ToLowerInvariant(method.Name[0]) + method.Name.Substring(1);
            engine.AddHostObject(methodName, method);
        }
    }
}
