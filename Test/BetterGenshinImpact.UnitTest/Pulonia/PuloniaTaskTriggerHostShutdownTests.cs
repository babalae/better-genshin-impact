using System.Reflection;
using System.Windows;
using BetterGenshinImpact.Pulonia.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>关闭回调不能依赖 Application.Current；不创建应用窗口或注册系统热键。</summary>
public sealed class PuloniaTaskTriggerHostShutdownTests
{
    /// <summary>Application.Current 已为空时，Dispatcher 关闭回调与重复清理均不能抛异常。</summary>
    [Fact]
    public void ShutdownWithoutApplication_CleanupIsSafeAndIdempotent()
    {
        Assert.Null(Application.Current);
        // 清理路径不使用存储、任务服务或实例服务，避免为了测试启动真实应用。
        using var host = new PuloniaTaskTriggerHost(null!, null!, null!,
            NullLogger<PuloniaTaskTriggerHost>.Instance, TimeProvider.System);
        var shutdownMethod = typeof(PuloniaTaskTriggerHost).GetMethod("OnDispatcherShutdown", BindingFlags.Instance | BindingFlags.NonPublic);
        var releaseMethod = typeof(PuloniaTaskTriggerHost).GetMethod("ReleaseHotkeys", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(shutdownMethod);
        Assert.NotNull(releaseMethod);
        var shutdown = (EventHandler)shutdownMethod.CreateDelegate(typeof(EventHandler), host);
        var release = (Action)releaseMethod.CreateDelegate(typeof(Action), host);

        shutdown(null, EventArgs.Empty);
        release();
        shutdown(null, EventArgs.Empty);

        Assert.Null(Application.Current);
    }
}
