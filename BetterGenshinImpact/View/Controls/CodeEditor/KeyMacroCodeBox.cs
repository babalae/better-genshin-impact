using BetterGenshinImpact.GameTask.KeyMacro.Script;
using BetterGenshinImpact.ViewModel.Pages.KeyMacro;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml;

namespace BetterGenshinImpact.View.Controls.CodeEditor;

/// <summary>
/// 按键宏脚本编辑器：语法高亮、补全、诊断标记，以及响应 ViewModel 的插入和跳转请求。
/// 高亮规则在代码里定义，不依赖资源文件
/// </summary>
public class KeyMacroCodeBox : CodeBox
{
    /// <summary>
    /// 高亮定义，所有实例共用
    /// </summary>
    private static readonly Lazy<IHighlightingDefinition> Highlighting = new(CreateHighlighting);

    /// <summary>
    /// 要标出的诊断
    /// </summary>
    public static readonly DependencyProperty DiagnosticsProperty = DependencyProperty.Register(
        nameof(Diagnostics),
        typeof(IReadOnlyList<KeyMacroDiagnostic>),
        typeof(KeyMacroCodeBox),
        new PropertyMetadata(null, (d, e) => ((KeyMacroCodeBox)d).OnDiagnosticsChanged(e.NewValue as IReadOnlyList<KeyMacroDiagnostic>)));

    /// <summary>
    /// ViewModel 的最新请求（插入文字、跳转）
    /// </summary>
    public static readonly DependencyProperty RequestProperty = DependencyProperty.Register(
        nameof(Request),
        typeof(KeyMacroEditorRequest),
        typeof(KeyMacroCodeBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((KeyMacroCodeBox)d).OnRequest(e.NewValue as KeyMacroEditorRequest)));

    /// <summary>
    /// 诊断标记渲染器
    /// </summary>
    private readonly KeyMacroDiagnosticRenderer _diagnosticRenderer = new();

    /// <summary>
    /// 当前打开的补全窗口
    /// </summary>
    private CompletionWindow? _completionWindow;

    /// <summary>
    /// 编辑器不可见时收到的跳转请求，显示后再执行
    /// </summary>
    private KeyMacroEditorRequest? _pendingJump;

    /// <summary>
    /// 要标出的诊断
    /// </summary>
    public IReadOnlyList<KeyMacroDiagnostic>? Diagnostics
    {
        get => (IReadOnlyList<KeyMacroDiagnostic>?)GetValue(DiagnosticsProperty);
        set => SetValue(DiagnosticsProperty, value);
    }

    /// <summary>
    /// ViewModel 的最新请求
    /// </summary>
    public KeyMacroEditorRequest? Request
    {
        get => (KeyMacroEditorRequest?)GetValue(RequestProperty);
        set => SetValue(RequestProperty, value);
    }

    /// <summary>
    /// 创建编辑器
    /// </summary>
    public KeyMacroCodeBox()
    {
        SyntaxHighlighting = Highlighting.Value;
        TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);
        TextArea.TextEntered += OnTextEntered;
        TextArea.PreviewKeyDown += OnTextAreaPreviewKeyDown;
        IsVisibleChanged += (_, _) =>
        {
            // 切换到脚本视图后执行之前收到的跳转
            if (IsVisible && _pendingJump is { } pending)
            {
                _pendingJump = null;
                JumpTo(pending.Line, pending.Column, pending.Length);
            }
        };
        Options.ConvertTabsToSpaces = true;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        // 行号与代码之间留出间距
        TextArea.TextView.Margin = new Thickness(8, 2, 0, 0);
    }

    /// <summary>
    /// 诊断变化后重绘标记
    /// </summary>
    private void OnDiagnosticsChanged(IReadOnlyList<KeyMacroDiagnostic>? diagnostics)
    {
        _diagnosticRenderer.Diagnostics = diagnostics ?? [];
        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    /// <summary>
    /// 执行 ViewModel 的请求，执行后把请求清空（经双向绑定写回 ViewModel），
    /// 避免切换到其他宏再切回来时重复执行旧请求
    /// </summary>
    private void OnRequest(KeyMacroEditorRequest? request)
    {
        if (request == null)
        {
            return;
        }

        switch (request.Kind)
        {
            case KeyMacroEditorRequestKind.Insert:
                InsertLine(request.Text);
                break;
            case KeyMacroEditorRequestKind.Jump when !IsVisible:
                _pendingJump = request;
                break;
            case KeyMacroEditorRequestKind.Jump:
                JumpTo(request.Line, request.Column, request.Length);
                break;
        }

        SetCurrentValue(RequestProperty, null);
    }

    /// <summary>
    /// 插入一条指令：光标所在行为空时插在光标处，否则插在本行之后的新行
    /// </summary>
    private void InsertLine(string text)
    {
        var line = Document.GetLineByOffset(CaretOffset);
        var lineText = Document.GetText(line.Offset, line.Length);
        if (string.IsNullOrWhiteSpace(lineText))
        {
            Document.Replace(line.Offset, line.Length, text);
            CaretOffset = line.Offset + text.Length;
        }
        else
        {
            var insert = KeyMacroScriptWriter.NewLine + text;
            Document.Insert(line.EndOffset, insert);
            CaretOffset = line.EndOffset;
        }

        Focus();
        TextArea.Caret.BringCaretToView();
    }

    /// <summary>
    /// 跳到指定位置并选中
    /// </summary>
    private void JumpTo(int lineNumber, int column, int length)
    {
        if (Document.LineCount == 0)
        {
            return;
        }

        // 选择脚本视图标签页后控件才会显示，等布局完成再跳转
        Dispatcher.InvokeAsync(() =>
        {
            var line = Document.GetLineByNumber(Math.Clamp(lineNumber, 1, Document.LineCount));
            var start = line.Offset + Math.Min(column, line.Length);
            var selectLength = length > 0 ? Math.Min(length, line.EndOffset - start) : line.EndOffset - start;
            Select(start, Math.Max(0, selectLength));
            ScrollTo(line.LineNumber, column);
            Focus();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Ctrl+Space 手动打开补全
    /// </summary>
    private void OnTextAreaPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ShowCompletion(true);
        }
    }

    /// <summary>
    /// 输入字符后按上下文打开补全
    /// </summary>
    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (_completionWindow != null || e.Text.Length != 1)
        {
            return;
        }

        var c = e.Text[0];
        if (char.IsAsciiLetter(c) || c == '(' || c == ',')
        {
            ShowCompletion(false);
        }
    }

    /// <summary>
    /// 按光标前的内容决定补全列表：括号内补全按键名或鼠标键名，括号外补全指令名
    /// </summary>
    /// <param name="manual">是否由 Ctrl+Space 手动打开</param>
    private void ShowCompletion(bool manual)
    {
        var line = Document.GetLineByOffset(CaretOffset);
        var before = Document.GetText(line.Offset, CaretOffset - line.Offset);
        if (before.Contains("//", StringComparison.Ordinal) || before.TrimStart().StartsWith('#'))
        {
            return;
        }

        // 光标前正在输入的词
        var wordLength = 0;
        while (wordLength < before.Length && char.IsAsciiLetterOrDigit(before[before.Length - 1 - wordLength]))
        {
            wordLength++;
        }

        var lastOpen = Math.Max(before.LastIndexOf('('), before.LastIndexOf('（'));
        var lastClose = Math.Max(before.LastIndexOf(')'), before.LastIndexOf('）'));
        IEnumerable<KeyMacroCompletionData> items;
        if (lastOpen > lastClose)
        {
            // 括号内：取括号前的指令名
            var command = before[..lastOpen].TrimEnd();
            var nameStart = command.Length;
            while (nameStart > 0 && char.IsAsciiLetter(command[nameStart - 1]))
            {
                nameStart--;
            }

            if (!KeyMacroCommandCatalog.TryGet(command[nameStart..], out var info))
            {
                return;
            }

            // 只有第一个参数是按键名或鼠标键名；click(left, x, y) 后面的坐标不补全
            if (before.AsSpan(lastOpen + 1).IndexOfAny(',', '，') >= 0)
            {
                return;
            }

            items = info.Kind switch
            {
                KeyMacroCommandKind.KeyDown or KeyMacroCommandKind.KeyUp or KeyMacroCommandKind.KeyPress =>
                    KeyMacroKeys.CompletionKeyNames.Select(name => new KeyMacroCompletionData(name, name,
                        KeyMacroKeys.TryParseKey(name, out var vk) ? KeyMacroKeys.GetDisplayName(vk) : string.Empty)),
                KeyMacroCommandKind.MouseDown or KeyMacroCommandKind.MouseUp or KeyMacroCommandKind.Click =>
                    KeyMacroKeys.CompletionButtonNames.Select(name => new KeyMacroCompletionData(name, name,
                        KeyMacroKeys.TryParseButton(name, out var button) ? KeyMacroKeys.GetButtonDisplayName(button) : string.Empty)),
                _ => []
            };
        }
        else
        {
            // 括号外：只在开始输入一个词时补全指令名，避免每个字母都弹出
            if (!manual && wordLength != 1)
            {
                return;
            }

            items = KeyMacroCommandCatalog.All.Select(info => new KeyMacroCompletionData(info.Name, info.Name + "()",
                $"{info.Signature}  {info.Description}", 1));
        }

        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        _completionWindow = new CompletionWindow(TextArea)
        {
            StartOffset = CaretOffset - wordLength,
            CloseWhenCaretAtBeginning = true
        };
        foreach (var item in list)
        {
            _completionWindow.CompletionList.CompletionData.Add(item);
        }

        _completionWindow.Closed += (_, _) => _completionWindow = null;
        _completionWindow.Show();
        if (wordLength > 0)
        {
            _completionWindow.CompletionList.SelectItem(before[^wordLength..]);
        }
    }

    /// <summary>
    /// 生成高亮定义：指令名、按键与鼠标键名、数字、注释
    /// </summary>
    private static IHighlightingDefinition CreateHighlighting()
    {
        var keywords = new StringBuilder();
        foreach (var info in KeyMacroCommandCatalog.All)
        {
            keywords.Append("<Word>").Append(info.Name).Append("</Word>");
        }

        var xshd = $"""
                    <SyntaxDefinition name="BgiKeyMacro" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
                      <Color name="Comment" foreground="#6A9955" />
                      <Color name="Command" foreground="#3B8FD9" fontWeight="bold" />
                      <Color name="Number" foreground="#2E9E77" />
                      <Color name="Argument" foreground="#D0803A" />
                      <RuleSet ignoreCase="true">
                        <Span color="Comment" begin="//" />
                        <Span color="Comment" begin="^\s*\#" />
                        <Span begin="[\(（]" end="[\)）]">
                          <RuleSet>
                            <Span color="Comment" begin="//" />
                            <Rule color="Number">-?\d+(\.\d+)?</Rule>
                            <Rule color="Argument">[A-Za-z_][A-Za-z0-9_]*</Rule>
                          </RuleSet>
                        </Span>
                        <Keywords color="Command">{keywords}</Keywords>
                      </RuleSet>
                    </SyntaxDefinition>
                    """;
        using var reader = XmlReader.Create(new StringReader(xshd));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
