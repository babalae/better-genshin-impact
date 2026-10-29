using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace BetterGenshinImpact.Helpers;

/// <summary>合并悬浮日志文档维护，批量保留最新内容，不在TextChanged调用栈内逐条裁剪。</summary>
public sealed class OverlayLogDocument : IDisposable
{
    /// <summary>沿用既有显示上限；只限制UI，不裁剪文件日志。</summary>
    public const int MaximumInlines = 200, MaximumTextCharacters = 10000;
    /// <summary>由当前窗口拥有的实际WPF控件。</summary>
    private readonly RichTextBox _box;
    /// <summary>至多一个待执行的维护回调。</summary>
    private DispatcherOperation? _pending;
    /// <summary>维护期间抑制自身文本事件，关闭后拒绝新任务。</summary>
    private bool _maintaining, _disposed;

    /// <summary>必须在控件所属UI线程创建并拥有订阅。</summary>
    public OverlayLogDocument(RichTextBox box)
    {
        box.Dispatcher.VerifyAccess();
        _box = box;
        _box.TextChanged += OnTextChanged;
    }

    /// <summary>只投递一次后台优先级维护；不在插入事件内同步修改文档。</summary>
    private void OnTextChanged(object sender, TextChangedEventArgs args)
    {
        if (_disposed || _maintaining || _pending is not null || _box.Dispatcher.HasShutdownStarted) return;
        _pending = _box.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Maintain));
    }

    /// <summary>在一个Change块内批量保留尾部内容，最后统一滚动，避免递归和重复布局。</summary>
    private void Maintain()
    {
        _pending = null;
        if (_disposed) return;
        _maintaining = true;
        try
        {
            _box.BeginChange();
            try
            {
                var document = _box.Document;
                if (document.Blocks.LastBlock is Paragraph last
                    && (document.Blocks.Count > 1 || last.Inlines.Count > MaximumInlines))
                {
                    // 从尾部最多取200项，不逐项扫描/删除整个历史文档。
                    var retained = new List<Inline>(MaximumInlines);
                    for (var item = last.Inlines.LastInline; item is not null && retained.Count < MaximumInlines; item = item.PreviousInline)
                        retained.Add(item);
                    retained.Reverse();
                    document.Blocks.Clear();
                    var paragraph = new Paragraph { Margin = last.Margin };
                    paragraph.Inlines.AddRange(retained);
                    document.Blocks.Add(paragraph);
                }

                var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
                if (text.Length > MaximumTextCharacters)
                {
                    // 给段落结束符留空间；只在超限时退为纯文本，保留最新文字及完整Unicode字符。
                    var start = text.Length - (MaximumTextCharacters - 2);
                    if (char.IsLowSurrogate(text[start]) && start > 0 && char.IsHighSurrogate(text[start - 1])) start++;
                    document.Blocks.Clear();
                    document.Blocks.Add(new Paragraph(new Run(text[start..])));
                }
            }
            finally { _box.EndChange(); }
            _box.ScrollToEnd();
        }
        finally { _maintaining = false; }
    }

    /// <summary>窗口关闭时退订并撤销唯一待办，迟到回调也会检查已释放状态。</summary>
    public void Dispose()
    {
        _box.Dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _box.TextChanged -= OnTextChanged;
        _pending?.Abort();
        _pending = null;
    }
}
