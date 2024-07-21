using BetterGenshinImpact.Script.Dependence;
using System.Threading.Tasks;
using Microsoft.ClearScript;

namespace BetterGenshinImpact.Script;

public class EngineExtend
{
    public static void InitHost(IScriptEngine engine)
    {
        // engine.AddHostObject("xHost", new ExtendedHostFunctions());  // 有越权的安全风险

        // 添加我的自定义实例化对象
        engine.AddHostObject("gi", new Script.Dependence.Genshin());
        engine.AddHostObject("log", new Log());

        // 添加方法
#pragma warning disable CS8974 // Converting method group to non-delegate type
        engine.AddHostObject("sleep", GlobalMethod.Sleep);
#pragma warning restore CS8974 // Converting method group to non-delegate type

        // 添加C#的类型
        engine.AddHostType(typeof(Task));
    }
}
