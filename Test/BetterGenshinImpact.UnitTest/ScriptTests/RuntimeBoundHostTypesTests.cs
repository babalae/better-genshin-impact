using System.Reflection;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Dependence;
using Microsoft.ClearScript.V8;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.ScriptTests;

/// <summary>
/// 用真实 V8 验证运行环境宿主的旧坐标 API，工厂使用替身，不初始化应用或发送输入。
/// </summary>
public sealed class RuntimeBoundHostTypesTests
{
    /// <summary>
    /// 五个旧入口均须保持可调用、大小写兼容，并原样传递坐标或位置计算委托。
    /// </summary>
    [Theory]
    [InlineData(nameof(ScriptHostTypeFactory.GameRegionClick))]
    [InlineData(nameof(ScriptHostTypeFactory.GameRegionMove))]
    [InlineData(nameof(ScriptHostTypeFactory.GameRegionMoveBy))]
    [InlineData(nameof(ScriptHostTypeFactory.GameRegion1080PPosClick))]
    [InlineData(nameof(ScriptHostTypeFactory.GameRegion1080PPosMove))]
    public void CoordinateStatics_PreserveLegacyCalls(string method)
    {
        using var engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding);
        var factory = new CoordinateFactoryProbe();
        engine.AddHostObject("__bgiHostTypeFactory", factory);
        foreach (var name in new[] { "BvPage", "BvLocator", "BvImage", "KeyMouseHook", "CombatScenes", "Avatar", "GameCaptureRegion" })
        {
            engine.AddHostType("__bgiHostType_" + name, typeof(HostTypeProbe));
        }

        // 直接使用生产引导脚本，避免测试中的副本漏掉新增或删除的映射。
        var bootstrap = typeof(EngineExtend).GetField("RuntimeBoundHostTypesScript", BindingFlags.NonPublic | BindingFlags.Static)!;
        engine.Execute((string)bootstrap.GetRawConstantValue()!);
        Func<Size, double, (double, double)> position = (size, scale) => (size.Width * scale, size.Height * scale);
        engine.AddHostObject("position", position);
        var uses1080P = method.Contains("1080P", StringComparison.Ordinal);

        foreach (var exposedName in new[] { method, method.ToLowerInvariant() })
        {
            Assert.Equal("function", engine.Evaluate($"typeof GameCaptureRegion.{exposedName}"));
            engine.Execute($"GameCaptureRegion.{exposedName}({(uses1080P ? "420, 360" : "position")})");
            Assert.Equal(method, factory.LastMethod);
            if (uses1080P)
            {
                Assert.Equal((420d, 360d), factory.LastCoordinates);
            }
            else
            {
                Assert.Same(position, factory.LastPosition);
            }
        }
    }

    /// <summary>原始宿主类型替身，保留由生产脚本包装所需的构造入口。</summary>
    public sealed class HostTypeProbe;

    /// <summary>只记下调用及参数，不执行鼠标操作的宿主工厂。</summary>
    public sealed class CoordinateFactoryProbe
    {
        /// <summary>最近一次调用的方法。</summary>
        public string? LastMethod { get; private set; }

        /// <summary>最近一次传入的位置计算委托。</summary>
        public Func<Size, double, (double, double)>? LastPosition { get; private set; }

        /// <summary>最近一次传入的 1080P 坐标。</summary>
        public (double, double) LastCoordinates { get; private set; }

        /// <summary>记录旧点击入口的委托。</summary>
        public void GameRegionClick(Func<Size, double, (double, double)> position)
        {
            LastMethod = nameof(GameRegionClick);
            LastPosition = position;
        }

        /// <summary>记录旧绝对移动入口的委托。</summary>
        public void GameRegionMove(Func<Size, double, (double, double)> position)
        {
            LastMethod = nameof(GameRegionMove);
            LastPosition = position;
        }

        /// <summary>记录旧相对移动入口的委托。</summary>
        public void GameRegionMoveBy(Func<Size, double, (double, double)> position)
        {
            LastMethod = nameof(GameRegionMoveBy);
            LastPosition = position;
        }

        /// <summary>记录 1080P 点击坐标。</summary>
        public void GameRegion1080PPosClick(double x, double y)
        {
            LastMethod = nameof(GameRegion1080PPosClick);
            LastCoordinates = (x, y);
        }

        /// <summary>记录 1080P 绝对移动坐标。</summary>
        public void GameRegion1080PPosMove(double x, double y)
        {
            LastMethod = nameof(GameRegion1080PPosMove);
            LastCoordinates = (x, y);
        }
    }
}
