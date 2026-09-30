namespace BetterGenshinImpact.Core.Input.Backends.WebSdk;

/// <summary>
/// 云原神网页版 JS SDK（Assets/JavaScript/ys-input-inject.js）的通用调用接口
/// </summary>
public interface IWebInputBridge
{
    /// <summary>
    /// 调用页面 SDK <c>window.__ysInputInject[method](...args)</c>。
    /// 不等待执行结果，页面侧按到达顺序串行执行；失败通过实现类的事件回报。
    /// 可选参数请直接省略，不要传 null：JSON 的 null 不会触发 JS 的参数默认值。
    /// </summary>
    void Invoke(string method, params object[] args);
}
