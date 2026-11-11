using System;
using BetterGenshinImpact.Core.Plugins.Pluggables;

namespace BetterGenshinImpact.Core.Plugins;

/// <summary>
/// 可插拔能力的静态入口，供没有经过服务容器创建的业务代码和插件使用。
/// 经过服务容器创建的类直接在构造函数中注入可插拔能力接口即可，拿到的是同一个代理。
/// </summary>
/// <remarks>
/// 静态类 <see cref="Pluggable"/> 与特性类 <see cref="PluggableAttribute"/> 可以并存：
/// C# 解析 [Pluggable] 时只选派生自 Attribute 的类型。
/// </remarks>
public static class Pluggable
{
    /// <summary>
    /// 主程序的可插拔能力注册表。
    /// </summary>
    private static PluggableRegistry Registry =>
        App.GetService<PluggableRegistry>() ?? throw new InvalidOperationException("可插拔机制尚未初始化。");

    /// <summary>
    /// 获取可插拔能力的代理。代理长期不变，每次调用转发给当前选中的实现，可以保存在字段中。
    /// </summary>
    /// <typeparam name="T">可插拔能力接口。</typeparam>
    public static T Get<T>() where T : class => Registry.GetProxy<T>();

    /// <summary>
    /// 获取固定使用指定实现的代理；该实现不可用时回退到默认实现。
    /// </summary>
    /// <typeparam name="T">可插拔能力接口。</typeparam>
    /// <param name="implementationId">完整的实现标识，如 builtin.ppocr_v6。</param>
    public static T Get<T>(string implementationId) where T : class => Registry.GetProxy<T>(implementationId);

    /// <summary>
    /// 让当前实例在进行中的调用结束后释放，下一次调用重新创建。用于实现依赖的配置发生变化时。
    /// </summary>
    /// <typeparam name="T">可插拔能力接口。</typeparam>
    public static void Reset<T>() where T : class => Registry.Reset<T>();

    /// <summary>
    /// 按当前选中的实现创建一个不影响全局的独立实例，调用方负责释放（实现了 IDisposable 时）。
    /// </summary>
    /// <typeparam name="T">可插拔能力接口。</typeparam>
    /// <param name="args">补充或覆盖的构造参数，其余参数从服务容器解析。</param>
    public static T CreateDetached<T>(params object[] args) where T : class => Registry.CreateDetached<T>(args);
}
