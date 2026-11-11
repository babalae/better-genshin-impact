using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls.CodeEditor;

/// <summary>
/// 按键宏编辑器的一个补全项：指令名、按键名或鼠标键名
/// </summary>
public sealed class KeyMacroCompletionData : ICompletionData
{
    /// <summary>
    /// 插入后光标相对插入文字末尾的偏移，指令补全时把光标放进括号里
    /// </summary>
    private readonly int _caretOffsetFromEnd;

    /// <summary>
    /// 创建补全项
    /// </summary>
    /// <param name="text">用于筛选的文字</param>
    /// <param name="insertText">确认后插入的文字</param>
    /// <param name="description">说明</param>
    /// <param name="caretOffsetFromEnd">插入后光标距离插入文字末尾的字符数</param>
    public KeyMacroCompletionData(string text, string insertText, string description, int caretOffsetFromEnd = 0)
    {
        Text = text;
        InsertText = insertText;
        Description = description;
        Content = string.IsNullOrEmpty(description) ? text : $"{text}    {description}";
        _caretOffsetFromEnd = caretOffsetFromEnd;
    }

    /// <summary>
    /// 确认后插入的文字
    /// </summary>
    public string InsertText { get; }

    /// <inheritdoc />
    public ImageSource? Image => null;

    /// <inheritdoc />
    public string Text { get; }

    /// <inheritdoc />
    public object Content { get; }

    /// <inheritdoc />
    public object Description { get; }

    /// <inheritdoc />
    public double Priority => 0;

    /// <inheritdoc />
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, InsertText);
        if (_caretOffsetFromEnd > 0)
        {
            textArea.Caret.Offset = Math.Max(0, textArea.Caret.Offset - _caretOffsetFromEnd);
        }
    }
}
