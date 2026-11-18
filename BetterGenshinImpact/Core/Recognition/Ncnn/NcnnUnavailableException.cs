using System;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn 运行库或模型产物不可用：文件缺失、版本过旧、会话创建失败。
///     消息里带上可直接排查的路径与首个失败原因，供设置页与启动校验原样展示。
/// </summary>
public sealed class NcnnUnavailableException(string message) : Exception(message);
