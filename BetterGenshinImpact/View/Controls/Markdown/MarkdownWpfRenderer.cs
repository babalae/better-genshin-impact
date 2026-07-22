using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Figures;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MdBlock = Markdig.Syntax.Block;
using MdTable = Markdig.Extensions.Tables.Table;
using WpfBlock = System.Windows.Documents.Block;
using WpfList = System.Windows.Documents.List;
using WpfTable = System.Windows.Documents.Table;

namespace BetterGenshinImpact.View.Controls.Markdown;

internal sealed class MarkdownWpfRenderer
{
    private static readonly double[] HeadingFontSizes = [30, 25, 21, 18, 16, 14];
    private static readonly Regex ClosingHtmlTagRegex = new(
        "^<\\s*/\\s*([a-zA-Z0-9]+)\\s*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OpeningHtmlTagRegex = new(
        "^<\\s*([a-zA-Z0-9]+)(?:\\s+[^>]*)?/?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DangerousHtmlContentRegex = new(
        "<(script|style|iframe)\\b[^>]*>.*?</\\1\\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex HtmlLineBreakRegex = new(
        "<(br|hr)\\s*/?>|</(p|div|li|tr|h[1-6]|details|summary)\\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnyHtmlTagRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly MarkdownRenderPlan _plan;
    private readonly MarkdownView _owner;
    private readonly IMarkdownImageLoader _imageLoader;
    private readonly CancellationToken _cancellationToken;
    private readonly List<MarkdownHeading> _outline = [];
    private readonly List<MarkdownDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, FrameworkContentElement> _anchors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _slugCounts = new(StringComparer.OrdinalIgnoreCase);

    public MarkdownWpfRenderer(
        MarkdownRenderPlan plan,
        MarkdownView owner,
        IMarkdownImageLoader imageLoader,
        CancellationToken cancellationToken)
    {
        _plan = plan;
        _owner = owner;
        _imageLoader = imageLoader;
        _cancellationToken = cancellationToken;
    }

    public MarkdownRenderResult Render()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var document = new FlowDocument
        {
            Background = Brushes.Transparent,
            PagePadding = new Thickness(20, 16, 20, 24),
            ColumnWidth = double.PositiveInfinity,
            FontSize = 14,
            LineHeight = 22
        };
        document.SetResourceReference(TextElement.FontFamilyProperty, "TextThemeFontFamily");
        document.SetResourceReference(TextElement.ForegroundProperty, "MarkdownForegroundBrush");

        foreach (var block in _plan.Document)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var renderedBlock in RenderBlock(block))
                {
                    document.Blocks.Add(renderedBlock);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownDiagnosticSeverity.Warning,
                    $"节点 {block.GetType().Name} 渲染失败，已按纯文本降级：{ex.Message}",
                    GetLine(block)));
                document.Blocks.Add(CreateFallbackParagraph(GetBlockText(block)));
            }
        }

        ConfigureEmojiRendering(document);

        return new MarkdownRenderResult(document, _outline, _diagnostics, _anchors);
    }

    private void ConfigureEmojiRendering(FlowDocument document)
    {
        if (!_plan.Options.EnableEmojiRendering)
        {
            return;
        }

        RoutedEventHandler? loadedHandler = null;
        loadedHandler = async (_, _) =>
        {
            document.Loaded -= loadedHandler;
            try
            {
                await RenderEmojiAsync(document);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _owner.ReportResourceDiagnostic(new MarkdownDiagnostic(
                    MarkdownDiagnosticSeverity.Warning,
                    $"Emoji 彩色渲染失败，已回退为系统字体：{ex.Message}"));
            }
        };
        document.Loaded += loadedHandler;
    }

    private async Task RenderEmojiAsync(FlowDocument document)
    {
        var frameBudget = TimeSpan.FromMilliseconds(4);
        Exception? firstException = null;
        var runs = EnumerateRuns(document.Blocks).ToArray();

        // 先让文档完成首帧布局，再逐批替换 Emoji，避免阻塞抽屉动画和内容呈现。
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        var frameStartedAt = Stopwatch.GetTimestamp();
        for (var index = 0; index < runs.Length; index++)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var run = runs[index];
            try
            {
                Emoji.Wpf.FlowDocumentExtensions.SubstituteGlyphs(run);
            }
            catch (Exception ex)
            {
                firstException ??= ex;
            }

            if (Stopwatch.GetElapsedTime(frameStartedAt) >= frameBudget)
            {
                await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
                frameStartedAt = Stopwatch.GetTimestamp();
            }
        }

        if (firstException is not null)
        {
            _owner.ReportResourceDiagnostic(new MarkdownDiagnostic(
                MarkdownDiagnosticSeverity.Warning,
                $"部分 Emoji 彩色渲染失败，已回退为系统字体：{firstException.Message}"));
        }
    }

    private static IEnumerable<Run> EnumerateRuns(BlockCollection blocks)
    {
        foreach (WpfBlock block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach (var run in EnumerateRuns(paragraph.Inlines))
                    {
                        yield return run;
                    }

                    break;
                case Section section:
                    foreach (var run in EnumerateRuns(section.Blocks))
                    {
                        yield return run;
                    }

                    break;
                case WpfList list:
                    foreach (var listItem in list.ListItems)
                    {
                        foreach (var run in EnumerateRuns(listItem.Blocks))
                        {
                            yield return run;
                        }
                    }

                    break;
                case WpfTable table:
                    foreach (var rowGroup in table.RowGroups)
                    {
                        foreach (var row in rowGroup.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                foreach (var run in EnumerateRuns(cell.Blocks))
                                {
                                    yield return run;
                                }
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static IEnumerable<Run> EnumerateRuns(InlineCollection inlines)
    {
        foreach (System.Windows.Documents.Inline inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    yield return run;
                    break;
                case Span span:
                    foreach (var nestedRun in EnumerateRuns(span.Inlines))
                    {
                        yield return nestedRun;
                    }

                    break;
            }
        }
    }

    private IEnumerable<WpfBlock> RenderBlock(MdBlock block)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        switch (block)
        {
            case YamlFrontMatterBlock:
                yield break;
            case HeadingBlock headingBlock:
                yield return RenderHeading(headingBlock);
                yield break;
            case ParagraphBlock paragraphBlock:
                yield return RenderParagraph(paragraphBlock);
                yield break;
            case AlertBlock alertBlock:
                yield return RenderAlert(alertBlock);
                yield break;
            case QuoteBlock quoteBlock:
                yield return RenderQuote(quoteBlock);
                yield break;
            case ListBlock listBlock:
                yield return RenderList(listBlock);
                yield break;
            case ThematicBreakBlock:
                yield return RenderThematicBreak();
                yield break;
            case MdTable table:
                yield return RenderTable(table);
                yield break;
            case FootnoteGroup footnoteGroup:
                yield return RenderFootnoteGroup(footnoteGroup);
                yield break;
            case DefinitionList definitionList:
                yield return RenderDefinitionList(definitionList);
                yield break;
            case Markdig.Extensions.Figures.Figure figure:
                yield return RenderFigure(figure);
                yield break;
            case FigureCaption figureCaption:
                yield return RenderFigureCaption(figureCaption);
                yield break;
            case MathBlock mathBlock:
                _diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownDiagnosticSeverity.Information,
                    "数学公式渲染器尚未配置，已显示 LaTeX 源码。",
                    GetLine(mathBlock)));
                yield return RenderCodeBlock(mathBlock, "LaTeX");
                yield break;
            case FencedCodeBlock fencedCodeBlock:
                var language = fencedCodeBlock.Info?.Trim() ?? string.Empty;
                if (language.Equals("mermaid", StringComparison.OrdinalIgnoreCase)
                    || language.Equals("nomnoml", StringComparison.OrdinalIgnoreCase))
                {
                    _diagnostics.Add(new MarkdownDiagnostic(
                        MarkdownDiagnosticSeverity.Information,
                        $"{language} 图表渲染器尚未配置，已显示源码。",
                        GetLine(fencedCodeBlock)));
                }

                yield return RenderCodeBlock(fencedCodeBlock, language);
                yield break;
            case CodeBlock codeBlock:
                yield return RenderCodeBlock(codeBlock, string.Empty);
                yield break;
            case HtmlBlock htmlBlock:
                yield return RenderHtmlBlock(htmlBlock);
                yield break;
            case ContainerBlock containerBlock:
                var section = CreateSection();
                foreach (var child in containerBlock)
                {
                    foreach (var childBlock in RenderBlock(child))
                    {
                        section.Blocks.Add(childBlock);
                    }
                }

                yield return section;
                yield break;
            case LeafBlock leafBlock when leafBlock.Inline is not null:
                var paragraph = CreateParagraph();
                RenderInlineContainer(leafBlock.Inline, paragraph.Inlines);
                yield return paragraph;
                yield break;
            default:
                yield return CreateFallbackParagraph(GetBlockText(block));
                yield break;
        }
    }

    private Paragraph RenderHeading(HeadingBlock headingBlock)
    {
        var text = ExtractInlineText(headingBlock.Inline);
        var anchor = CreateUniqueSlug(text);
        var level = Math.Clamp(headingBlock.Level, 1, 6);
        var paragraph = new Paragraph
        {
            FontSize = HeadingFontSizes[level - 1],
            FontWeight = level <= 2 ? FontWeights.SemiBold : FontWeights.Medium,
            Margin = new Thickness(0, level == 1 ? 8 : 18, 0, level <= 2 ? 12 : 8),
            Padding = level <= 2 ? new Thickness(0, 0, 0, 7) : new Thickness(0),
            KeepWithNext = true,
            Tag = anchor
        };
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "MarkdownForegroundBrush");
        if (level <= 2)
        {
            paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
            paragraph.SetResourceReference(System.Windows.Documents.Block.BorderBrushProperty, "MarkdownHeadingBorderBrush");
        }

        if (headingBlock.Inline is not null)
        {
            RenderInlineContainer(headingBlock.Inline, paragraph.Inlines);
        }

        _outline.Add(new MarkdownHeading(level, text, anchor));
        _anchors[anchor] = paragraph;
        return paragraph;
    }

    private Paragraph RenderParagraph(ParagraphBlock paragraphBlock)
    {
        var paragraph = CreateParagraph();
        if (paragraphBlock.Inline is not null)
        {
            RenderInlineContainer(paragraphBlock.Inline, paragraph.Inlines);
        }

        return paragraph;
    }

    private Section RenderQuote(QuoteBlock quoteBlock)
    {
        var section = CreateSection();
        section.Margin = new Thickness(0, 6, 0, 12);
        section.Padding = new Thickness(14, 8, 12, 8);
        section.BorderThickness = new Thickness(3, 0, 0, 0);
        section.SetResourceReference(System.Windows.Documents.Block.BackgroundProperty, "MarkdownQuoteBackgroundBrush");
        section.SetResourceReference(System.Windows.Documents.Block.BorderBrushProperty, "MarkdownQuoteBorderBrush");
        foreach (var child in quoteBlock)
        {
            foreach (var renderedBlock in RenderBlock(child))
            {
                section.Blocks.Add(renderedBlock);
            }
        }

        return section;
    }

    private Section RenderAlert(AlertBlock alertBlock)
    {
        var kind = alertBlock.Kind.ToString().ToUpperInvariant();
        var section = CreateSection();
        section.Margin = new Thickness(0, 6, 0, 12);
        section.Padding = new Thickness(14, 10, 12, 8);
        section.BorderThickness = new Thickness(3, 0, 0, 0);
        section.SetResourceReference(System.Windows.Documents.Block.BackgroundProperty, "MarkdownQuoteBackgroundBrush");
        section.SetResourceReference(System.Windows.Documents.Block.BorderBrushProperty, GetAlertBrush(kind));

        var title = new Paragraph
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5),
            KeepWithNext = true
        };
        title.SetResourceReference(TextElement.ForegroundProperty, GetAlertBrush(kind));
        title.Inlines.Add(new Run($"{GetAlertIcon(kind)} {GetAlertTitle(kind)}"));
        section.Blocks.Add(title);

        foreach (var child in alertBlock)
        {
            foreach (var renderedBlock in RenderBlock(child))
            {
                section.Blocks.Add(renderedBlock);
            }
        }

        return section;
    }

    private WpfList RenderList(ListBlock listBlock)
    {
        var list = new WpfList
        {
            MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(18, 3, 0, 10),
            Padding = new Thickness(4, 0, 0, 0),
            MarkerOffset = 10
        };
        if (listBlock.IsOrdered
            && int.TryParse(listBlock.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startIndex))
        {
            list.StartIndex = startIndex;
        }

        foreach (var child in listBlock)
        {
            if (child is not ListItemBlock listItemBlock)
            {
                continue;
            }

            var listItem = new ListItem { Margin = new Thickness(0, 1, 0, listBlock.IsLoose ? 5 : 1) };
            foreach (var itemChild in listItemBlock)
            {
                foreach (var itemBlock in RenderBlock(itemChild))
                {
                    if (itemBlock is Paragraph paragraph)
                    {
                        paragraph.Margin = new Thickness(0, 0, 0, listBlock.IsLoose ? 6 : 1);
                    }

                    listItem.Blocks.Add(itemBlock);
                }
            }

            if (listItem.Blocks.Count == 0)
            {
                listItem.Blocks.Add(new Paragraph());
            }

            list.ListItems.Add(listItem);
        }

        return list;
    }

    private static BlockUIContainer RenderThematicBreak()
    {
        var separator = new Separator
        {
            Margin = new Thickness(0, 12, 0, 12),
            Height = 1
        };
        separator.SetResourceReference(Control.BackgroundProperty, "MarkdownHeadingBorderBrush");
        return new BlockUIContainer(separator) { Margin = new Thickness(0) };
    }

    private WpfTable RenderTable(MdTable table)
    {
        var renderedTable = new WpfTable
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 14)
        };

        var columnCount = Math.Max(
            table.ColumnDefinitions.Count,
            table.OfType<Markdig.Extensions.Tables.TableRow>().Select(row => row.Count).DefaultIfEmpty(0).Max());
        for (var index = 0; index < columnCount; index++)
        {
            var width = index < table.ColumnDefinitions.Count
                ? table.ColumnDefinitions[index].Width
                : 0;
            renderedTable.Columns.Add(new TableColumn
            {
                Width = width > 0
                    ? new GridLength(width, GridUnitType.Star)
                    : new GridLength(1, GridUnitType.Star)
            });
        }

        var rowGroup = new TableRowGroup();
        renderedTable.RowGroups.Add(rowGroup);
        var rowIndex = 0;
        foreach (var sourceRow in table.OfType<Markdig.Extensions.Tables.TableRow>())
        {
            var row = new System.Windows.Documents.TableRow();
            if (sourceRow.IsHeader)
            {
                row.FontWeight = FontWeights.SemiBold;
                row.SetResourceReference(TextElement.BackgroundProperty, "MarkdownTableHeaderBackgroundBrush");
            }
            else if (rowIndex % 2 == 0)
            {
                row.SetResourceReference(TextElement.BackgroundProperty, "MarkdownTableAlternateRowBackgroundBrush");
            }

            foreach (var sourceCell in sourceRow.OfType<Markdig.Extensions.Tables.TableCell>())
            {
                var cell = new System.Windows.Documents.TableCell
                {
                    Padding = new Thickness(9, 6, 9, 6),
                    BorderThickness = new Thickness(0.5),
                    ColumnSpan = Math.Max(1, sourceCell.ColumnSpan),
                    RowSpan = Math.Max(1, sourceCell.RowSpan)
                };
                cell.SetResourceReference(System.Windows.Documents.TableCell.BorderBrushProperty, "MarkdownBorderBrush");
                if (sourceCell.ColumnIndex >= 0 && sourceCell.ColumnIndex < table.ColumnDefinitions.Count)
                {
                    cell.TextAlignment = table.ColumnDefinitions[sourceCell.ColumnIndex].Alignment?.ToString() switch
                    {
                        "Center" => TextAlignment.Center,
                        "Right" => TextAlignment.Right,
                        _ => TextAlignment.Left
                    };
                }

                foreach (var cellChild in sourceCell)
                {
                    foreach (var cellBlock in RenderBlock(cellChild))
                    {
                        if (cellBlock is Paragraph paragraph)
                        {
                            paragraph.Margin = new Thickness(0);
                        }

                        cell.Blocks.Add(cellBlock);
                    }
                }

                if (cell.Blocks.Count == 0)
                {
                    cell.Blocks.Add(new Paragraph());
                }

                row.Cells.Add(cell);
            }

            rowGroup.Rows.Add(row);
            rowIndex++;
        }

        return renderedTable;
    }

    private Section RenderFootnoteGroup(FootnoteGroup footnoteGroup)
    {
        var section = CreateSection();
        section.Margin = new Thickness(0, 18, 0, 4);
        section.Padding = new Thickness(0, 10, 0, 0);
        section.BorderThickness = new Thickness(0, 1, 0, 0);
        section.SetResourceReference(System.Windows.Documents.Block.BorderBrushProperty, "MarkdownHeadingBorderBrush");

        foreach (var footnote in footnoteGroup.OfType<Footnote>())
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 5) };
            var label = string.IsNullOrWhiteSpace(footnote.Label)
                ? (footnote.Order + 1).ToString(CultureInfo.InvariantCulture)
                : footnote.Label;
            paragraph.Inlines.Add(new Bold(new Run($"[{label}] ")));
            foreach (var child in footnote)
            {
                if (child is ParagraphBlock paragraphBlock && paragraphBlock.Inline is not null)
                {
                    RenderInlineContainer(paragraphBlock.Inline, paragraph.Inlines);
                }
                else
                {
                    paragraph.Inlines.Add(new Run(GetBlockText(child)));
                }
            }

            section.Blocks.Add(paragraph);
        }

        return section;
    }

    private Section RenderDefinitionList(DefinitionList definitionList)
    {
        var section = CreateSection();
        section.Margin = new Thickness(0, 4, 0, 12);
        foreach (var child in definitionList)
        {
            if (child is DefinitionItem definitionItem)
            {
                foreach (var itemChild in definitionItem)
                {
                    foreach (var renderedBlock in RenderBlock(itemChild))
                    {
                        if (itemChild is DefinitionTerm && renderedBlock is Paragraph termParagraph)
                        {
                            termParagraph.FontWeight = FontWeights.SemiBold;
                            termParagraph.Margin = new Thickness(0, 5, 0, 2);
                        }
                        else
                        {
                            renderedBlock.Margin = new Thickness(18, 0, 0, 5);
                        }

                        section.Blocks.Add(renderedBlock);
                    }
                }
            }
            else
            {
                foreach (var renderedBlock in RenderBlock(child))
                {
                    section.Blocks.Add(renderedBlock);
                }
            }
        }

        return section;
    }

    private Section RenderFigure(Markdig.Extensions.Figures.Figure figure)
    {
        var section = CreateSection();
        section.TextAlignment = TextAlignment.Center;
        section.Margin = new Thickness(0, 6, 0, 12);
        foreach (var child in figure)
        {
            foreach (var renderedBlock in RenderBlock(child))
            {
                section.Blocks.Add(renderedBlock);
            }
        }

        return section;
    }

    private Paragraph RenderFigureCaption(FigureCaption figureCaption)
    {
        var paragraph = CreateParagraph();
        paragraph.FontStyle = FontStyles.Italic;
        paragraph.TextAlignment = TextAlignment.Center;
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "MarkdownSecondaryForegroundBrush");
        if (figureCaption.Inline is not null)
        {
            RenderInlineContainer(figureCaption.Inline, paragraph.Inlines);
        }

        return paragraph;
    }

    private BlockUIContainer RenderCodeBlock(CodeBlock codeBlock, string language)
    {
        var code = codeBlock.Lines.ToString().TrimEnd('\r', '\n');
        return RenderCodeBlockText(code, language);
    }

    private BlockUIContainer RenderCodeBlockText(string code, string language)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 6, 0, 14),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true
        };
        border.SetResourceReference(Border.BackgroundProperty, "MarkdownCodeBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "MarkdownBorderBrush");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { Margin = new Thickness(10, 5, 7, 4) };
        var languageText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "TEXT" : language.ToUpperInvariant(),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        languageText.SetResourceReference(TextBlock.ForegroundProperty, "MarkdownSecondaryForegroundBrush");
        DockPanel.SetDock(languageText, Dock.Left);
        header.Children.Add(languageText);

        var copyButton = new Button
        {
            Content = "复制",
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "复制代码"
        };
        AutomationProperties.SetName(copyButton, "复制代码");
        copyButton.Click += (_, _) =>
        {
            try
            {
                if (!string.IsNullOrEmpty(code))
                {
                    Clipboard.SetText(code);
                }
            }
            catch (Exception ex)
            {
                _owner.ReportResourceDiagnostic(new MarkdownDiagnostic(
                    MarkdownDiagnosticSeverity.Warning,
                    $"复制代码失败：{ex.Message}"));
            }
        };
        DockPanel.SetDock(copyButton, Dock.Right);
        header.Children.Add(copyButton);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var editor = new TextEditor
        {
            Text = code,
            IsReadOnly = true,
            ShowLineNumbers = false,
            WordWrap = false,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 7, 10, 10),
            Background = Brushes.Transparent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = Math.Clamp(42 + CountLines(code) * 19, 64, 520)
        };
        editor.SetResourceReference(Control.FontFamilyProperty, "MarkdownCodeFontFamily");
        editor.SetResourceReference(Control.ForegroundProperty, "MarkdownForegroundBrush");
        editor.SyntaxHighlighting = ResolveHighlighting(language);
        Grid.SetRow(editor, 1);
        grid.Children.Add(editor);

        border.Child = grid;
        return new BlockUIContainer(border) { Margin = new Thickness(0) };
    }

    private WpfBlock RenderHtmlBlock(HtmlBlock htmlBlock)
    {
        var html = htmlBlock.Lines.ToString();
        if (_plan.Options.HtmlPolicy == MarkdownHtmlPolicy.Disabled)
        {
            return RenderCodeBlockText(html.TrimEnd('\r', '\n'), "HTML");
        }

        var safeText = ConvertHtmlToSafeText(html);
        var paragraph = CreateParagraph();
        paragraph.Inlines.Add(new Run(safeText));
        _diagnostics.Add(new MarkdownDiagnostic(
            MarkdownDiagnosticSeverity.Information,
            "HTML 块已按安全文本子集渲染，样式和脚本不会执行。",
            GetLine(htmlBlock)));
        return paragraph;
    }

    private void RenderInlineContainer(ContainerInline container, InlineCollection target)
    {
        var htmlStack = new Stack<(string Tag, Span Span)>();
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (inline is HtmlInline htmlInline
                && TryRenderSafeHtmlInline(htmlInline.Tag, target, htmlStack))
            {
                continue;
            }

            var currentTarget = htmlStack.Count > 0 ? htmlStack.Peek().Span.Inlines : target;
            RenderInline(inline, currentTarget);
        }
    }

    private void RenderInline(Markdig.Syntax.Inlines.Inline inline, InlineCollection target)
    {
        switch (inline)
        {
            case LiteralInline literalInline:
                target.Add(new Run(literalInline.Content.ToString()));
                break;
            case CodeInline codeInline:
                target.Add(CreateInlineCode(codeInline.Content));
                break;
            case LineBreakInline lineBreakInline:
                if (lineBreakInline.IsHard)
                {
                    target.Add(new LineBreak());
                }
                else
                {
                    target.Add(new Run(" "));
                }

                break;
            case EmphasisInline emphasisInline:
                var emphasisSpan = CreateEmphasisSpan(emphasisInline);
                RenderInlineContainer(emphasisInline, emphasisSpan.Inlines);
                target.Add(emphasisSpan);
                break;
            case LinkInline linkInline when linkInline.IsImage:
                target.Add(RenderInlineImage(linkInline));
                break;
            case LinkInline linkInline:
                target.Add(RenderHyperlink(linkInline));
                break;
            case AutolinkInline autolinkInline:
                target.Add(RenderAutolink(autolinkInline));
                break;
            case TaskList taskList:
                target.Add(RenderTaskList(taskList));
                break;
            case FootnoteLink footnoteLink:
                target.Add(RenderFootnoteLink(footnoteLink));
                break;
            case MathInline mathInline:
                _diagnostics.Add(new MarkdownDiagnostic(
                    MarkdownDiagnosticSeverity.Information,
                    "行内数学公式渲染器尚未配置，已显示 LaTeX 源码。"));
                target.Add(CreateInlineCode(mathInline.Content.ToString()));
                break;
            case HtmlInline htmlInline:
                target.Add(new Run(htmlInline.Tag));
                break;
            case ContainerInline containerInline:
                var span = new Span();
                RenderInlineContainer(containerInline, span.Inlines);
                target.Add(span);
                break;
            default:
                var fallbackText = inline.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(fallbackText))
                {
                    target.Add(new Run(fallbackText));
                }

                break;
        }
    }

    private InlineUIContainer CreateInlineCode(string code)
    {
        var text = new TextBlock
        {
            Text = code,
            Padding = new Thickness(4, 0, 4, 1),
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "MarkdownCodeFontFamily");
        text.SetResourceReference(TextBlock.ForegroundProperty, "MarkdownForegroundBrush");
        var border = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(1, 0, 1, 0)
        };
        border.SetResourceReference(Border.BackgroundProperty, "MarkdownCodeBackgroundBrush");
        return new InlineUIContainer(border) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static Span CreateEmphasisSpan(EmphasisInline emphasisInline)
    {
        var span = new Span();
        switch (emphasisInline.DelimiterChar)
        {
            case '*' or '_':
                if (emphasisInline.DelimiterCount >= 2)
                {
                    span.FontWeight = FontWeights.Bold;
                }

                if (emphasisInline.DelimiterCount % 2 == 1)
                {
                    span.FontStyle = FontStyles.Italic;
                }

                break;
            case '~' when emphasisInline.DelimiterCount >= 2:
                span.TextDecorations = TextDecorations.Strikethrough;
                break;
            case '~':
                span.BaselineAlignment = BaselineAlignment.Subscript;
                span.FontSize = 11;
                break;
            case '^':
                span.BaselineAlignment = BaselineAlignment.Superscript;
                span.FontSize = 11;
                break;
            case '+':
                span.TextDecorations = TextDecorations.Underline;
                break;
            case '=':
                span.Background = Brushes.Goldenrod;
                break;
        }

        return span;
    }

    private InlineUIContainer RenderInlineImage(LinkInline linkInline)
    {
        var alternativeText = ExtractInlineText(linkInline);
        var rawSource = linkInline.Url ?? string.Empty;
        var source = _imageLoader.Resolve(rawSource, _plan.Source.BasePath);
        if (source is null)
        {
            _diagnostics.Add(new MarkdownDiagnostic(
                MarkdownDiagnosticSeverity.Warning,
                $"无法解析图片地址：{rawSource}",
                Source: rawSource));
            return CreateBrokenImageInline(alternativeText, rawSource);
        }

        var request = new MarkdownImageRequest(source, rawSource, alternativeText, linkInline.Title, IsInline: true);
        var presenter = new MarkdownImagePresenter(request, _imageLoader, _owner, _cancellationToken);
        return new InlineUIContainer(presenter) { BaselineAlignment = BaselineAlignment.Center };
    }

    private InlineUIContainer CreateBrokenImageInline(string alternativeText, string rawSource)
    {
        var text = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(alternativeText) ? rawSource : alternativeText,
            Padding = new Thickness(5, 1, 5, 1),
            ToolTip = rawSource
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MarkdownErrorBrush");
        var border = new Border { Child = text, CornerRadius = new CornerRadius(3) };
        border.SetResourceReference(Border.BackgroundProperty, "MarkdownImagePlaceholderBrush");
        return new InlineUIContainer(border) { BaselineAlignment = BaselineAlignment.Center };
    }

    private Hyperlink RenderHyperlink(LinkInline linkInline)
    {
        var displayText = ExtractInlineText(linkInline);
        var request = CreateLinkRequest(linkInline.Url ?? string.Empty, displayText, linkInline.Title);
        var hyperlink = new Hyperlink { ToolTip = linkInline.Title ?? linkInline.Url };
        hyperlink.SetResourceReference(TextElement.ForegroundProperty, "MarkdownLinkBrush");
        RenderInlineContainer(linkInline, hyperlink.Inlines);
        hyperlink.Click += (_, _) => _owner.ActivateLink(request);
        return hyperlink;
    }

    private Hyperlink RenderAutolink(AutolinkInline autolinkInline)
    {
        var target = autolinkInline.IsEmail && !autolinkInline.Url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? $"mailto:{autolinkInline.Url}"
            : autolinkInline.Url;
        var request = CreateLinkRequest(target, autolinkInline.Url, null);
        var hyperlink = new Hyperlink(new Run(autolinkInline.Url));
        hyperlink.SetResourceReference(TextElement.ForegroundProperty, "MarkdownLinkBrush");
        hyperlink.Click += (_, _) => _owner.ActivateLink(request);
        return hyperlink;
    }

    private InlineUIContainer RenderTaskList(TaskList taskList)
    {
        var checkBox = new CheckBox
        {
            IsChecked = taskList.Checked,
            IsEnabled = _owner.IsTaskListInteractive,
            Focusable = _owner.IsTaskListInteractive,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            ToolTip = taskList.Checked ? "已完成" : "未完成"
        };
        AutomationProperties.SetName(checkBox, taskList.Checked ? "已完成的任务" : "未完成的任务");
        checkBox.Click += (_, _) =>
        {
            var request = new MarkdownTaskChangedRequest(checkBox.IsChecked == true);
            if (_owner.TaskChangedCommand?.CanExecute(request) == true)
            {
                _owner.TaskChangedCommand.Execute(request);
            }
        };
        return new InlineUIContainer(checkBox) { BaselineAlignment = BaselineAlignment.Center };
    }

    private Hyperlink RenderFootnoteLink(FootnoteLink footnoteLink)
    {
        var label = footnoteLink.Footnote?.Label;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = (footnoteLink.Index + 1).ToString(CultureInfo.InvariantCulture);
        }

        var hyperlink = new Hyperlink(new Run(footnoteLink.IsBackLink ? "↩" : $"[{label}]"))
        {
            BaselineAlignment = BaselineAlignment.Superscript,
            FontSize = 11,
            ToolTip = footnoteLink.IsBackLink ? "返回正文" : $"脚注 {label}"
        };
        hyperlink.SetResourceReference(TextElement.ForegroundProperty, "MarkdownLinkBrush");
        return hyperlink;
    }

    private bool TryRenderSafeHtmlInline(
        string tagText,
        InlineCollection rootTarget,
        Stack<(string Tag, Span Span)> stack)
    {
        if (_plan.Options.HtmlPolicy == MarkdownHtmlPolicy.Disabled)
        {
            return false;
        }

        var closingMatch = ClosingHtmlTagRegex.Match(tagText);
        if (closingMatch.Success)
        {
            var closingTag = closingMatch.Groups[1].Value.ToLowerInvariant();
            if (stack.Count > 0 && stack.Peek().Tag == closingTag)
            {
                stack.Pop();
                return true;
            }

            return IsSafeHtmlTag(closingTag);
        }

        var openingMatch = OpeningHtmlTagRegex.Match(tagText);
        if (!openingMatch.Success)
        {
            return false;
        }

        var tag = openingMatch.Groups[1].Value.ToLowerInvariant();
        if (!IsSafeHtmlTag(tag))
        {
            return false;
        }

        var currentTarget = stack.Count > 0 ? stack.Peek().Span.Inlines : rootTarget;
        if (tag == "br")
        {
            currentTarget.Add(new LineBreak());
            return true;
        }

        if (tag == "img")
        {
            var sourceValue = GetHtmlAttribute(tagText, "src");
            var alternativeText = GetHtmlAttribute(tagText, "alt") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourceValue))
            {
                return true;
            }

            var source = _imageLoader.Resolve(WebUtility.HtmlDecode(sourceValue), _plan.Source.BasePath);
            if (source is null)
            {
                currentTarget.Add(CreateBrokenImageInline(alternativeText, sourceValue));
                return true;
            }

            var request = new MarkdownImageRequest(
                source,
                sourceValue,
                WebUtility.HtmlDecode(alternativeText),
                GetHtmlAttribute(tagText, "title"),
                IsInline: true);
            currentTarget.Add(new InlineUIContainer(
                new MarkdownImagePresenter(request, _imageLoader, _owner, _cancellationToken))
            {
                BaselineAlignment = BaselineAlignment.Center
            });
            return true;
        }

        Span span;
        if (tag == "a")
        {
            var href = WebUtility.HtmlDecode(GetHtmlAttribute(tagText, "href") ?? string.Empty);
            var hyperlink = new Hyperlink();
            hyperlink.SetResourceReference(TextElement.ForegroundProperty, "MarkdownLinkBrush");
            hyperlink.Click += (_, _) => _owner.ActivateLink(CreateLinkRequest(href, href, GetHtmlAttribute(tagText, "title")));
            span = hyperlink;
        }
        else
        {
            span = CreateHtmlSpan(tag);
        }

        currentTarget.Add(span);
        if (!tagText.EndsWith("/>", StringComparison.Ordinal) && tag is not "hr")
        {
            stack.Push((tag, span));
        }

        return true;
    }

    private Span CreateHtmlSpan(string tag)
    {
        var span = new Span();
        switch (tag)
        {
            case "b" or "strong" or "summary":
                span.FontWeight = FontWeights.SemiBold;
                break;
            case "i" or "em":
                span.FontStyle = FontStyles.Italic;
                break;
            case "s" or "del":
                span.TextDecorations = TextDecorations.Strikethrough;
                break;
            case "u" or "ins":
                span.TextDecorations = TextDecorations.Underline;
                break;
            case "sub":
                span.BaselineAlignment = BaselineAlignment.Subscript;
                span.FontSize = 11;
                break;
            case "sup":
                span.BaselineAlignment = BaselineAlignment.Superscript;
                span.FontSize = 11;
                break;
            case "code" or "kbd":
                span.SetResourceReference(TextElement.FontFamilyProperty, "MarkdownCodeFontFamily");
                span.SetResourceReference(TextElement.BackgroundProperty, "MarkdownCodeBackgroundBrush");
                break;
            case "mark":
                span.Background = Brushes.Goldenrod;
                break;
        }

        return span;
    }

    private MarkdownLinkRequest CreateLinkRequest(string rawTarget, string displayText, string? title)
    {
        if (rawTarget.StartsWith('#'))
        {
            return new MarkdownLinkRequest(null, rawTarget, displayText, title, MarkdownLinkKind.Anchor);
        }

        try
        {
            if (Uri.TryCreate(rawTarget, UriKind.Absolute, out var absoluteUri))
            {
                var kind = absoluteUri.Scheme.ToLowerInvariant() switch
                {
                    "http" or "https" => MarkdownLinkKind.Web,
                    "mailto" => MarkdownLinkKind.Email,
                    "file" => MarkdownLinkKind.File,
                    _ => MarkdownLinkKind.Unknown
                };
                return new MarkdownLinkRequest(absoluteUri, rawTarget, displayText, title, kind);
            }

            if (!string.IsNullOrWhiteSpace(_plan.Source.BasePath))
            {
                var pathPart = rawTarget;
                var fragment = string.Empty;
                var fragmentIndex = rawTarget.IndexOf('#');
                if (fragmentIndex >= 0)
                {
                    pathPart = rawTarget[..fragmentIndex];
                    fragment = rawTarget[fragmentIndex..];
                }

                var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(_plan.Source.BasePath, pathPart));
                var builder = new UriBuilder(new Uri(fullPath)) { Fragment = fragment.TrimStart('#') };
                return new MarkdownLinkRequest(builder.Uri, rawTarget, displayText, title, MarkdownLinkKind.Relative);
            }

            return new MarkdownLinkRequest(new Uri(rawTarget, UriKind.Relative), rawTarget, displayText, title, MarkdownLinkKind.Relative);
        }
        catch (Exception)
        {
            _diagnostics.Add(new MarkdownDiagnostic(
                MarkdownDiagnosticSeverity.Warning,
                $"无法解析链接地址：{rawTarget}",
                Source: rawTarget));
            return new MarkdownLinkRequest(null, rawTarget, displayText, title, MarkdownLinkKind.Unknown);
        }
    }

    private static Paragraph CreateParagraph()
    {
        return new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(0),
            LineHeight = 22
        };
    }

    private static Section CreateSection()
    {
        return new Section
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };
    }

    private static Paragraph CreateFallbackParagraph(string text)
    {
        var paragraph = CreateParagraph();
        paragraph.Inlines.Add(new Run(text));
        return paragraph;
    }

    private string CreateUniqueSlug(string heading)
    {
        var builder = new StringBuilder();
        var pendingDash = false;
        foreach (var rune in heading.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(rune.ToString().ToLowerInvariant());
                pendingDash = false;
            }
            else if (Rune.IsWhiteSpace(rune) || rune.Value is '-' or '_')
            {
                pendingDash = true;
            }
        }

        var baseSlug = builder.Length == 0 ? "section" : builder.ToString();
        if (!_slugCounts.TryGetValue(baseSlug, out var count))
        {
            _slugCounts[baseSlug] = 0;
            return baseSlug;
        }

        count++;
        _slugCounts[baseSlug] = count;
        return $"{baseSlug}-{count}";
    }

    private static string ExtractInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literalInline:
                    builder.Append(literalInline.Content.ToString());
                    break;
                case CodeInline codeInline:
                    builder.Append(codeInline.Content);
                    break;
                case AutolinkInline autolinkInline:
                    builder.Append(autolinkInline.Url);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case ContainerInline childContainer:
                    builder.Append(ExtractInlineText(childContainer));
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static string GetBlockText(MdBlock block)
    {
        if (block is LeafBlock leafBlock)
        {
            if (leafBlock.Inline is not null)
            {
                return ExtractInlineText(leafBlock.Inline);
            }

            return leafBlock.Lines.ToString();
        }

        if (block is ContainerBlock containerBlock)
        {
            return string.Join(Environment.NewLine, containerBlock.Select(GetBlockText));
        }

        return block.ToString() ?? string.Empty;
    }

    private static int? GetLine(MdBlock block)
    {
        return block.Line >= 0 ? block.Line + 1 : null;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        return text.Count(character => character == '\n') + 1;
    }

    private static IHighlightingDefinition? ResolveHighlighting(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        var definitionName = normalized switch
        {
            "cs" or "csharp" or "dotnet" => "C#",
            "cpp" or "c++" or "c" => "C++",
            "js" or "javascript" or "ts" or "typescript" or "json" => "JavaScript",
            "html" or "htm" or "xhtml" => "HTML",
            "xml" or "xaml" or "svg" => "XML",
            "css" or "scss" or "less" => "CSS",
            "sql" => "SQL",
            "tex" or "latex" => "TeX",
            "vb" or "visualbasic" => "VBNET",
            _ => language.Trim()
        };
        return HighlightingManager.Instance.GetDefinition(definitionName);
    }

    private static string ConvertHtmlToSafeText(string html)
    {
        var withoutDangerousContent = DangerousHtmlContentRegex.Replace(html, string.Empty);
        var withLineBreaks = HtmlLineBreakRegex.Replace(withoutDangerousContent, Environment.NewLine);
        var withoutTags = AnyHtmlTagRegex.Replace(withLineBreaks, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static string? GetHtmlAttribute(string tag, string attributeName)
    {
        var match = Regex.Match(
            tag,
            $"\\b{Regex.Escape(attributeName)}\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static bool IsSafeHtmlTag(string tag)
    {
        return tag is "br" or "b" or "strong" or "i" or "em" or "s" or "del" or "u" or "ins"
            or "sub" or "sup" or "code" or "kbd" or "mark" or "a" or "img" or "details"
            or "summary" or "small" or "span" or "hr";
    }

    private static string GetAlertBrush(string kind)
    {
        return kind switch
        {
            "WARNING" or "CAUTION" => "MarkdownWarningBrush",
            "TIP" => "MarkdownSuccessBrush",
            "IMPORTANT" => "MarkdownLinkBrush",
            _ => "MarkdownQuoteBorderBrush"
        };
    }

    private static string GetAlertIcon(string kind)
    {
        return kind switch
        {
            "WARNING" => "⚠",
            "CAUTION" => "⛔",
            "TIP" => "💡",
            "IMPORTANT" => "❗",
            _ => "ℹ"
        };
    }

    private static string GetAlertTitle(string kind)
    {
        return kind switch
        {
            "WARNING" => "警告",
            "CAUTION" => "注意",
            "TIP" => "提示",
            "IMPORTANT" => "重要",
            _ => "说明"
        };
    }

}
