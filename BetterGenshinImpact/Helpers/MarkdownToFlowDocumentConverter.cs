using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// Markdown To FlowDocument的转换器
/// 支持显示：分割线、图片、多级标题、表格、任务列表、多级无序列表、多级有序列表、普通段落、粗斜体、粗体、斜体、删除线、下划线、内联代码和超链接
/// 不支持显示：代码块、脚注、引用、HTML标签、数学公式
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

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                // 为空行创建一个较小间距的段落，减少空行占用的垂直空间
                doc.Blocks.Add(new Paragraph()
                {
                    Margin = new Thickness(0, 0, 0, 0),
                    FontSize = 6
                });
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

            // 独立图片行处理 - 直接显示图片
            if (IsStandaloneImage(trimmedLine))
            {
                var imageMatch = Regex.Match(trimmedLine, @"^!\[(.*?)\]\((.*?)\)$");
                if (imageMatch.Success)
                {
                    string altText = imageMatch.Groups[1].Value;
                    string imageContent = imageMatch.Groups[2].Value;

                    // 解析URL和标题
                    ParseImageUrlAndTitle(imageContent, out string url, out string? title);

                    var imageParagraph = new Paragraph
                    {
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 10, 0, 10)
                    };

                    // 尝试创建并添加图片
                    if (TryCreateInlineImage(url, altText, out var inlineImage))
                    {
                        imageParagraph.Inlines.Add(inlineImage);

                        // 如果有标题，添加到图片下方
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            imageParagraph.Inlines.Add(new LineBreak());
                            imageParagraph.Inlines.Add(new Run(title)
                            {
                                FontStyle = FontStyles.Normal,
                                FontSize = 12
                            });
                        }
                    }
                    else
                    {
                        // 加载图片失败，回退到创建超链接
                        string displayText = !string.IsNullOrWhiteSpace(title) ? title : altText;
                        if (TryCreateImageHyperlink(url, displayText, out Hyperlink? imageLink))
                        {
                            imageParagraph.Inlines.Add(imageLink);
                        }
                        else
                        {
                            // 创建普通文本显示
                            string fallbackText = !string.IsNullOrWhiteSpace(displayText) ? $"[图片: {displayText}]" : $"[图片: {url}]";
                            imageParagraph.Inlines.Add(new Run(fallbackText)
                            {
                                FontStyle = FontStyles.Normal,
                                Foreground = Brushes.Gray
                            });
                        }
                    }

                    doc.Blocks.Add(imageParagraph);
                    continue;
                }
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
                // 标题支持粗体、斜体、删除线、下划线、内联代码和超链接格式
                AddInlinesWithFormatting(para.Inlines, text);
                doc.Blocks.Add(para);
                continue;
            }

            // 检查是否为表格开始
            if (IsTableRow(trimmedLine))
            {
                var tableLines = new List<string> { line };

                // 收集表格的所有行
                int j = i + 1;
                while (j < lines.Length)
                {
                    var nextLine = lines[j];
                    var nextTrimmed = nextLine.Trim();

                    if (string.IsNullOrWhiteSpace(nextTrimmed))
                    {
                        break; // 空行表示表格结束
                    }

                    if (IsTableRow(nextTrimmed) || IsTableSeparatorRow(nextTrimmed))
                    {
                        tableLines.Add(nextLine);
                        j++;
                    }
                    else
                    {
                        break; // 非表格行表示表格结束
                    }
                }

                // 创建表格
                var table = CreateTable(tableLines);
                if (table != null)
                {
                    doc.Blocks.Add(table);
                    i = j - 1; // 调整索引，跳过已处理的表格行
                    continue;
                }
            }

            // 任务列表项
            if (IsTaskListItem(line, out int taskIndentLevel, out string taskContent, out bool isCompleted))
            {
                var para = new Paragraph
                {
                    Margin = new Thickness(20 + (taskIndentLevel * 20), 2, 0, 2) // 根据缩进级别调整左边距
                };

                // 根据任务完成状态添加不同的符号
                string taskSymbol = isCompleted ? "✅ " : "❎ ";
                para.Inlines.Add(new Run(taskSymbol) { FontWeight = FontWeights.Bold });

                // 处理任务内容，支持粗体、斜体、删除线、下划线、内联代码和超链接
                AddInlinesWithFormatting(para.Inlines, taskContent);
                doc.Blocks.Add(para);
                continue;
            }

            // 多级无序列表
            if (IsUnorderedListItem(line, out int unorderedIndentLevel, out string unorderedContent))
            {
                var para = new Paragraph
                {
                    Margin = new Thickness(20 + (unorderedIndentLevel * 20), 2, 0, 2) // 根据缩进级别调整左边距
                };

                // 根据缩进级别选择不同的项目符号
                string bullet = GetUnorderedListBullet(unorderedIndentLevel);
                para.Inlines.Add(new Run(bullet + " ") { FontWeight = FontWeights.Bold });

                // 处理列表项内容，支持粗体、斜体、删除线、下划线、内联代码和超链接
                AddInlinesWithFormatting(para.Inlines, unorderedContent);
                doc.Blocks.Add(para);
                continue;
            }

            // 多级有序列表
            if (IsOrderedListItem(line, out int orderedIndentLevel, out string orderedContent, out string numberPrefix))
            {
                var para = new Paragraph
                {
                    Margin = new Thickness(20 + (orderedIndentLevel * 20), 2, 0, 2) // 根据缩进级别调整左边距
                };

                // 添加序号前缀
                para.Inlines.Add(new Run(numberPrefix + " ") { FontWeight = FontWeights.Bold });

                // 处理有序列表内容，支持粗体、斜体、删除线、下划线、内联代码和超链接
                AddInlinesWithFormatting(para.Inlines, orderedContent);
                doc.Blocks.Add(para);
                continue;
            }

            // 普通段落，支持粗体、斜体、删除线、下划线、内联代码和超链接
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 2)
            };
            // 处理普通段落，支持粗体、斜体、删除线、下划线、内联代码和超链接
            AddInlinesWithFormatting(paragraph.Inlines, line);
            doc.Blocks.Add(paragraph);
        }
        return doc;
    }

    /// <summary>
    /// 检查是否为任务列表项
    /// </summary>
    /// <param name="line">文本行</param>
    /// <param name="indentLevel">缩进级别（0为顶级）</param>
    /// <param name="content">任务内容</param>
    /// <param name="isCompleted">任务是否已完成</param>
    /// <returns>是否为任务列表项</returns>
    private static bool IsTaskListItem(string line, out int indentLevel, out string content, out bool isCompleted)
    {
        indentLevel = 0;
        content = string.Empty;
        isCompleted = false;

        // 匹配任务列表项：支持空格缩进 + "- [ ] " 或 "- [x] " 格式
        // 每两个空格为一级缩进
        var match = Regex.Match(line, @"^(\s*)-\s*\[([ xX])\]\s+(.*)$");
        if (match.Success)
        {
            string indentString = match.Groups[1].Value;
            string checkMark = match.Groups[2].Value;
            content = match.Groups[3].Value;

            // 计算缩进级别（每2个空格为一级）
            indentLevel = indentString.Length / 2;

            // 判断任务是否已完成
            isCompleted = checkMark.ToLower() == "x";

            return true;
        }

        return false;
    }


    //图片处理
    /// <summary>
    /// 检查是否为独立的图片行
    /// </summary>
    /// <param name="line">文本行</param>
    /// <returns>是否为独立图片</returns>
    private static bool IsStandaloneImage(string line)
    {
        // 检查整行是否只包含一个图片标记，支持带标题的格式
        var pattern = @"^!\[.*?\]\([^\)]*\)$";
        return Regex.IsMatch(line, pattern);
    }

    /// <summary>
    /// 解析图片标记，分离 URL 和标题
    /// </summary>
    /// <param name="imageContent">图片括号内容，如：url "title" 或 url</param>
    /// <param name="url">解析出的URL</param>
    /// <param name="title">解析出的标题（可选）</param>
    private static void ParseImageUrlAndTitle(string imageContent, out string url, out string? title)
    {
        url = imageContent.Trim();
        title = null;

        // 匹配 URL 和可选标题：url "title" 或 url 'title' 或纯 url
        var match = Regex.Match(imageContent.Trim(), @"^(.+?)\s+[""']([^""']*)[""']\s*$");
        if (match.Success)
        {
            url = match.Groups[1].Value.Trim();
            title = match.Groups[2].Value;
        }
    }

    /// <summary>
    /// 尝试创建内联图片
    /// </summary>
    /// <param name="url">图片URL或路径</param>
    /// <param name="altText">替代文本</param>
    /// <param name="inlineImage">创建的内联图片</param>
    /// <returns>是否成功创建图片</returns>
    private static bool TryCreateInlineImage(string url, string altText, out InlineUIContainer? inlineImage)
    {
        inlineImage = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            // 尝试解析为绝对URI
            Uri imageUri;

            if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri) &&
                (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps || absoluteUri.Scheme == Uri.UriSchemeFile))
            {
                imageUri = absoluteUri;
            }
            else if (File.Exists(url))
            {
                // 本地文件路径
                imageUri = new Uri(Path.GetFullPath(url), UriKind.Absolute);
            }
            else
            {
                // 尝试作为相对路径处理
                var fullPath = Path.Combine(Environment.CurrentDirectory, url);
                if (File.Exists(fullPath))
                {
                    imageUri = new Uri(fullPath, UriKind.Absolute);
                }
                else
                {
                    return false;
                }
            }

            // 创建图片
            var image = new Image
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                MaxWidth = 400, // 限制最大宽度，避免图片过大
                MaxHeight = 300, // 限制最大高度
                ToolTip = !string.IsNullOrWhiteSpace(altText) ? altText : "图片"
            };

            // 设置图片源
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = imageUri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // 加载后缓存，避免文件锁定
            bitmap.EndInit();
            image.Source = bitmap;

            // 创建内联容器
            inlineImage = new InlineUIContainer(image);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载图片失败: {url}, 错误: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 尝试创建图片超链接
    /// </summary>
    /// <param name="url">图片URL或路径</param>
    /// <param name="altText">替代文本</param>
    /// <param name="imageLink">创建的超链接对象</param>
    /// <returns>是否成功创建超链接</returns>
    private static bool TryCreateImageHyperlink(string url, string altText, out Hyperlink? imageLink)
    {
        imageLink = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            Uri? imageUri = null;
            string displayText = string.IsNullOrWhiteSpace(altText) ? $"🖼️ 查看图片: {Path.GetFileName(url)}" : $"🖼️ {altText}";

            // 处理不同类型的URL
            if (Uri.TryCreate(url, UriKind.Absolute, out imageUri))
            {
                // 绝对URL（http/https/file等）
            }
            else if (File.Exists(url))
            {
                // 相对路径或绝对文件路径
                var fullPath = Path.GetFullPath(url);
                imageUri = new Uri(fullPath, UriKind.Absolute);
            }
            else
            {
                // 尝试作为相对路径处理
                var fullPath = Path.Combine(Environment.CurrentDirectory, url);
                if (File.Exists(fullPath))
                {
                    imageUri = new Uri(fullPath, UriKind.Absolute);
                }
                else
                {
                    // 文件不存在，但仍然创建链接（可能是网络URL）
                    if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out imageUri))
                    {
                        // 创建相对URL的绝对路径
                        var baseUri = new Uri(Environment.CurrentDirectory + Path.DirectorySeparatorChar);
                        imageUri = new Uri(baseUri, imageUri);
                    }
                }
            }

            if (imageUri != null)
            {
                imageLink = new Hyperlink(new Run(displayText))
                {
                    NavigateUri = imageUri,
                    Foreground = Brushes.DodgerBlue,
                    TextDecorations = TextDecorations.Underline,
                    ToolTip = $"点击在浏览器中打开图片: {url}"
                };

                // 添加点击事件处理
                imageLink.RequestNavigate += (sender, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"无法打开图片: {e.Uri}, 错误: {ex.Message}");
                    }
                    e.Handled = true;
                };

                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"创建图片链接失败 - URL: {url}, 错误: {ex.Message}");
        }

        return false;
    }


    //分割线处理
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


    //表格处理
    /// <summary>
    /// 检查是否为表格行
    /// </summary>
    /// <param name="line">文本行</param>
    /// <returns>是否为表格行</returns>
    private static bool IsTableRow(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        // 简单检查：包含至少一个 | 字符，且不是表格分隔行
        return line.Contains('|') && !IsTableSeparatorRow(line);
    }

    /// <summary>
    /// 检查是否为表格分隔行（如 |---|---|）
    /// </summary>
    /// <param name="line">文本行</param>
    /// <returns>是否为表格分隔行</returns>
    private static bool IsTableSeparatorRow(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        // 匹配表格分隔行：|---|---|--- 或 | :--- | ---: | :---: |
        var pattern = @"^\s*\|(\s*:?-+:?\s*\|)+\s*$";
        return Regex.IsMatch(line, pattern);
    }

    /// <summary>
    /// 创建表格
    /// </summary>
    /// <param name="tableLines">表格行列表</param>
    /// <returns>创建的表格对象</returns>
    private static Table? CreateTable(List<string> tableLines)
    {
        if (tableLines.Count < 1)
            return null;

        // 解析表格数据
        var rows = new List<List<string>>();
        List<TextAlignment>? alignments = null;
        bool hasSeparatorRow = false;

        foreach (var line in tableLines)
        {
            var trimmedLine = line.Trim();
            if (IsTableSeparatorRow(trimmedLine))
            {
                // 解析对齐方式
                alignments = ParseTableAlignment(trimmedLine);
                hasSeparatorRow = true;
                continue;
            }

            // 解析表格行
            var cells = ParseTableRow(trimmedLine);
            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        if (rows.Count == 0)
            return null;

        // 创建WPF表格
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 10)
        };

        // 确定列数
        int columnCount = rows.Max(row => row.Count);

        // 创建列定义
        for (int i = 0; i < columnCount; i++)
        {
            table.Columns.Add(new TableColumn());
        }

        // 创建表格行组
        var tableRowGroup = new TableRowGroup();

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var tableRow = new TableRow();

            // 第一行作为表头（如果有分隔行的话）
            bool isHeader = hasSeparatorRow && rowIndex == 0;

            for (int colIndex = 0; colIndex < columnCount; colIndex++)
            {
                var cellContent = colIndex < row.Count ? row[colIndex] : "";
                var tableCell = new TableCell();

                // 设置单元格边框
                tableCell.BorderBrush = Brushes.Gray;
                tableCell.BorderThickness = new Thickness(1);
                tableCell.Padding = new Thickness(8, 4, 8, 4);

                // 设置对齐方式
                if (alignments != null && colIndex < alignments.Count)
                {
                    tableCell.TextAlignment = alignments[colIndex];
                }

                // 创建段落
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0)
                };

                // 表头加粗
                if (isHeader)
                {
                    paragraph.FontWeight = FontWeights.Bold;
                    tableCell.Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)); // 淡灰色背景
                }

                // 添加格式化文本
                AddInlinesWithFormatting(paragraph.Inlines, cellContent);
                tableCell.Blocks.Add(paragraph);
                tableRow.Cells.Add(tableCell);
            }

            tableRowGroup.Rows.Add(tableRow);
        }

        table.RowGroups.Add(tableRowGroup);
        return table;
    }

    /// <summary>
    /// 解析表格行，提取单元格内容
    /// </summary>
    /// <param name="line">表格行</param>
    /// <returns>单元格内容列表</returns>
    private static List<string> ParseTableRow(string line)
    {
        var cells = new List<string>();

        // 移除首尾的管道符
        var trimmed = line.Trim();
        if (trimmed.StartsWith("|"))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith("|"))
            trimmed = trimmed[..^1];

        // 分割单元格，但要处理转义的管道符
        var parts = SplitTableCells(trimmed);

        foreach (var part in parts)
        {
            cells.Add(part.Trim());
        }

        return cells;
    }

    /// <summary>
    /// 分割表格单元格，处理转义的管道符
    /// </summary>
    /// <param name="content">单元格内容</param>
    /// <returns>单元格列表</returns>
    private static List<string> SplitTableCells(string content)
    {
        var cells = new List<string>();
        var currentCell = "";
        bool inCodeSpan = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (c == '`')
            {
                inCodeSpan = !inCodeSpan;
                currentCell += c;
            }
            else if (c == '\\' && i + 1 < content.Length && content[i + 1] == '|')
            {
                // 转义的管道符
                currentCell += '|';
                i++; // 跳过下一个字符
            }
            else if (c == '|' && !inCodeSpan)
            {
                // 分隔符
                cells.Add(currentCell);
                currentCell = "";
            }
            else
            {
                currentCell += c;
            }
        }

        // 添加最后一个单元格
        if (!string.IsNullOrEmpty(currentCell) || cells.Count > 0)
        {
            cells.Add(currentCell);
        }

        return cells;
    }

    /// <summary>
    /// 解析表格对齐方式
    /// </summary>
    /// <param name="separatorLine">分隔行</param>
    /// <returns>对齐方式列表</returns>
    private static List<TextAlignment> ParseTableAlignment(string separatorLine)
    {
        var alignments = new List<TextAlignment>();

        // 移除首尾的管道符和空格
        var trimmed = separatorLine.Trim().Trim('|');
        var parts = trimmed.Split('|');

        foreach (var part in parts)
        {
            var cell = part.Trim();
            if (cell.StartsWith(":") && cell.EndsWith(":"))
            {
                alignments.Add(TextAlignment.Center);
            }
            else if (cell.EndsWith(":"))
            {
                alignments.Add(TextAlignment.Right);
            }
            else
            {
                alignments.Add(TextAlignment.Left);
            }
        }

        return alignments;
    }


    //列表处理
    /// <summary>
    /// 检查是否为多级无序列表项
    /// </summary>
    /// <param name="line">文本行</param>
    /// <param name="indentLevel">缩进级别（0为顶级）</param>
    /// <param name="content">列表项内容</param>
    /// <returns>是否为无序列表项</returns>
    private static bool IsUnorderedListItem(string line, out int indentLevel, out string content)
    {
        indentLevel = 0;
        content = string.Empty;

        // 匹配多级无序列表：支持空格缩进 + "- " 格式
        // 每两个空格为一级缩进
        var match = Regex.Match(line, @"^(\s*)- (.*)$");
        if (match.Success)
        {
            string indentString = match.Groups[1].Value;
            content = match.Groups[2].Value;

            // 计算缩进级别（每2个空格为一级）
            indentLevel = indentString.Length / 2;

            return true;
        }

        return false;
    }

    /// <summary>
    /// 检查是否为多级有序列表项
    /// </summary>
    /// <param name="line">文本行</param>
    /// <param name="indentLevel">缩进级别（0为顶级）</param>
    /// <param name="content">列表项内容</param>
    /// <param name="numberPrefix">序号前缀</param>
    /// <returns>是否为有序列表项</returns>
    private static bool IsOrderedListItem(string line, out int indentLevel, out string content, out string numberPrefix)
    {
        indentLevel = 0;
        content = string.Empty;
        numberPrefix = string.Empty;

        // 匹配多级有序列表：支持空格缩进 + "数字." 格式
        // 每两个空格为一级缩进
        var match = Regex.Match(line, @"^(\s*)(\d+)\.\s+(.*)$");
        if (match.Success)
        {
            string indentString = match.Groups[1].Value;
            numberPrefix = match.Groups[2].Value + ".";
            content = match.Groups[3].Value;

            // 计算缩进级别（每2个空格为一级）
            indentLevel = indentString.Length / 2;

            return true;
        }

        return false;
    }

    /// <summary>
    /// 根据缩进级别获取无序列表的项目符号
    /// </summary>
    /// <param name="indentLevel">缩进级别</param>
    /// <returns>项目符号字符</returns>
    private static string GetUnorderedListBullet(int indentLevel)
    {
        return indentLevel switch
        {
            0 => "•",      // 顶级：实心圆点
            1 => "◦",      // 二级：空心圆点
            2 => "▪",      // 三级：小方块
            _ => "‣"       // 更深级别：三角形
        };
    }


    //文本处理
    /// <summary>
    /// 处理文本格式（粗体、斜体、粗斜体、删除线、下划线、内联代码、图片、超链接等）
    /// </summary>
    /// <param name="inlines">内联集合</param>
    /// <param name="text">要处理的文本</param>
    private static void AddInlinesWithFormatting(InlineCollection inlines, string text)
    {
        // 首先处理转义字符
        text = ProcessEscapeCharacters(text);

        int idx = 0;
        while (idx < text.Length)
        {
            // 查找下一个格式标记的位置（包括图片）
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

            // 处理格式标记（包括图片）
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
    /// 处理转义字符
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <returns>处理转义字符后的文本</returns>
    private static string ProcessEscapeCharacters(string text)
    {
        // 使用特殊标记替换转义字符，避免在格式处理时被识别
        const string ESCAPED_ASTERISK = "\x00ESCAPED_ASTERISK\x00";
        const string ESCAPED_UNDERSCORE = "\x00ESCAPED_UNDERSCORE\x00";
        const string ESCAPED_TILDE = "\x00ESCAPED_TILDE\x00";
        const string ESCAPED_BACKSLASH = "\x00ESCAPED_BACKSLASH\x00";
        const string ESCAPED_BACKTICK = "\x00ESCAPED_BACKTICK\x00";

        var result = text
            .Replace(@"\*", ESCAPED_ASTERISK)
            .Replace(@"\_", ESCAPED_UNDERSCORE)
            .Replace(@"\~", ESCAPED_TILDE)
            .Replace(@"\\", ESCAPED_BACKSLASH)
            .Replace(@"\`", ESCAPED_BACKTICK);

        return result;
    }

    /// <summary>
    /// 恢复转义字符
    /// </summary>
    /// <param name="text">包含转义标记的文本</param>
    /// <returns>恢复转义字符后的文本</returns>
    private static string RestoreEscapeCharacters(string text)
    {
        const string ESCAPED_ASTERISK = "\x00ESCAPED_ASTERISK\x00";
        const string ESCAPED_UNDERSCORE = "\x00ESCAPED_UNDERSCORE\x00";
        const string ESCAPED_TILDE = "\x00ESCAPED_TILDE\x00";
        const string ESCAPED_BACKSLASH = "\x00ESCAPED_BACKSLASH\x00";
        const string ESCAPED_BACKTICK = "\x00ESCAPED_BACKTICK\x00";

        return text
            .Replace(ESCAPED_ASTERISK, "*")
            .Replace(ESCAPED_UNDERSCORE, "_")
            .Replace(ESCAPED_TILDE, "~")
            .Replace(ESCAPED_BACKSLASH, "\\")
            .Replace(ESCAPED_BACKTICK, "`");
    }

    /// <summary>
    /// 查找下一个格式标记的位置
    /// </summary>
    /// <param name="text">文本内容</param>
    /// <param name="startIndex">开始搜索的位置</param>
    /// <returns>格式标记的位置，如果没有找到返回-1</returns>
    private static int FindNextFormatMark(string text, int startIndex)
    {
        int boldItalicIndex = text.IndexOf("***", startIndex, StringComparison.Ordinal);
        int boldIndex = text.IndexOf("**", startIndex, StringComparison.Ordinal);
        int italicIndex = text.IndexOf('*', startIndex);
        int strikethroughIndex = text.IndexOf("~~", startIndex, StringComparison.Ordinal);
        int underlineIndex = text.IndexOf("__", startIndex, StringComparison.Ordinal);
        int inlineCodeIndex = text.IndexOf('`', startIndex);
        int imageIndex = text.IndexOf("![", startIndex, StringComparison.Ordinal);
        int autoLinkIndex = text.IndexOf("<http", startIndex, StringComparison.OrdinalIgnoreCase);
        int linkIndex = text.IndexOf('[', startIndex);

        // 避免将 *** 或 ** 中的 * 识别为斜体标记
        if (italicIndex != -1)
        {
            if (italicIndex == boldItalicIndex)
            {
                // 跳过 *** 中的第一个 *
                italicIndex = text.IndexOf('*', boldItalicIndex + 3);
            }
            else if (italicIndex == boldIndex)
            {
                // 跳过 ** 中的第一个 *
                italicIndex = text.IndexOf('*', boldIndex + 2);
            }
        }

        // 避免将 *** 识别为 ** + *
        if (boldIndex != -1 && boldIndex == boldItalicIndex)
        {
            boldIndex = text.IndexOf("**", boldItalicIndex + 3, StringComparison.Ordinal);
        }

        // 避免将 __ 识别为两个单独的下划线字符
        if (underlineIndex != -1)
        {
            // 确保找到的是完整的 __ 标记
            var nextUnderline = text.IndexOf("__", underlineIndex + 2, StringComparison.Ordinal);
            if (nextUnderline == -1)
            {
                underlineIndex = -1; // 没有配对的下划线，忽略
            }
        }

        // 收集所有有效的格式标记位置
        var indices = new[] { boldItalicIndex, boldIndex, italicIndex, strikethroughIndex, underlineIndex, inlineCodeIndex, imageIndex, autoLinkIndex, linkIndex }
            .Where(i => i != -1)
            .ToArray();

        // 返回最近的格式标记位置
        return indices.Length > 0 ? indices.Min() : -1;
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
        // 标准链接格式 [text](url)
        if (markIndex < text.Length && text[markIndex] == '[')
        {
            int closeBracketIndex = text.IndexOf(']', markIndex);
            if (closeBracketIndex != -1 && closeBracketIndex + 1 < text.Length && text[closeBracketIndex + 1] == '(')
            {
                int closeParenIndex = text.IndexOf(')', closeBracketIndex + 1);
                if (closeParenIndex != -1)
                {
                    string linkText = text.Substring(markIndex + 1, closeBracketIndex - markIndex - 1);
                    string url = text.Substring(closeBracketIndex + 2, closeParenIndex - closeBracketIndex - 2);

                    // 创建超链接
                    if (TryCreateHyperlinkWithText(url, linkText, out Hyperlink? hyperlink))
                    {
                        inlines.Add(hyperlink);
                        return closeParenIndex + 1;
                    }
                }
            }
        }

        // 自动链接格式 <https://example.com>
        else if (markIndex < text.Length && text[markIndex] == '<' &&
            markIndex + 1 < text.Length &&
            (text.IndexOf("http:", markIndex, StringComparison.OrdinalIgnoreCase) == markIndex + 1 ||
             text.IndexOf("https:", markIndex, StringComparison.OrdinalIgnoreCase) == markIndex + 1))
        {
            int autoLinkEndIndex = text.IndexOf('>', markIndex);
            if (autoLinkEndIndex != -1)
            {
                // 提取URL (不包含 < >)
                string url = text.Substring(markIndex + 1, autoLinkEndIndex - markIndex - 1);
                if (TryCreateHyperlink(url, out Hyperlink? hyperlink))
                {
                    inlines.Add(hyperlink);
                    return autoLinkEndIndex + 1;
                }
            }
        }
        // 图片处理 ![alt text](url) - 尝试直接显示图片
        else if (markIndex + 1 < text.Length && text[markIndex] == '!' && text[markIndex + 1] == '[')
        {
            int imageEndIndex = ProcessImageMark(inlines, text, markIndex);
            if (imageEndIndex > markIndex)
            {
                return imageEndIndex;
            }
        }
        // 粗斜体处理 ***text*** (必须在粗体和斜体之前处理)
        else if (markIndex + 2 < text.Length &&
            text[markIndex] == '*' && text[markIndex + 1] == '*' && text[markIndex + 2] == '*')
        {
            int boldItalicEndIndex = text.IndexOf("***", markIndex + 3, StringComparison.Ordinal);
            if (boldItalicEndIndex != -1)
            {
                string boldItalicText = text[(markIndex + 3)..boldItalicEndIndex];
                if (!string.IsNullOrWhiteSpace(boldItalicText))
                {
                    // 创建粗斜体：Bold 包含 Italic
                    var boldRun = new Bold();
                    var italicRun = new Italic();
                    // 递归处理粗斜体文本中的其他格式
                    AddInlinesWithFormatting(italicRun.Inlines, boldItalicText);
                    boldRun.Inlines.Add(italicRun);
                    inlines.Add(boldRun);
                    return boldItalicEndIndex + 3;
                }
            }
        }
        // 删除线处理 ~~text~~
        else if (markIndex + 1 < text.Length && text[markIndex] == '~' && text[markIndex + 1] == '~')
        {
            int strikethroughEndIndex = text.IndexOf("~~", markIndex + 2, StringComparison.Ordinal);
            if (strikethroughEndIndex != -1)
            {
                string strikethroughText = text[(markIndex + 2)..strikethroughEndIndex];
                if (!string.IsNullOrWhiteSpace(strikethroughText))
                {
                    var strikethroughRun = new Run(RestoreEscapeCharacters(strikethroughText))
                    {
                        TextDecorations = TextDecorations.Strikethrough
                    };
                    inlines.Add(strikethroughRun);
                    return strikethroughEndIndex + 2;
                }
            }
        }
        // 下划线处理 __text__ (注意与粗体 **text** 区分)
        else if (markIndex + 1 < text.Length && text[markIndex] == '_' && text[markIndex + 1] == '_')
        {
            int underlineEndIndex = text.IndexOf("__", markIndex + 2, StringComparison.Ordinal);
            if (underlineEndIndex != -1)
            {
                string underlineText = text[(markIndex + 2)..underlineEndIndex];
                if (!string.IsNullOrWhiteSpace(underlineText))
                {
                    var underlineRun = new Run(RestoreEscapeCharacters(underlineText))
                    {
                        TextDecorations = TextDecorations.Underline
                    };
                    inlines.Add(underlineRun);
                    return underlineEndIndex + 2;
                }
            }
        }
        // 粗体处理 **text**
        else if (markIndex + 1 < text.Length && text[markIndex] == '*' && text[markIndex + 1] == '*')
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
        // 内联代码处理 `code`
        else if (text[markIndex] == '`')
        {
            int codeEndIndex = text.IndexOf('`', markIndex + 1);
            if (codeEndIndex != -1)
            {
                string codeText = text[(markIndex + 1)..codeEndIndex];
                if (!string.IsNullOrEmpty(codeText)) // 允许空代码，但不允许null
                {
                    // 创建包含内联代码的容器，用于添加间距
                    var codeContainer = new InlineUIContainer();

                    // 创建边框元素来提供间距和背景
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(4, 2, 4, 2),
                        Margin = new Thickness(2, 0, 2, 0),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        ToolTip = "单击复制代码",
                        Child = new TextBlock
                        {
                            Text = RestoreEscapeCharacters(codeText),
                            FontFamily = new FontFamily("Consolas, 宋体, monospace"),
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    // 添加单击复制功能
                    border.MouseDown += (sender, e) =>
                    {
                        try
                        {
                            Clipboard.SetText(RestoreEscapeCharacters(codeText));
                            ThemedMessageBox.Information("代码已复制到剪贴板！", "复制成功");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"复制代码失败: {ex.Message}");
                            border.ToolTip = "复制失败";
                        }
                    };

                    codeContainer.Child = border;
                    inlines.Add(codeContainer);
                    return codeEndIndex + 1;
                }
            }
        }
        // 如果格式处理失败，作为普通文本处理
        AddTextWithHyperlinks(inlines, text[markIndex].ToString());
        return markIndex + 1;
    }

    /// <summary>
    /// 处理图片格式标记 ![alt text](url) - 尝试直接显示图片
    /// </summary>
    /// <param name="inlines">内联集合</param>
    /// <param name="text">文本内容</param>
    /// <param name="markIndex">标记位置</param>
    /// <returns>处理结束的位置</returns>
    private static int ProcessImageMark(InlineCollection inlines, string text, int markIndex)
    {
        // 查找 ![alt](url) 或 ![alt](url "title") 格式
        var imagePattern = @"^!\[(.*?)\]\((.*?)\)";
        var match = Regex.Match(text[markIndex..], imagePattern);

        if (match.Success)
        {
            string altText = match.Groups[1].Value;
            string imageContent = match.Groups[2].Value;

            // 解析URL和标题
            ParseImageUrlAndTitle(imageContent, out string url, out string? title);

            // 尝试创建内联图片
            if (TryCreateInlineImage(url, altText, out var inlineImage))
            {
                inlines.Add(inlineImage);

                // 如果有标题且不是空字符串，添加标题文本
                if (!string.IsNullOrWhiteSpace(title))
                {
                    inlines.Add(new Run(" " + title) { FontStyle = FontStyles.Normal, FontSize = 12 });
                }
            }
            else
            {
                // 如果图片加载失败，回退到超链接
                string displayText = !string.IsNullOrWhiteSpace(title) ? title : altText;
                if (TryCreateImageHyperlink(url, displayText, out Hyperlink? imageLink))
                {
                    inlines.Add(imageLink);
                }
                else
                {
                    // 如果链接创建失败，显示替代文本
                    string fallbackText = !string.IsNullOrWhiteSpace(displayText) ? $"[图片: {displayText}]" : $"[图片: {url}]";
                    inlines.Add(new Run(fallbackText));
                }
            }

            return markIndex + match.Length;
        }

        return markIndex;
    }


    //超链接处理
    /// <summary>
    /// 添加文本并自动检测超链接
    /// </summary>
    /// <param name="inlines">内联集合</param>
    /// <param name="text">要处理的文本</param>
    private static void AddTextWithHyperlinks(InlineCollection inlines, string text)
    {
        // 恢复转义字符
        text = RestoreEscapeCharacters(text);

        // 更严格的URL正则表达式：匹配完整的http或https URL
        var urlPattern = @"https?://(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)+[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?::\d+)?(?:/[^\s\[\]()]*)?";
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

            // 验证并创建超链接
            if (TryCreateHyperlink(match.Value, out Hyperlink? hyperlink))
            {
                inlines.Add(hyperlink);
            }
            else
            {
                // 如果URI无效，作为普通文本添加
                inlines.Add(new Run(match.Value));
            }

            lastIndex = match.Index + match.Length;
        }

        // 添加最后一个URL之后的文本
        if (lastIndex < text.Length)
        {
            inlines.Add(new Run(text[lastIndex..]));
        }
    }

    /// <summary>
    /// 尝试创建超链接
    /// </summary>
    /// <param name="urlText">URL文本</param>
    /// <param name="hyperlink">创建的超链接对象</param>
    /// <returns>是否成功创建超链接</returns>
    private static bool TryCreateHyperlink(string urlText, out Hyperlink? hyperlink)
    {
        hyperlink = null;

        try
        {
            // 验证URI是否有效
            if (Uri.TryCreate(urlText, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                hyperlink = new Hyperlink(new Run(urlText))
                {
                    NavigateUri = uri,
                    Foreground = Brushes.DodgerBlue,
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

                return true;
            }
        }
        catch (UriFormatException)
        {
            // URI格式异常，返回false
        }
        catch (Exception)
        {
            // 其他异常，返回false
        }

        return false;
    }

    /// <summary>
    /// 尝试创建带自定义文本的超链接
    /// </summary>
    /// <param name="urlText">URL文本</param>
    /// <param name="displayText">显示的文本</param>
    /// <param name="hyperlink">创建的超链接对象</param>
    /// <returns>是否成功创建超链接</returns>
    private static bool TryCreateHyperlinkWithText(string urlText, string displayText, out Hyperlink? hyperlink)
    {
        hyperlink = null;

        try
        {
            // 验证URI是否有效
            if (Uri.TryCreate(urlText, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                hyperlink = new Hyperlink(new Run(displayText))
                {
                    NavigateUri = uri,
                    Foreground = Brushes.DodgerBlue,
                    TextDecorations = TextDecorations.Underline,
                    ToolTip = urlText
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

                return true;
            }
        }
        catch (UriFormatException)
        {
            // URI格式异常，返回false
        }
        catch (Exception)
        {
            // 其他异常，返回false
        }

        return false;
    }
}