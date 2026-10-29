using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Threading;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.RichTextBox.Abstraction;
using Serilog.Sinks.RichTextBox.Output;
using Serilog.Sinks.RichTextBox.Themes;

namespace BetterGenshinImpact.Helpers;

/// <summary>保留现有Serilog富文本格式，用有界队列及单个异步UI批次代替无限积压。</summary>
public sealed class BoundedRichTextBoxSink : ILogEventSink, IDisposable
{
    /// <summary>待显示事件满时保留较新的通知；不会影响独立文件Sink。</summary>
    private readonly Channel<LogEvent> _events;
    /// <summary>沿用窗口提供的渲染目标。</summary>
    private readonly IRichTextBox _target;
    /// <summary>每次UI投递的最大事件数。</summary>
    private readonly int _batchSize;
    /// <summary>沿用原包的格式模板、主题和XML转义。</summary>
    private readonly XamlOutputTemplateRenderer _formatter = new(RichTextBoxConsoleTheme.Literate,
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}", null);
    /// <summary>取消队列等待和在途Dispatcher等待，不阻塞UI释放。</summary>
    private readonly CancellationTokenSource _stop = new();
    /// <summary>可重复释放标记。</summary>
    private int _disposed;
    /// <summary>拥有并观察后台泵的终止，不同步等待UI。</summary>
    public Task Completion { get; }

    /// <summary>默认最多128条待显示事件，单个UI批次不超过16条。</summary>
    public BoundedRichTextBoxSink(IRichTextBox target, int capacity = 128, int batchSize = 16)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        _target = target;
        _batchSize = batchSize;
        _events = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(capacity)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, AllowSynchronousContinuations = false });
        Completion = Task.Run(PumpAsync);
    }

    /// <summary>生产线程只进行非阻塞投递，不做XAML序列化或Dispatcher同步等待。</summary>
    public void Emit(LogEvent logEvent)
    {
        if (Volatile.Read(ref _disposed) == 0) _events.Writer.TryWrite(logEvent);
    }

    /// <summary>后台按需等待，任意时刻最多一个UI批次在途；格式/渲染失败不逃逸到应用。</summary>
    private async Task PumpAsync()
    {
        var token = _stop.Token;
        try
        {
            while (await _events.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                var text = new StringBuilder("<Paragraph xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xml:space=\"preserve\">");
                for (var count = 0; count < _batchSize && _events.Reader.TryRead(out var item); count++)
                {
                    try
                    {
                        using var writer = new StringWriter();
                        _formatter.Format(item, writer);
                        text.Append(writer);
                    }
                    catch (Exception exception) { SelfLog.WriteLine("Overlay log formatting failed: {0}", exception); }
                }
                text.Append("</Paragraph>");
                var control = _target.RichTextBox;
                try
                {
                    await _target.BeginInvoke(DispatcherPriority.Background, new Action<string>(xaml =>
                    {
                        if (Volatile.Read(ref _disposed) != 0 || !ReferenceEquals(control, _target.RichTextBox)) return;
                        try { _target.Write(xaml); }
                        catch (Exception exception) { SelfLog.WriteLine("Overlay log rendering failed: {0}", exception); }
                    }), text.ToString()).WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception) { SelfLog.WriteLine("Overlay log dispatch failed: {0}", exception); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { SelfLog.WriteLine("Overlay log pump failed: {0}", exception); }
    }

    /// <summary>释放不等待Dispatcher，已排队的迟到渲染会自行拒绝。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _events.Writer.TryComplete();
        _ = Completion.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
