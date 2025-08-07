using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// Markdown文本转换为WPF FlowDocument的转换器
/// 支持解析标题、分割线、无序列表、有序列表、普通段落、超链接等Markdown语法
/// </summary>
public static class MarkdownToFlowDocumentConverter
{
    /// <summary>
    /// 将Markdown文本转换为FlowDocument
    /// </summary>
    /// <param name="markdown">Markdown文本内容</param>
    /// <returns>转换后的FlowDocument</returns>
    public static FlowDocument ConvertToFlowDocument(string markdown)
    {
        var doc = new FlowDocument();

        // 尝试获取App.xaml中定义的默认字体
        if (Application.Current?.Resources["TextThemeFontFamily"] is FontFamily appFont)
            doc.FontFamily = appFont;

        var lines = markdown.Split(["\r\n", "\n"], StringSplitOptions.None);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                doc.Blocks.Add(new Paragraph());
                continue;
            }

            var trimmedLine = line.Trim();

            // 分割线 - 支持 ---, ***, ___ (至少3个字符)
            if (IsHorizontalRule(trimmedLine))
            {
                var hrParagraph = new Paragraph
                {
                    Margin = new Thickness(0, 10, 0, 10)
                };

                // 创建分割线效果 - 使用下划线的Run
                var hrRun = new Run(new string('─', 15)) // 使用Unicode水平线字符
                {
                    Foreground = Brushes.Gray,
                    FontSize = 24
                };

                hrParagraph.Inlines.Add(hrRun);
                hrParagraph.TextAlignment = TextAlignment.Center;
                doc.Blocks.Add(hrParagraph);
                continue;
            }

            // 标题
            if (trimmedLine.StartsWith("#"))
            {
                int level = trimmedLine.TakeWhile(c => c == '#').Count();
                string text = trimmedLine[level..].Trim();
                var para = new Paragraph
                {
                    FontWeight = FontWeights.Bold,
                    FontSize = 24 - (level - 1) * 2,
                    Margin = new Thickness(0, 10, 0, 5)
                };
                // 标题支持粗体、斜体和超链接格式
                AddInlinesWithFormatting(para.Inlines, text);
                doc.Blocks.Add(para);
                continue;
            }

            // 无序列表
            if (trimmedLine.StartsWith("- "))
            {
                var para = new Paragraph
                {
                    Margin = new Thickness(20, 2, 0, 2) // 添加左缩进
                };
                para.Inlines.Add(new Run("• ") { FontWeight = FontWeights.Bold });
                // 处理列表项内容，支持粗体、斜体和超链接
                AddInlinesWithFormatting(para.Inlines, trimmedLine[2..].Trim());
                doc.Blocks.Add(para);
                continue;
            }

            // 有序列表 (支持 1. 2. 3. 等格式)
            if (IsOrderedListItem(trimmedLine, out string listContent))
            {
                var para = new Paragraph
                {
                    Margin = new Thickness(20, 2, 0, 2) // 添加左缩进
                };
                // 处理有序列表内容，支持粗体、斜体和超链接
                AddInlinesWithFormatting(para.Inlines, listContent);
                doc.Blocks.Add(para);
                continue;
            }

            // 普通段落，支持粗体、斜体和超链接
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 2)
            };
            // 处理普通段落，支持粗体、斜体和超链接
            AddInlinesWithFormatting(paragraph.Inlines, line);
            doc.Blocks.Add(paragraph);
        }
        return doc;
    }

    /// <summary>
    /// 处理文本格式（粗体、斜体、超链接等）
    /// </summary>
    /// <param name="inlines">内联集合</param>
    /// <param name="text">要处理的文本</param>
    private static void AddInlinesWithFormatting(InlineCollection inlines, string text)
    {
        int idx = 0;
        while (idx < text.Length)
        {
            // 查找下一个格式标记的位置
            int nextFormatIndex = FindNextFormatMark(text, idx);

            if (nextFormatIndex == -1)
            {
                // 没有更多格式标记，处理剩余文本中的超链接
                if (idx < text.Length)
                    AddTextWithHyperlinks(inlines, text[idx..]);
                break;
            }

            // 处理格式标记之前的普通文本，包含超链接检测
            if (nextFormatIndex > idx)
                AddTextWithHyperlinks(inlines, text[idx..nextFormatIndex]);

            // 处理格式标记
            int formatEndIndex = ProcessFormatMark(inlines, text, nextFormatIndex);
            if (formatEndIndex > nextFormatIndex)
            {
                idx = formatEndIndex;
            }
            else
            {
                // 如果格式处理失败，跳过当前字符继续
                idx = nextFormatIndex + 1;
            }
        }
    }

    /// <summary>
    /// 添加文本并自动检测超链接
    /// </summary>
    /// <param name="inlines">内联集合</param>
    /// <param name="text">要处理的文本</param>
    private static void AddTextWithHyperlinks(InlineCollection inlines, string text)
    {
        // URL正则表达式：匹配http或https开头的URL
        var urlPattern = @"https?://[^\s\[\]()]+";
        var matches = Regex.Matches(text, urlPattern);

        if (matches.Count == 0)
        {
            // 没有找到URL，直接添加文本
            inlines.Add(new Run(text));
            return;
        }

        int lastIndex = 0;
        foreach (Match match in matches)
        {
            // 添加URL之前的文本
            if (match.Index > lastIndex)
            {
                inlines.Add(new Run(text[lastIndex..match.Index]));
            }

            // 创建超链接
            var hyperlink = new Hyperlink(new Run(match.Value))
            {
                NavigateUri = new Uri(match.Value),
                Foreground = Brushes.DeepSkyBlue,
                TextDecorations = TextDecorations.Underline
            };

            // 添加点击事件处理
            hyperlink.RequestNavigate += (sender, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
                }
                catch
                {
                    // 默认浏览器打开失败静默处理
                }
                e.Handled = true;
            };

            inlines.Add(hyperlink);
            lastIndex = match.Index + match.Length;
        }

        // 添加最后一个URL之后的文本
        if (lastIndex < text.Length)
        {
            inlines.Add(new Run(text[lastIndex..]));
        }
    }

    /// <summary>
    /// 查找下一个格式标记的位置
    /// </summary>
    /// <param name="text">文本内容</param>
    /// <param name="startIndex">开始搜索的位置</param>
    /// <returns>格式标记的位置，如果没有找到返回-1</returns>
    private static int FindNextFormatMark(string text, int startIndex)
    {
        int boldIndex = text.IndexOf("**", startIndex, StringComparison.Ordinal);
        int italicIndex = text.IndexOf('*', startIndex);

        // 避免将 ** 中的第一个 * 识别为斜体标记
        if (italicIndex != -1 && italicIndex == boldIndex)
        {
            italicIndex = text.IndexOf('*', boldIndex + 2);
        }

        // 返回最近的格式标记位置
        if (boldIndex == -1 && italicIndex == -1) return -1;
        if (boldIndex == -1) return italicIndex;
        if (italicIndex == -1) return boldIndex;
        return Math.Min(boldIndex, italicIndex);
    }

    /// <summary>
    /// 处理格式标记
    /// </summary>
    /// <param name="inlines">内联集合</param>
    /// <param name="text">文本内容</param>
    /// <param name="markIndex">标记位置</param>
    /// <returns>处理结束的位置</returns>
    private static int ProcessFormatMark(InlineCollection inlines, string text, int markIndex)
    {
        // 粗体处理 **text**
        if (markIndex + 1 < text.Length && text[markIndex] == '*' && text[markIndex + 1] == '*')
        {
            int boldEndIndex = text.IndexOf("**", markIndex + 2, StringComparison.Ordinal);
            if (boldEndIndex != -1)
            {
                string boldText = text[(markIndex + 2)..boldEndIndex];
                if (!string.IsNullOrWhiteSpace(boldText))
                {
                    // 递归处理粗体文本中的其他格式
                    var boldRun = new Bold();
                    AddInlinesWithFormatting(boldRun.Inlines, boldText);
                    inlines.Add(boldRun);
                    return boldEndIndex + 2;
                }
            }
        }
        // 斜体处理 *text*
        else if (text[markIndex] == '*')
        {
            int italicEndIndex = text.IndexOf("*", markIndex + 1, StringComparison.Ordinal);
            if (italicEndIndex != -1)
            {
                string italicText = text[(markIndex + 1)..italicEndIndex];
                if (!string.IsNullOrWhiteSpace(italicText))
                {
                    // 递归处理斜体文本中的其他格式
                    var italicRun = new Italic();
                    AddInlinesWithFormatting(italicRun.Inlines, italicText);
                    inlines.Add(italicRun);
                    return italicEndIndex + 1;
                }
            }
        }

        // 如果格式处理失败，作为普通文本处理
        AddTextWithHyperlinks(inlines, text[markIndex].ToString());
        return markIndex + 1;
    }

    /// <summary>
    /// 检查是否为分割线
    /// </summary>
    /// <param name="line">文本行</param>
    /// <returns>是否为分割线</returns>
    private static bool IsHorizontalRule(string line)
    {
        if (line.Length < 3) return false;

        // 检查是否全部为相同的分割线字符，至少3个
        if (line.All(c => c == '-') && line.Length >= 3) return true;
        if (line.All(c => c == '*') && line.Length >= 3) return true;
        if (line.All(c => c == '_') && line.Length >= 3) return true;

        // 支持带空格的分割线格式，如 "- - -" 或 "* * *"
        var withoutSpaces = line.Replace(" ", "");
        if (withoutSpaces.Length >= 3)
        {
            if (withoutSpaces.All(c => c == '-') ||
                withoutSpaces.All(c => c == '*') ||
                withoutSpaces.All(c => c == '_'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查是否为有序列表项
    /// </summary>
    /// <param name="line">文本行</param>
    /// <param name="content">列表项内容</param>
    /// <returns>是否为有序列表项</returns>
    private static bool IsOrderedListItem(string line, out string content)
    {
        content = string.Empty;

        // 匹配 "数字." 格式
        var match = Regex.Match(line, @"^\s*(\d+)\.\s+(.*)$");
        if (match.Success)
        {
            content = match.Groups[2].Value;
            return true;
        }

        return false;
    }
}