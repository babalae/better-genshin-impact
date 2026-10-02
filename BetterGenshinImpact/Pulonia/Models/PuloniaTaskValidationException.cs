using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 包含原计划或节点位置的校验错误，供界面定位和展示。
/// </summary>
public sealed class PuloniaTaskValidationException : Exception
{
    /// <summary>
    /// 出错的计划、节点或参数路径。
    /// </summary>
    public string Location { get; }

    /// <summary>
    /// 创建可定位的校验错误。
    /// </summary>
    public PuloniaTaskValidationException(string location, string message, Exception? innerException = null)
        : base($"{location}：{message}", innerException)
    {
        Location = location;
    }
}
