namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>树脂值对象的复制方法；C# 禁止 record 声明名为 Clone 的成员，因此使用扩展方法。</summary>
public static class ResinValueExtensions
{
    /// <summary>复制四类值；当 T 为值类型时副本完全独立，引用类型的值仍共享其引用。</summary>
    /// <typeparam name="T">每类树脂对应的值类型。</typeparam>
    /// <param name="values">要复制的值对象。</param>
    /// <returns>新的四类值容器。</returns>
    public static ResinKindValues<T> Clone<T>(this ResinKindValues<T> values) => values with { };

    /// <summary>深拷贝树脂设置及 Allowed、Counts，供配置、参数和编辑草稿独立持有。</summary>
    /// <param name="usage">要复制的设置。</param>
    /// <returns>嵌套值独立的新设置。</returns>
    public static ResinUsage Clone(this ResinUsage usage) => usage with
    {
        Allowed = usage.Allowed.Clone(),
        Counts = usage.Counts.Clone()
    };
}
