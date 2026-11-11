using System;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 运行开始时无法确定运行账户（本实例当前账户未确定、续跑时当前账户与原运行不同等）：
/// 没有触碰游戏，整次运行进入待处理，消息原样展示给用户。
/// </summary>
/// <param name="message">面向用户的原因与处理提示。</param>
internal sealed class PuloniaRunAccountException(string message) : Exception(message);
