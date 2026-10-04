using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BetterGenshinImpact.UnitTest.HelperTests;

/// <summary>异常日志不依赖服务容器，关闭期间不能覆盖原始异常或产生二次异常。</summary>
public sealed class ExceptionLogWriterTests
{
    /// <summary>服务容器已释放时仍记录完整外层异常与内部异常，不访问已释放的服务。</summary>
    [Fact]
    public void DisposedServiceProvider_DoesNotPreventLoggingOriginalException()
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var provider = new ServiceCollection().AddSingleton<ILogger>(logger).BuildServiceProvider();
        var writer = new ExceptionLogWriter(() => logger);
        var inner = new InvalidOperationException("热键清理失败");
        var original = new Exception("关闭失败", inner);

        provider.Dispose();
        Assert.Throws<ObjectDisposedException>(() => provider.GetService<ILogger>());
        writer.Error(original, "原始关闭异常");
        writer.Warning("异常已经记录");

        Assert.Collection(sink.Events,
            entry =>
            {
                Assert.Equal(LogEventLevel.Error, entry.Level);
                Assert.Same(original, entry.Exception);
                Assert.Same(inner, entry.Exception!.InnerException);
                Assert.Contains("原始关闭异常", entry.RenderMessage());
            },
            entry =>
            {
                Assert.Equal(LogEventLevel.Warning, entry.Level);
                Assert.Contains("异常已经记录", entry.RenderMessage());
            });
    }

    /// <summary>日志获取入口已经关闭时，错误与警告都不能再次抛到全局异常处理。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableLogger_DoesNotThrow(bool warning)
    {
        var writer = new ExceptionLogWriter(() => throw new ObjectDisposedException("logger"));
        var failure = Record.Exception(() => Write(writer, warning));
        Assert.Null(failure);
    }

    /// <summary>写入日志的接收器自身失败时，也不能形成异常处理递归。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailingSink_DoesNotThrow(bool warning)
    {
        // AuditTo 会直接传播接收器异常，确保真正覆盖日志写入失败的兜底路径。
        var sink = new FailingSink();
        using var logger = new LoggerConfiguration().AuditTo.Sink(sink).CreateLogger();
        var writer = new ExceptionLogWriter(() => logger);
        var failure = Record.Exception(() => Write(writer, warning));
        Assert.Null(failure);
        Assert.Equal(1, sink.Attempts);
    }

    /// <summary>调用错误或警告入口，不加载 WPF 应用，也不替换全局日志。</summary>
    private static void Write(ExceptionLogWriter writer, bool warning)
    {
        if (warning)
            writer.Warning("关闭期间的警告");
        else
            writer.Error(new InvalidOperationException("原始异常"), "关闭期间的错误");
    }

    /// <summary>仅为断言保存日志事件，不涉及文件或全局日志状态。</summary>
    private sealed class RecordingSink : ILogEventSink
    {
        /// <summary>本次测试记录的原始事件。</summary>
        public List<LogEvent> Events { get; } = [];

        /// <summary>保留事件与原始异常对象。</summary>
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    /// <summary>模拟日志接收器已释放，不依赖真实应用的退出过程。</summary>
    private sealed class FailingSink : ILogEventSink
    {
        /// <summary>实际执行写入失败路径的次数。</summary>
        public int Attempts { get; private set; }

        /// <summary>故意传播写入失败，验证异常日志的最后兜底。</summary>
        public void Emit(LogEvent logEvent)
        {
            Attempts++;
            throw new ObjectDisposedException("sink");
        }
    }
}
