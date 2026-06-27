using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.Reward;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 合成指定材料的执行结果。
/// </summary>
public class CraftMaterialResult
{
    /// <summary>
    /// 是否成功完成合成。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 目标材料名。
    /// </summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>
    /// 目标合成次数。
    /// </summary>
    public int TargetCount { get; set; }

    /// <summary>
    /// 实际设置到界面的合成次数。
    /// </summary>
    public int ActualCount { get; set; }

    /// <summary>
    /// 本次使用的材料筛选类型。
    /// </summary>
    public string MaterialType { get; set; } = string.Empty;

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    /// <param name="materialName">目标材料名。</param>
    /// <param name="targetCount">目标合成次数。</param>
    /// <param name="actualCount">实际设置次数。</param>
    /// <param name="materialType">材料筛选类型。</param>
    /// <returns>成功结果。</returns>
    public static CraftMaterialResult CreateSuccess(string materialName, int targetCount, int actualCount, string materialType)
    {
        return new CraftMaterialResult
        {
            Success = true,
            MaterialName = materialName,
            TargetCount = targetCount,
            ActualCount = actualCount,
            MaterialType = materialType
        };
    }

}

/// <summary>
/// 当前合成界面内按材料名自动合成材料。
/// </summary>
public class CraftMaterialTask
{
    private const int MaxSearchPages = 5;
    private const int SliderStartX = 1173;
    private const int SliderY = 672;

    private static readonly Regex PositiveIntRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex FractionRegex = new(@"(\d+)\s*/\s*(\d+)", RegexOptions.Compiled);
    private static readonly Lazy<Dictionary<string, string>> MaterialTypes = new(LoadMaterialTypes);
    private static readonly GridParams CraftingGridParams = new(new Rect(48, 193, 696, 762), 5, 3, 40, 28, 0.024);

    private readonly ILogger<CraftMaterialTask> _logger = App.GetLogger<CraftMaterialTask>();
    private readonly InputSimulator _input = Simulation.SendInput;
    private readonly RewardIconMatcher _iconMatcher = new();
    private readonly string _materialName;
    private readonly int _targetCount;
    private readonly string? _materialType;
    private BvPage? _page;
    private CancellationToken _ct;

    /// <summary>
    /// 当前任务使用的 BvPage 实例。
    /// </summary>
    /// <exception cref="InvalidOperationException">任务尚未启动时抛出。</exception>
    private BvPage Page => _page ?? throw new InvalidOperationException("CraftMaterialTask 尚未初始化 BvPage。");

    /// <summary>
    /// 创建当前合成界面自动合成任务。
    /// </summary>
    /// <param name="materialName">目标材料名。</param>
    /// <param name="targetCount">目标合成次数。</param>
    /// <param name="materialType">材料筛选类型；为空时从物品 CSV 读取。</param>
    public CraftMaterialTask(string materialName, int targetCount, string? materialType = null)
    {
        _materialName = materialName?.Trim() ?? string.Empty;
        _targetCount = targetCount;
        _materialType = string.IsNullOrWhiteSpace(materialType) ? null : materialType.Trim();
    }

    /// <summary>
    /// 执行合成任务。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>合成执行结果。</returns>
    /// <exception cref="ArgumentException">材料名为空时抛出。</exception>
    /// <exception cref="ArgumentOutOfRangeException">合成次数小于等于 0 时抛出。</exception>
    /// <exception cref="InvalidOperationException">合成界面、材料筛选、材料搜索或数量设置失败时抛出。</exception>
    public async Task<CraftMaterialResult> Start(CancellationToken ct)
    {
        _ct = ct;
        _page = new BvPage(ct);
        ValidateArguments();

        var materialType = ResolveMaterialType();
        EnsureInCraftingUi();

        _logger.LogInformation("开始合成材料：{MaterialName}，目标次数：{Count}，筛选类型：{MaterialType}", _materialName, _targetCount, materialType);

        await SelectMaterialType(materialType);
        await FindAndSelectMaterial();
        var adjustedCount = await SetCraftCount(_targetCount);

        await SubmitCraftAndClaimResult();

        _logger.LogInformation("材料 {MaterialName} 合成完成，次数：{Count}", _materialName, adjustedCount);
        return CraftMaterialResult.CreateSuccess(_materialName, _targetCount, adjustedCount, materialType);
    }

    /// <summary>
    /// 校验任务构造参数。
    /// </summary>
    /// <exception cref="ArgumentException">材料名为空时抛出。</exception>
    /// <exception cref="ArgumentOutOfRangeException">合成次数小于等于 0 时抛出。</exception>
    private void ValidateArguments()
    {
        if (string.IsNullOrWhiteSpace(_materialName))
        {
            throw new ArgumentException("材料名不能为空。", nameof(_materialName));
        }

        if (_targetCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_targetCount), _targetCount, "合成次数必须大于 0。");
        }
    }

    /// <summary>
    /// 解析材料筛选类型，优先使用调用方显式传入的类型。
    /// </summary>
    /// <returns>材料筛选类型。</returns>
    /// <exception cref="InvalidOperationException">未传入材料类型且 CSV 中没有对应材料类型时抛出。</exception>
    private string ResolveMaterialType()
    {
        if (!string.IsNullOrWhiteSpace(_materialType))
        {
            return _materialType;
        }

        if (MaterialTypes.Value.TryGetValue(_materialName, out var materialType) && !string.IsNullOrWhiteSpace(materialType))
        {
            return materialType;
        }

        throw new InvalidOperationException($"未找到材料 {_materialName} 的材料类型，请传入 materialType 或检查 item.csv。");
    }

    /// <summary>
    /// 从物品原型 CSV 读取材料类型映射。
    /// </summary>
    /// <returns>材料名到材料类型的映射。</returns>
    private static Dictionary<string, string> LoadMaterialTypes()
    {
        var path = Global.Absolute(@"Assets\Model\ItemV2\item.csv");
        var result = new Dictionary<string, string>();

        using var parser = new TextFieldParser(path, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()!;

        int nameIndex = FindHeaderIndex(headers, "item_name");
        int typeIndex = FindHeaderIndex(headers, "material_type", "materialType", "item_type", "itemType", "material_category", "category");

        while (!parser.EndOfData)
        {
            var columns = parser.ReadFields()!;
            var name = columns[nameIndex].Trim();
            var materialType = columns[typeIndex].Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(materialType))
            {
                result[name] = materialType;
            }
        }

        return result;
    }

    /// <summary>
    /// 查找 CSV 表头中第一个匹配的列索引。
    /// </summary>
    /// <param name="headers">CSV 表头。</param>
    /// <param name="candidates">可接受的列名候选。</param>
    /// <returns>匹配到的列索引；未找到时返回 -1。</returns>
    private static int FindHeaderIndex(string[] headers, params string[] candidates)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var header = headers[i]?.Trim();
            if (candidates.Any(candidate => string.Equals(header, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 确认当前界面是合成界面。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前不在合成界面时抛出。</exception>
    private void EnsureInCraftingUi()
    {
        if (!IsInCraftingUi())
        {
            throw new InvalidOperationException("请先打开合成界面。");
        }
    }

    /// <summary>
    /// 判断当前是否处于合成界面。
    /// </summary>
    /// <returns>处于合成界面时返回 true。</returns>
    private bool IsInCraftingUi()
    {
        return ContainsText(Rect1080(40, 960, 720, 105), "筛选")
               && ContainsText(Rect1080(0, 0, 260, 95), "合成");
    }

    /// <summary>
    /// 选择材料筛选类型，失败时抛出异常。
    /// </summary>
    /// <param name="materialType">材料筛选类型。</param>
    /// <returns>异步任务。</returns>
    /// <exception cref="InvalidOperationException">未找到或无法点击筛选类型时抛出。</exception>
    private async Task SelectMaterialType(string materialType)
    {
        if (!await TrySelectMaterialType(materialType))
        {
            throw new InvalidOperationException($"未能选择材料筛选类型：{materialType}");
        }
    }

    /// <summary>
    /// 在筛选弹窗中选择指定材料类型。
    /// </summary>
    /// <param name="materialType">材料筛选类型。</param>
    /// <returns>选择成功时返回 true。</returns>
    private async Task<bool> TrySelectMaterialType(string materialType)
    {
        try
        {
        
            var confirmation = await Page.GetByText(materialType, Rect1080(165, 1000, 279, 34)).TryWaitFor(300);
            if (confirmation.Count > 0)
            {
                return true;
            }

            await Page.GetByText("筛选", Rect1080(90, 1000, 60, 33)).Click(3000);
            await Page.GetByText(materialType, Rect1080(35, 124, 243, 615)).Click(5000);
    

            throw new TimeoutException($"识别文字[{materialType}]在 10 次筛选后仍未确认！");
        }
        catch (TimeoutException e)
        {
            _logger.LogWarning("选择材料筛选类型 {MaterialType} 超时：{Message}", materialType, e.Message);
            return false;
        }
    }

    /// <summary>
    /// 查找并选中目标材料，失败时抛出异常。
    /// </summary>
    /// <returns>异步任务。</returns>
    /// <exception cref="InvalidOperationException">前 5 页未找到目标材料时抛出。</exception>
    private async Task FindAndSelectMaterial()
    {
        if (!await TryFindAndSelectMaterial())
        {
            throw new InvalidOperationException($"前 {MaxSearchPages} 页未找到材料：{_materialName}");
        }
    }

    /// <summary>
    /// 在合成列表中用图标模型查找并选中目标材料。
    /// </summary>
    /// <returns>找到并选中目标材料时返回 true。</returns>
    private async Task<bool> TryFindAndSelectMaterial()
    {
        var pageIndex = 0;
        var shouldStop = false;
        GridScreen gridScreen = new(CraftingGridParams, _logger, _ct);
        gridScreen.OnAfterTurnToNewPage += data =>
        {
            pageIndex++;
            GridScreen.DrawItemsAfterTurnToNewPage(data);
        };
        gridScreen.OnBeforeScroll += () => VisionContext.Instance().DrawContent.ClearAll();

        try
        {
            await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
            {
                if (pageIndex > MaxSearchPages || shouldStop)
                {
                    break;
                }

                using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
                using Mat icon = itemRegion.SrcMat.GetGridIcon();
                var candidate = _iconMatcher.Match(icon);
                if (candidate.Score < 0.75 || candidate.Name != _materialName)
                {
                    continue;
                }

                itemRegion.Click();
                await Delay(500, _ct);

                if (!ConfirmSelectedMaterial())
                {
                    _logger.LogWarning("模型识别到 {Name}，但详情区未确认同名材料，继续搜索。", _materialName);
                    continue;
                }

                shouldStop = true;
                return true;
            }
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
        }

        return false;
    }

    /// <summary>
    /// 通过右侧详情区 OCR 确认当前选中材料是否为目标材料。
    /// </summary>
    /// <returns>详情区包含目标材料名时返回 true。</returns>
    private bool ConfirmSelectedMaterial()
    {
        return ContainsText(Rect1080(1040, 90, 760, 175), _materialName);
    }

    /// <summary>
    /// 设置合成次数并校验最终数量。
    /// </summary>
    /// <param name="targetCount">目标合成次数。</param>
    /// <returns>最终校验到的合成次数。</returns>
    /// <exception cref="InvalidOperationException">最大可合成次数、当前合成次数读取失败，材料不足或最终数量不匹配时抛出。</exception>
    private async Task<int> SetCraftCount(int targetCount)
    {
        var addButton = (X: 1614, Y: 672);
        var reduceButton = (X: 1074, Y: 672);

        var maxCount = ReadMaxCraftCount();
        if (maxCount <= 0)
        {
            throw new InvalidOperationException("未能识别最大可合成次数。");
        }

        if (targetCount > maxCount)
        {
            throw new InvalidOperationException($"材料不足以合成指定数量，目标 {targetCount}，当前最多可合成 {maxCount}。");
        }

        if (maxCount == 1)
        {
            return 1;
        }

        await DragSliderToRatio(0);
        await Delay(250, _ct);

        var ratio = Math.Clamp((targetCount - 1d) / (maxCount - 1d), 0d, 1d);
        await DragSliderToRatio(ratio);
        await Delay(350, _ct);

        var actualCount = ReadCurrentCraftCount();
        if (actualCount <= 0)
        {
            throw new InvalidOperationException("未能读取当前合成次数。");
        }

        if (Math.Abs(actualCount - targetCount) > 30)
        {
            await DragSliderToRatio(ratio);
            await Delay(350, _ct);
            actualCount = ReadCurrentCraftCount();
            if (actualCount <= 0 || Math.Abs(actualCount - targetCount) > 30)
            {
                throw new InvalidOperationException($"滑块调整合成次数后校验失败，目标 {targetCount}，当前 {actualCount}。");
            }
        }

        while (actualCount < targetCount)
        {
            GameCaptureRegion.GameRegion1080PPosClick(addButton.X, addButton.Y);
            await Delay(60, _ct);
            actualCount++;
        }

        while (actualCount > targetCount)
        {
            GameCaptureRegion.GameRegion1080PPosClick(reduceButton.X, reduceButton.Y);
            await Delay(60, _ct);
            actualCount--;
        }

        await Delay(200, _ct);
        var verifiedCount = ReadCurrentCraftCount();
        if (verifiedCount <= 0)
        {
            throw new InvalidOperationException("最终合成次数读取失败。");
        }

        if (verifiedCount != targetCount)
        {
            throw new InvalidOperationException($"最终合成次数与目标不一致，目标 {targetCount}，当前 {verifiedCount}。");
        }

        return verifiedCount;
    }

    /// <summary>
    /// 读取右侧当前合成次数。
    /// </summary>
    /// <returns>识别到的当前合成次数；失败时返回 0。</returns>
    private int ReadCurrentCraftCount()
    {
        var text = StringUtils.ConvertFullWidthNumToHalfWidth(ReadOcrText(Rect1080(1220, 600, 300, 85)));
        return ReadFirstPositiveInt(text);
    }

    /// <summary>
    /// 读取当前材料最大可合成次数。
    /// </summary>
    /// <returns>最大可合成次数；识别失败时返回 0。</returns>
    private int ReadMaxCraftCount()
    {
        var maxText = StringUtils.ConvertFullWidthNumToHalfWidth(ReadOcrText(Rect1080(1500, 625, 135, 90)));
        var maxCount = ReadFirstPositiveInt(maxText);
        if (maxCount > 0)
        {
            return maxCount;
        }

        var materialText = StringUtils.ConvertFullWidthNumToHalfWidth(ReadOcrText(Rect1080(815, 742, 1055, 215)));
        var counts = FractionRegex.Matches(materialText)
            .Select(match =>
            {
                var owned = StringUtils.TryParseInt(match.Groups[1].Value, 0);
                var required = StringUtils.TryParseInt(match.Groups[2].Value, 0);
                return required > 0 ? owned / required : 0;
            })
            .Where(value => value > 0)
            .ToList();

        return counts.Count > 0 ? counts.Min() : 0;
    }

    /// <summary>
    /// 从文本中读取第一个正整数。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <returns>第一个正整数；没有数字时返回 0。</returns>
    private static int ReadFirstPositiveInt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var match = PositiveIntRegex.Match(StringUtils.RemoveAllSpace(text));
        return match.Success ? StringUtils.TryParseInt(match.Value, 0) : 0;
    }

    /// <summary>
    /// 将合成数量滑块拖动到指定比例位置。
    /// </summary>
    /// <param name="ratio">滑块比例，0 表示最左侧，1 表示最右侧。</param>
    /// <returns>异步任务。</returns>
    private async Task DragSliderToRatio(double ratio)
    {
        var x = SliderStartX + (1521 - SliderStartX) * Math.Clamp(ratio, 0d, 1d);
        GameCaptureRegion.GameRegion1080PPosMove(SliderStartX, SliderY);
        await Delay(80, _ct);
        _input.Mouse.LeftButtonDown();
        try
        {
            const int steps = 12;
            for (int step = 0; step <= steps; step++)
            {
                var currentX = SliderStartX + (x - SliderStartX) * step / steps;
                GameCaptureRegion.GameRegion1080PPosMove(currentX, SliderY);
                await Delay(25, _ct);
            }
        }
        finally
        {
            _input.Mouse.LeftButtonUp();
        }
    }

    /// <summary>
    /// 提交合成并按顺序确认合成消耗与产物获得弹窗。
    /// </summary>
    /// <returns>异步任务。</returns>
    /// <exception cref="InvalidOperationException">合成提交或任一确认按钮未能识别点击时抛出。</exception>
    private async Task SubmitCraftAndClaimResult()
    {
        try
        {   //分别是右下角合成按钮，弹窗确认合成按钮，弹窗产物确认按钮
            await Page.GetByText("合成", Rect1080(1588, 967, 332, 113)).Click(5000);
            await Page.GetByText("确认", Rect1080(980, 725, 370, 70)).Click(5000);
            await Page.GetByText("确认", Rect1080(790, 875, 340, 65)).Click(5000);
        }
        catch (TimeoutException e)
        {
            throw new InvalidOperationException("合成提交与确认流程失败。", e);
        }
    }

    /// <summary>
    /// 读取指定区域内的 OCR 文本。
    /// </summary>
    /// <param name="rect">待识别区域。</param>
    /// <returns>按从上到下、从左到右顺序拼接的 OCR 文本。</returns>
    private string ReadOcrText(Rect rect)
    {
        return string.Concat(Page.Ocr(rect)
            .OrderBy(region => region.Y)
            .ThenBy(region => region.X)
            .Select(region => region.Text));
    }

    /// <summary>
    /// 判断指定区域内是否包含目标文本。
    /// </summary>
    /// <param name="rect">待识别区域。</param>
    /// <param name="text">目标文本。</param>
    /// <returns>包含目标文本时返回 true。</returns>
    private bool ContainsText(Rect rect, string text)
    {
        var normalizedText = StringUtils.RemoveAllSpace(ReadOcrText(rect));
        return normalizedText.Contains(StringUtils.RemoveAllSpace(text), StringComparison.Ordinal);
    }

    /// <summary>
    /// 将 1080P 基准矩形转换为当前截图尺寸矩形。
    /// </summary>
    /// <param name="x">1080P 基准左上角 X。</param>
    /// <param name="y">1080P 基准左上角 Y。</param>
    /// <param name="width">1080P 基准宽度。</param>
    /// <param name="height">1080P 基准高度。</param>
    /// <returns>当前截图尺寸下的矩形。</returns>
    private static Rect Rect1080(int x, int y, int width, int height)
    {
        var scale = TaskContext.Instance().SystemInfo.AssetScale;
        return new Rect(
            (int)Math.Round(x * scale),
            (int)Math.Round(y * scale),
            (int)Math.Round(width * scale),
            (int)Math.Round(height * scale));
    }
}
