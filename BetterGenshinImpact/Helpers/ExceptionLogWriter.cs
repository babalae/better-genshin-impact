using System;
using System.Diagnostics;
using Serilog;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 未处理异常的独立日志入口，不依赖 DI 容器；关闭期间日志设施失败也不能再次抛出异常。
/// </summary>
public sealed class ExceptionLogWriter
{
    /// <summary>获取仍由应用管理的 Serilog 日志，不从可能已释放的 IServiceProvider 解析服务。</summary>
    private readonly Func<ILogger> _getLogger;

    /// <summary>建立独立于服务容器生命周期的异常日志入口。</summary>
    public ExceptionLogWriter(Func<ILogger> getLogger)
    {
        ArgumentNullException.ThrowIfNull(getLogger);
        _getLogger = getLogger;
    }

    /// <summary>记录完整原始异常，日志不可用时保留调试输出，不引发二次未处理异常。</summary>
    public void Error(Exception exception, string message)
    {
        WriteDebug(message, exception);
        try
        {
            _getLogger().Error(exception, "{ErrorMessage}", message);
        }
        catch (Exception loggingException)
        {
            WriteDebug("记录异常日志时发生异常，原始异常已保留在调试输出中。", loggingException);
        }
    }

    /// <summary>记录异常提示，日志已经关闭或损坏时不再次触发全局异常处理。</summary>
    public void Warning(string message)
    {
        try
        {
            _getLogger().Warning("{WarningMessage}", message);
        }
        catch (Exception loggingException)
        {
            WriteDebug(message, loggingException);
        }
    }

    /// <summary>调试输出不借用应用服务；即使自定义监听器失败也不能覆盖原始异常。</summary>
    private static void WriteDebug(string message, Exception exception)
    {
        try
        {
            Debug.WriteLine(message);
            Debug.WriteLine(exception);
        }
        catch
        {
            // 异常处理的最后兜底不可递归调用日志或服务容器。
        }
    }
}
