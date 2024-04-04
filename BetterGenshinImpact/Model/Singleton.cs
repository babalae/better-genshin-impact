using System;

namespace BetterGenshinImpact.Model;

/// <summary>
/// 由于 C# 的 DI 过于难用，bgi代码中依旧存在使用大量原始单例的对象
/// 给他们实现一个通用的单例模式
/// </summary>
/// <typeparam name="T"></typeparam>
public class Singleton<T> where T : class
{
    // 使用Lazy<T>确保线程安全的延迟初始化
    private static readonly Lazy<T> _instance = new(() => CreateInstanceOfT()!, isThreadSafe: true);

    public static T Instance => _instance.Value;

    // 保护的构造函数，防止直接实例化
    protected Singleton()
    {
    }

    private static T? CreateInstanceOfT()
    {
        return Activator.CreateInstance(typeof(T), true) as T;
    }
}
