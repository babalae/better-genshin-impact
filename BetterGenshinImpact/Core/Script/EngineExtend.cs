using System;
using System.Collections.Generic;
using System.Drawing;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Dependence.Model;
using Microsoft.ClearScript;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Core.BgiVision;
using OpenCvSharp;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Core.Script.Utils;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.Common.Reward;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.CharacterDevelopment;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using Region = BetterGenshinImpact.GameTask.Model.Area.Region;

namespace BetterGenshinImpact.Core.Script;

public class EngineExtend
{
    /// <summary>
    /// 把需要运行环境的宿主类型包装成同名 JS 构造函数的引导脚本。
    /// <para>
    /// 每个类型的原始宿主类型以 <c>__bgiHostType_类型名</c> 注册，同名全局变量换成一个 Proxy 包装的 JS 函数：
    /// 1. <c>new X(...args)</c> / <c>X(...args)</c> 转发到绑定了运行环境的工厂 <c>__bgiHostTypeFactory.CreateX(...args)</c>
    ///    （GameCaptureRegion 的构造不需要运行环境，直接构造原始宿主类型）；
    /// 2. 需要运行环境的静态方法（按名称大小写不敏感匹配，与引擎的成员绑定规则一致）转发到工厂中的对应方法；
    /// 3. 其余静态成员（如 BvLocator.DefaultTimeout、Avatar.ParseActionSchedulerByCd）的读写转发到原始宿主类型。
    /// </para>
    /// </summary>
    private const string RuntimeBoundHostTypesScript = """
        (() => {
            'use strict';
            const factory = __bgiHostTypeFactory;
            const findStatic = (statics, prop) => {
                if (!statics || typeof prop !== 'string') {
                    return undefined;
                }
                const lower = prop.toLowerCase();
                const key = Object.keys(statics).find(k => k.toLowerCase() === lower);
                return key === undefined ? undefined : statics[key];
            };
            const define = (name, hostType, create, statics) => {
                const ctor = function (...args) {
                    return create(...args);
                };
                globalThis[name] = new Proxy(ctor, {
                    get: (target, prop, receiver) => {
                        const bound = findStatic(statics, prop);
                        if (bound !== undefined) {
                            return bound;
                        }
                        return (prop in target) ? Reflect.get(target, prop, receiver) : hostType[prop];
                    },
                    set: (target, prop, value, receiver) => {
                        if (prop in target) {
                            return Reflect.set(target, prop, value, receiver);
                        }
                        hostType[prop] = value;
                        return true;
                    }
                });
            };
            define('BvPage', __bgiHostType_BvPage, (...a) => factory.CreateBvPage(...a));
            define('BvLocator', __bgiHostType_BvLocator, (...a) => factory.CreateBvLocator(...a));
            define('BvImage', __bgiHostType_BvImage, (...a) => factory.CreateBvImage(...a));
            define('KeyMouseHook', __bgiHostType_KeyMouseHook, (...a) => factory.CreateKeyMouseHook(...a));
            define('CombatScenes', __bgiHostType_CombatScenes, (...a) => factory.CreateCombatScenes(...a), {
                GetCombatScenesWithRetry: () => factory.GetCombatScenesWithRetry(),
                FromAvatars: (avatars) => factory.CombatScenesFromAvatars(avatars)
            });
            define('Avatar', __bgiHostType_Avatar, (...a) => factory.CreateAvatar(...a), {
                ThrowWhenDefeated: (region, ct) => factory.AvatarThrowWhenDefeated(region, ct),
                TpForRecover: (ct, retryException) => factory.AvatarTpForRecover(ct, retryException)
            });
            // GameCaptureRegion 的构造不需要运行环境，直接交给原始宿主类型；原来的五个坐标静态方法都需要绑定运行环境
            define('GameCaptureRegion', __bgiHostType_GameCaptureRegion, (...a) => new __bgiHostType_GameCaptureRegion(...a), {
                GameRegionClick: (posFunc) => factory.GameRegionClick(posFunc),
                GameRegionMove: (posFunc) => factory.GameRegionMove(posFunc),
                GameRegionMoveBy: (deltaFunc) => factory.GameRegionMoveBy(deltaFunc),
                GameRegion1080PPosClick: (x, y) => factory.GameRegion1080PPosClick(x, y),
                GameRegion1080PPosMove: (x, y) => factory.GameRegion1080PPosMove(x, y)
            });
        })();
        """;

    /// <summary>
    /// ！！！ 注意：这个方法会添加一些全局方法和对象，不要随便添加，以免安全风险！！！
    /// <para>
    /// 本方法属于编排层（L4）：宿主对象（L3）需要的进程级服务（如截图服务）在这里获取后通过构造函数交给它们，
    /// 宿主对象本身不使用 App.GetService。
    /// </para>
    /// </summary>
    /// <param name="engine"></param>
    /// <param name="runtime">本次脚本运行使用的运行环境，所有宿主对象都绑定到它。</param>
    /// <param name="allConfig">配置根。</param>
    /// <param name="workDir"></param>
    /// <param name="searchPaths"></param>
    /// <param name="config">当前脚本使用的可选配置。</param>
    /// <param name="ct">当前脚本执行的取消令牌。</param>
    /// <param name="repositoryResources">Pulonia 已确认来源；为空时保留传统用户目录语义。</param>
    public static void InitHost(IScriptEngine engine, GameRuntime runtime, AllConfig allConfig, string workDir,
        string[]? searchPaths, object? config,
        CancellationToken ct, ScriptRepositoryResourceContext? repositoryResources = null)
    {
        // engine.AddHostObject("xHost", new ExtendedHostFunctions());  // 有越权的安全风险

        // 截图服务是进程级 DI 单例；InitHost 是编排层，在这里取出后交给需要的宿主对象（自动钓鱼的关键帧截图）
        var screenshots = App.GetService<GameScreenshotService>();

        // 添加我的自定义实例化对象
        engine.AddHostObject("keyMouseScript", new KeyMouseScript(runtime, workDir, ct));
        engine.AddHostObject("pathingScript", new AutoPathingScript(runtime, allConfig, workDir, config, ct, repositoryResources));
        engine.AddHostObject("genshin", new Dependence.Genshin(runtime, allConfig, ct, screenshots));
        engine.AddHostObject("characterDevelopmentTask", new CharacterDevelopmentTask(runtime, ct));
        engine.AddHostObject("log", new Log());
        engine.AddHostObject("file", new LimitedFile(workDir)); // 限制文件访问
        engine.AddHostObject("http", new Http()); // 限制网络访问
        engine.AddHostObject("yolo", new YoloPredictorFactory(runtime, workDir)); // YOLO ONNX 推理
        engine.AddHostObject("notification", new Notification(allConfig));

        // 任务调度器
        engine.AddHostObject("dispatcher", new Dispatcher(runtime, allConfig, config, ct, screenshots));
        engine.AddHostType("RealtimeTimer", typeof(RealtimeTimer));
        engine.AddHostType("SoloTask", typeof(SoloTask));
        engine.AddHostType("AutoSkipConfig", typeof(AutoSkipConfig));

        // 添加取消令牌相关类型
        engine.AddHostType("CancellationTokenSource", typeof(CancellationTokenSource));
        engine.AddHostType("CancellationToken", typeof(CancellationToken));

        // PostMessage 作为类型实例化
        engine.AddHostType("PostMessage", typeof(Dependence.Simulator.PostMessage));

        // 直接添加方法（每个引擎一个 GlobalMethod 实例，setGameMetrics 的设置只作用于本脚本）
        AddAllGlobalMethod(engine, new GlobalMethod(runtime, allConfig), ct);

        // 识图模块相关
        engine.AddHostType("Mat", typeof(Mat));
        engine.AddHostType("Point2f", typeof(Point2f)); // 添加Point2f类型暴露
        engine.AddHostType("RecognitionObject", typeof(RecognitionObject));
        engine.AddHostType("DesktopRegion", typeof(DesktopRegion));
        // GameCaptureRegion 原有的五个坐标静态方法需要运行环境，在下方 RegisterRuntimeBoundHostTypes 中统一包装
        engine.AddHostType("ImageRegion", typeof(ImageRegion));
        engine.AddHostType("Region", typeof(Region));

        engine.AddHostType("Pen", typeof(Pen));
        engine.AddHostType("Color", typeof(Color));

        // CombatScenes、Avatar 的构造需要运行环境，在下方 RegisterRuntimeBoundHostTypes 中统一包装


        engine.AddHostObject("OpenCvSharp", new HostTypeCollection("OpenCvSharp"));

        engine.AddHostType("ServerTime", typeof(ServerTime));

        engine.AddHostType("AutoDomainParam", typeof(AutoDomainParam));
        engine.AddHostType("AutoBossParam", typeof(AutoBossParam));
        engine.AddHostType("CountInventoryItemParam", typeof(CountInventoryItemParam));
        engine.AddHostType("GridScreenName", typeof(GridScreenName));
        engine.AddHostType("ItemIconRecognitionMode", typeof(ItemIconRecognitionMode));
        engine.AddHostType("AutoFightParam", typeof(AutoFightParam));
        engine.AddHostType("AutoLeyLineOutcropParam", typeof(AutoLeyLineOutcropParam));
        engine.AddHostType("TargetItem", typeof(TargetItem));
        engine.AddHostType("AutoStygianOnslaughtParam", typeof(AutoStygianOnslaughtParam));
        // 统一树脂设置：新脚本可直接修改 param.ResinUsage（如 Mode、RunCount、Allowed、Counts、OriginalTier），
        // 旧的 SpecifyResinUse、RunCount、IsResinExhaustionMode 等兼容属性继续可用
        engine.AddHostType("ResinUsage", typeof(ResinUsage));
        engine.AddHostType("ResinUsageMode", typeof(ResinUsageMode));
        engine.AddHostType("OriginalResinTier", typeof(OriginalResinTier));
        engine.AddHostType("ResinKind", typeof(ResinKind));
        engine.AddHostObject("strategyFile", new StrategyFile());
        // 添加C#的类型
        engine.AddHostType(typeof(Task));

        // 需要运行环境的可 new 类型：鼠标回调 KeyMouseHook、新的 BvPage 类（BvPage、BvLocator、BvImage）、CombatScenes、Avatar，
        // 以及静态方法需要运行环境的 GameCaptureRegion
        RegisterRuntimeBoundHostTypes(engine, runtime, allConfig);

        engine.AddHostObject("host", new CustomHostFunctions());

        // HTML 遮罩
        engine.AddHostObject("htmlMask", new HtmlMask(workDir));

        // 导入 JavaScript 模块
        // https://microsoft.github.io/ClearScript/2023/01/24/module-interop.html
        // https://github.com/microsoft/ClearScript/blob/master/ClearScriptTest/V8ModuleTest.cs
        engine.DocumentSettings.AccessFlags = DocumentAccessFlags.AllowCategoryMismatch;
        if (searchPaths != null)
        {
            var normalizedPaths = new List<string>();
            foreach (var path in searchPaths)
            {
                try
                {
                    var normalizedPath = ScriptUtils.NormalizePath(workDir, path);
                    normalizedPaths.Add(normalizedPath);
                }
                catch (Exception ex)
                {
                    throw new Exception($"从 library 字段读取路径 '{path}' 失败: {ex.Message}", ex);
                }
            }

            if (normalizedPaths.Count > 0)
            {
                engine.DocumentSettings.SearchPath = string.Join(';', normalizedPaths);
            }
        }
    }

    /// <summary>
    /// 注册脚本可以直接 new、但构造需要运行环境的宿主类型。
    /// 原始宿主类型改用隐藏名称注册，同名全局变量换成转发到工厂的 JS 构造函数（见 <see cref="RuntimeBoundHostTypesScript"/>），
    /// 脚本中 <c>new BvPage()</c> 等写法保持不变。
    /// </summary>
    /// <param name="engine">脚本引擎</param>
    /// <param name="runtime">本次脚本运行使用的运行环境</param>
    /// <param name="allConfig">配置根</param>
    private static void RegisterRuntimeBoundHostTypes(IScriptEngine engine, GameRuntime runtime, AllConfig allConfig)
    {
        // 1. 绑定了运行环境的工厂对象
        engine.AddHostObject("__bgiHostTypeFactory", new ScriptHostTypeFactory(runtime, allConfig));

        // 2. 原始宿主类型以隐藏名称注册，供 JS 包装函数转发其余静态成员
        engine.AddHostType("__bgiHostType_BvPage", typeof(BvPage));
        engine.AddHostType("__bgiHostType_BvLocator", typeof(BvLocator));
        engine.AddHostType("__bgiHostType_BvImage", typeof(BvImage));
        engine.AddHostType("__bgiHostType_KeyMouseHook", typeof(KeyMouseHook));
        engine.AddHostType("__bgiHostType_CombatScenes", typeof(CombatScenes));
        engine.AddHostType("__bgiHostType_Avatar", typeof(Avatar));
        engine.AddHostType("__bgiHostType_GameCaptureRegion", typeof(GameCaptureRegion));

        // 3. 定义同名 JS 构造函数
        engine.Execute(RuntimeBoundHostTypesScript);
    }

    /// <summary>
    /// 注册兼容现有脚本名称的全局方法，并为异步等待绑定本次脚本令牌。
    /// 依赖运行环境与脚本游戏尺寸的方法注册为 <paramref name="globalMethod"/> 的实例方法，其余为静态方法；JS 侧函数名与参数不变。
    /// </summary>
    /// <param name="engine">脚本引擎</param>
    /// <param name="globalMethod">本引擎专属的全局方法实例</param>
    /// <param name="ct">本次脚本执行的取消令牌</param>
    public static void AddAllGlobalMethod(IScriptEngine engine, GlobalMethod globalMethod, CancellationToken ct)
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
        engine.AddHostObject("sleep", new Func<int, Task>(millisecondsTimeout =>
            GlobalMethod.Sleep(millisecondsTimeout, ct)));
        engine.AddHostObject("getVersion", GlobalMethod.GetVersion);
        engine.AddHostObject("keyDown", GlobalMethod.KeyDown);
        engine.AddHostObject("keyUp", GlobalMethod.KeyUp);
        engine.AddHostObject("keyPress", GlobalMethod.KeyPress);
        engine.AddHostObject("setGameMetrics", globalMethod.SetGameMetrics);
        engine.AddHostObject("getGameMetrics", globalMethod.GetGameMetrics);
        engine.AddHostObject("moveMouseBy", globalMethod.MoveMouseBy);
        engine.AddHostObject("moveMouseTo", globalMethod.MoveMouseTo);
        engine.AddHostObject("click", globalMethod.Click);
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
        engine.AddHostObject("captureGameRegion", globalMethod.CaptureGameRegion);
        engine.AddHostObject("getAvatars", globalMethod.GetAvatars);
        engine.AddHostObject("inputText", GlobalMethod.InputText);
#pragma warning restore CS8974 // Converting method group to non-delegate type
    }
}
