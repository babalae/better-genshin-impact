using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FontStyle = System.Drawing.FontStyle;
using ImageRegion = BetterGenshinImpact.GameTask.Model.Area.ImageRegion;

namespace BetterGenshinImpact.GameTask.GetGridIcons;

/// <summary>
/// 数量 OCR 对比的目标页面；前三项会自动打开并扫描，当前一页只处理现有画面。
/// </summary>
public enum InventoryCountComparisonTarget
{
    /// <summary>打开养成道具并扫描其全部页面。</summary>
    [Description("养成道具")]
    CharacterDevelopmentItems,

    /// <summary>打开食物并扫描其全部页面。</summary>
    [Description("食物")]
    Food,

    /// <summary>打开材料并扫描其全部页面。</summary>
    [Description("材料")]
    Materials,

    /// <summary>不进行导航，只截取并识别当前一页。</summary>
    [Description("当前一页")]
    CurrentPage,
}

/// <summary>
/// 在背包页面上对比常规 OCR 与数字区域裁剪 OCR，并保存标注截图和 CSV 结果。
/// </summary>
internal sealed class InventoryCountComparisonTask : ISoloTask
{
    private const int MaxPages = 100;
    private readonly InventoryCountComparisonTarget target;
    private readonly ILogger logger = App.GetLogger<InventoryCountComparisonTask>();
    private readonly InputSimulator input = Simulation.SendInput;

    /// <summary>
    /// 创建指定目标的数量 OCR 对比任务。
    /// </summary>
    /// <param name="target">要打开并扫描的分类，或表示不导航的当前一页。</param>
    /// <exception cref="ArgumentOutOfRangeException">目标枚举值未定义时抛出。</exception>
    public InventoryCountComparisonTask(InventoryCountComparisonTarget target)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "不支持的识别目标");
        }

        this.target = target;
    }

    /// <summary>任务显示名称。</summary>
    public string Name => $"{GetTargetName(target)}数量 OCR 对比";

    /// <summary>
    /// 创建时间戳输出目录并执行一次或多页 OCR 对比。
    /// 当前一页目标会在导航逻辑前提前返回，因此不会打开界面、滚动或返回主界面。
    /// </summary>
    /// <param name="ct">用于停止截图、翻页和 OCR 处理的取消令牌。</param>
    public async Task Start(CancellationToken ct)
    {
        // 每次运行使用独立目录，同时保存页面截图、CSV 和异常样本标准化图片。
        string directory = Path.Combine(AppContext.BaseDirectory, "log", "InventoryCountComparison",
            DateTime.Now.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "normalized"));
        File.WriteAllText(
            Path.Combine(directory, "results.csv"),
            "category,page,row,column,regular_text,regular_count,cropped_text,cropped_count,components,bounds,reason\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        if (target == InventoryCountComparisonTarget.CurrentPage)
        {
            // 当前一页必须在所有导航操作之前完成，并且只允许捕获一次画面。
            SaveCurrentPage(directory, ct);
            return;
        }

        if (!TryGetGridScreenName(target, out GridScreenName category))
        {
            throw new InvalidOperationException($"无法打开识别目标：{target}");
        }

        try
        {
            // 分类目标才打开背包；GridScreen 后续每次翻页事件只保存一张对比截图。
            await new ReturnMainUiTask().Start(ct);
            await AutoArtifactSalvageTask.OpenInventory(category, input, logger, ct);

            int pageNumber = 0;
            GridScreen gridScreen = new(GridParams.Templates[category], logger, ct);
            gridScreen.OnAfterTurnToNewPage += data =>
            {
                pageNumber++;
                SaveComparisonPage(data.Item1, data.Item2.Select(x => x.Item1).ToArray(),
                    OcrFactory.Paddle, directory, target, pageNumber, logger);
            };

            await foreach (var _ in gridScreen.WithCancellation(ct))
            {
                if (pageNumber >= MaxPages)
                {
                    break;
                }
            }

            logger.LogInformation("{Category} 数量 OCR 对比完成，共输出 {PageCount} 页",
                GetTargetName(target), pageNumber);
        }
        finally
        {
            await new ReturnMainUiTask().Start(ct);
        }
    }

    private void SaveCurrentPage(string directory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 三个目标分类共用养成道具模板的 ROI；当前页面不依赖分类名称即可定位网格。
        GridParams gridParams = GridParams.Templates[GridScreenName.CharacterDevelopmentItems];
        using ImageRegion capture = TaskControl.CaptureToRectArea();
        using ImageRegion page = capture.DeriveCrop(gridParams.Roi);
        // 复用 GridScreen 的轮廓提取和幻影格子后处理，但不创建枚举器，避免触发滚动。
        IEnumerable<Rect> rects = GridScreen.GridEnumerator.GetGridItems(page.SrcMat, gridParams.Columns);
        GridCell[] cells = GridScreen.GridEnumerator
            .PostProcess(page.SrcMat, rects, (int)(0.025 * gridParams.Roi.Height))
            .OrderBy(cell => cell.RowNum)
            .ThenBy(cell => cell.ColNum)
            .ToArray();

        if (cells.Length == 0)
        {
            logger.LogWarning("当前一页未定位到背包物品格子，仍保存当前网格区域截图");
        }

        SaveComparisonPage(page, cells.Select(cell => cell.Rect).ToArray(), OcrFactory.Paddle,
            directory, target, 1, logger);
        logger.LogInformation("当前一页数量 OCR 对比完成");
    }

    /// <summary>
    /// 在一张页面截图上标注两种 OCR 结果，并追加结构化 CSV 记录。
    /// </summary>
    /// <param name="page">已裁剪到背包网格的页面图像。</param>
    /// <param name="itemRects">按行列顺序排列的物品格子矩形。</param>
    /// <param name="ocrService">常规 OCR 和裁剪 OCR 共用的 OCR 服务。</param>
    /// <param name="directory">本次运行的时间戳输出目录。</param>
    /// <param name="target">当前输出所属目标。</param>
    /// <param name="pageNumber">输出页码。</param>
    /// <param name="logger">用于记录本页识别统计的日志记录器。</param>
    internal static void SaveComparisonPage(
        ImageRegion page,
        Rect[] itemRects,
        IOcrService ocrService,
        string directory,
        InventoryCountComparisonTarget target,
        int pageNumber,
        ILogger logger)
    {
        // 同时创建图形资源，确保标注、图片和画刷在本页处理结束时释放。
        using Bitmap bitmap = page.SrcMat.ToBitmap();
        using Graphics graphics = Graphics.FromImage(bitmap);
        using Font font = new("Microsoft YaHei UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        using Brush backgroundBrush = new SolidBrush(Color.FromArgb(210, 24, 24, 24));
        using Brush sameBrush = new SolidBrush(Color.LimeGreen);
        using Brush improvedBrush = new SolidBrush(Color.Gold);
        using Brush failedBrush = new SolidBrush(Color.Red);
        using StringFormat format = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        int regularFailures = 0;
        int croppedFailures = 0;
        int validMismatches = 0;
        List<string> csvLines = [];
        for (int index = 0; index < itemRects.Length; index++)
        {
            Rect itemRect = itemRects[index];
            using Mat item = page.SrcMat.SubMat(itemRect);
            // 常规路径保留原有检测 OCR；裁剪路径只处理固定数量区域。
            string regularText = item.GetGridItemIconText(ocrService);
            int regularCount = GridItemCountRecognizer.TryParseStrict(regularText, out int parsedRegularCount)
                ? parsedRegularCount
                : -2;
            using GridItemCountRecognitionResult croppedResult =
                GridItemCountRecognizer.RecognizeCropped(item, ocrService);
            regularFailures += regularCount < 0 ? 1 : 0;
            croppedFailures += croppedResult.Count < 0 ? 1 : 0;
            validMismatches += regularCount >= 0 && regularCount != croppedResult.Count ? 1 : 0;

            bool allEqual = regularCount >= 0 && regularCount == croppedResult.Count;
            // 裁剪失败显示红色，有效但不一致显示黄色，其他有效结果显示绿色。
            Brush textBrush = croppedResult.Count < 0
                ? failedBrush
                : allEqual
                    ? sameBrush
                    : improvedBrush;
            var labelRect = new RectangleF(itemRect.X + 2, itemRect.Y + 2, itemRect.Width - 4, 18);
            graphics.FillRectangle(backgroundBrush, labelRect);
            string croppedLabel = croppedResult.Reason == null
                ? $"常规:{regularCount} 裁剪:{croppedResult.Count}"
                : $"常规:{regularCount} 裁剪:{croppedResult.Count}/{croppedResult.Reason}";
            graphics.DrawString(croppedLabel, font, textBrush, labelRect, format);

            int row = index / 8;
            int column = index % 8;
            csvLines.Add(string.Join(",",
                target,
                pageNumber,
                row,
                column,
                CsvEscape(regularText),
                regularCount,
                CsvEscape(croppedResult.RawText),
                croppedResult.Count,
                croppedResult.ComponentCount,
                CsvEscape(croppedResult.ForegroundBounds.ToString()),
                CsvEscape(croppedResult.Reason ?? string.Empty)));

            // 只为常规失败、结果不一致或裁剪修正失败的格子保存标准化数字图。
            bool needsDiagnostic = !allEqual || regularCount < 0;
            if (needsDiagnostic && croppedResult.NormalizedImage != null)
            {
                string normalizedPath = Path.Combine(directory, "normalized",
                    $"{GetFilePrefix(target)}_page_{pageNumber:D3}_row_{row:D2}_col_{column:D2}.png");
                Cv2.ImWrite(normalizedPath, croppedResult.NormalizedImage);
            }
        }

        string filePath = Path.Combine(directory,
            $"{GetFilePrefix(target)}_page_{pageNumber:D3}.png");
        bitmap.Save(filePath, ImageFormat.Png);
        File.AppendAllLines(Path.Combine(directory, "results.csv"), csvLines, Encoding.UTF8);
        logger.LogInformation(
            "{Category} 数量 OCR 对比第 {PageNumber} 页已保存，共 {ItemCount} 项，常规 OCR 失败 {RegularFailures} 项，裁剪 OCR 失败 {CroppedFailures} 项，两者有效但不同 {ValidMismatches} 项",
            GetTargetName(target), pageNumber, itemRects.Length, regularFailures, croppedFailures, validMismatches);
    }

    private static string CsvEscape(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    /// <summary>
    /// 将可导航的对比目标映射为背包网格名称。
    /// </summary>
    /// <param name="target">对比目标。</param>
    /// <param name="category">映射后的背包分类。</param>
    /// <returns>目标可导航且映射成功时返回 true；当前一页返回 false。</returns>
    internal static bool TryGetGridScreenName(InventoryCountComparisonTarget target, out GridScreenName category)
    {
        category = target switch
        {
            InventoryCountComparisonTarget.CharacterDevelopmentItems => GridScreenName.CharacterDevelopmentItems,
            InventoryCountComparisonTarget.Food => GridScreenName.Food,
            InventoryCountComparisonTarget.Materials => GridScreenName.Materials,
            _ => default,
        };
        return target != InventoryCountComparisonTarget.CurrentPage && Enum.IsDefined(target);
    }

    private static string GetTargetName(InventoryCountComparisonTarget target)
    {
        return target.GetType().GetField(target.ToString())?
            .GetCustomAttributes(typeof(DescriptionAttribute), false)
            .Cast<DescriptionAttribute>()
            .FirstOrDefault()?.Description ?? target.ToString();
    }

    private static string GetFilePrefix(InventoryCountComparisonTarget target)
    {
        return target == InventoryCountComparisonTarget.CurrentPage
            ? "currentpage"
            : target.ToString().ToLowerInvariant();
    }
}
