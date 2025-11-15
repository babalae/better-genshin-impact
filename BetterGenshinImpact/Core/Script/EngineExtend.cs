using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Dependence.Model;
using Microsoft.ClearScript;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;

namespace BetterGenshinImpact.Core.Script;

public class EngineExtend
{
    /// <summary>
    /// ！！！ 注意：这个方法会添加一些全局方法和对象，不要随便添加，以免安全风险！！！
    /// </summary>
    /// <param name="engine"></param>
    /// <param name="workDir"></param>
    /// <param name="searchPaths"></param>
    public static void InitHost(IScriptEngine engine, string workDir, string[]? searchPaths = null, object? config = null)
    {
        // engine.AddHostObject("xHost", new ExtendedHostFunctions());  // 有越权的安全风险

        // 添加我的自定义实例化对象
        engine.AddHostObject("keyMouseScript", new KeyMouseScript(workDir));
        engine.AddHostObject("pathingScript", new AutoPathingScript(workDir, config));
        engine.AddHostObject("genshin", new Dependence.Genshin());
        engine.AddHostObject("log", new Log());
        engine.AddHostObject("file", new LimitedFile(workDir)); // 限制文件访问
        engine.AddHostObject("http", new Http()); // 限制文件访问
        engine.AddHostObject("notification", new Notification());

        // 任务调度器
        engine.AddHostObject("dispatcher", new Dispatcher(config));
        engine.AddHostType("RealtimeTimer", typeof(RealtimeTimer));
        engine.AddHostType("SoloTask", typeof(SoloTask));
        
        // 添加取消令牌相关类型
        engine.AddHostType("CancellationTokenSource", typeof(CancellationTokenSource));
        engine.AddHostType("CancellationToken", typeof(CancellationToken));

        // PostMessage 作为类型实例化
        engine.AddHostType("PostMessage", typeof(Dependence.Simulator.PostMessage));

        // 直接添加方法
        AddAllGlobalMethod(engine);

        // 识图模块相关
        engine.AddHostType("Mat", typeof(Mat));
        engine.AddHostType("Point2f", typeof(Point2f)); // 添加Point2f类型暴露
        engine.AddHostType("RecognitionObject", typeof(RecognitionObject));
        engine.AddHostType("DesktopRegion", typeof(DesktopRegion));
        engine.AddHostType("GameCaptureRegion", typeof(GameCaptureRegion));
        engine.AddHostType("ImageRegion", typeof(ImageRegion));
        engine.AddHostType("Region", typeof(Region));
        
        engine.AddHostType("CombatScenes", typeof(CombatScenes));
        engine.AddHostType("Avatar", typeof(Avatar));
        
        
        engine.AddHostObject("OpenCvSharp", new HostTypeCollection("OpenCvSharp"));

        engine.AddHostType("ServerTime", typeof(ServerTime));
        
        engine.AddHostType("AutoDomainParam", typeof(AutoDomainParam));  
        engine.AddHostType("AutoFightParam", typeof(AutoFightParam)); 
        


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
        // // 获取GlobalMethod类的所有静态方法
        // var methods = typeof(GlobalMethod).GetMethods(BindingFlags.Static | BindingFlags.Public);
        //
        // foreach (var method in methods)
        // {
        //     // 使用方法名首字母小写作为HostObject的名称
        //     var methodName = char.ToLowerInvariant(method.Name[0]) + method.Name[1..];
        //     engine.AddHostObject(methodName, method);
        // }

#pragma warning disable CS8974 // Converting method group to non-delegate type
        engine.AddHostObject("sleep", GlobalMethod.Sleep);
        engine.AddHostObject("keyDown", GlobalMethod.KeyDown);
        engine.AddHostObject("keyUp", GlobalMethod.KeyUp);
        engine.AddHostObject("keyPress", GlobalMethod.KeyPress);
        engine.AddHostObject("setGameMetrics", GlobalMethod.SetGameMetrics);
        engine.AddHostObject("getGameMetrics", GlobalMethod.GetGameMetrics);
        engine.AddHostObject("moveMouseBy", GlobalMethod.MoveMouseBy);
        engine.AddHostObject("moveMouseTo", GlobalMethod.MoveMouseTo);
        engine.AddHostObject("click", GlobalMethod.Click);
        engine.AddHostObject("leftButtonClick", GlobalMethod.LeftButtonClick);
        engine.AddHostObject("leftButtonDown", GlobalMethod.LeftButtonDown);
        engine.AddHostObject("leftButtonUp", GlobalMethod.LeftButtonUp);
        engine.AddHostObject("rightButtonClick", GlobalMethod.RightButtonClick);
        engine.AddHostObject("rightButtonDown", GlobalMethod.RightButtonDown);
        engine.AddHostObject("rightButtonUp", GlobalMethod.RightButtonUp);
        engine.AddHostObject("middleButtonClick", GlobalMethod.MiddleButtonClick);
        engine.AddHostObject("middleButtonDown", GlobalMethod.MiddleButtonDown);
        engine.AddHostObject("middleButtonUp", GlobalMethod.MiddleButtonUp);
        engine.AddHostObject("verticalScroll", GlobalMethod.VerticalScroll);
        engine.AddHostObject("captureGameRegion", GlobalMethod.CaptureGameRegion);
        engine.AddHostObject("getAvatars", GlobalMethod.GetAvatars);
        engine.AddHostObject("inputText", GlobalMethod.InputText);
#pragma warning restore CS8974 // Converting method group to non-delegate type
    }
}
