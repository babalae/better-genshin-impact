using System;
using System.IO;

namespace BetterGenshinImpact.Core.Plugins.Pluggables;

/// <summary>
/// 判断异常是否表示插件与当前主程序二进制不兼容：插件用到的主程序类型、方法或字段已被删除或修改。
/// JIT 在首次调用方法时才解析成员，所以这类异常可能在调用时才出现。
/// </summary>
internal static class BinaryCompatibility
{
    /// <summary>
    /// 沿内部异常链查找二进制不兼容的异常。
    /// </summary>
    /// <param name="exception">调用或创建实例时抛出的异常。</param>
    public static bool IsIncompatible(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            switch (current)
            {
                // DllNotFoundException、EntryPointNotFoundException 也派生自 TypeLoadException，它们是原生库问题，与插件接口无关
                case DllNotFoundException or EntryPointNotFoundException:
                    return false;
                case TypeLoadException or MissingMemberException or FileLoadException:
                    return true;
                // 只把程序集加载失败当作不兼容：业务代码找不到模型等普通文件时也会抛 FileNotFoundException
                case FileNotFoundException notFound when notFound.FileName?.Contains("Version=", StringComparison.Ordinal) == true:
                    return true;
            }
        }

        return false;
    }
}
