using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.GetGridIcons;

/// <summary>
/// 获取Grid界面的物品图标
/// </summary>
[SoloTaskInfo(TaskId, "截取物品图标", Group = "调试", Order = 610, Scopes = SoloTaskScopes.Standalone)]
public class GetGridIconsTask : ISoloTask<GetGridIconsParam, NoData>
{
    /// <summary>
    /// 任务标识。
    /// </summary>
    public const string TaskId = "get_grid_icons";

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    private readonly ILogger logger = App.GetLogger<GetGridIconsTask>();
    private IInputChannel input => InputHub.Foreground;

    private CancellationToken ct;

    /// <summary>
    /// 最多获取的数量，为空表示不限；在 RunAsync 开头由参数赋值
    /// </summary>
    private int? maxNumToGet;

    /// <summary>
    /// 目标 Grid 界面；在 RunAsync 开头由参数赋值
    /// </summary>
    private GridScreenName gridScreenName;

    /// <summary>
    /// 是否把星级作为文件名后缀；在 RunAsync 开头由参数赋值
    /// </summary>
    private bool starAsSuffix;

    /// <summary>
    /// 创建获取 Grid 界面物品图标任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public GetGridIconsTask(GameRuntime runtime)
    {
        _runtime = runtime;
    }

    /// <summary>
    /// 遍历目标 Grid 界面，截取物品图标并按物品名保存到 log/gridIcons 下的时间戳目录。
    /// </summary>
    /// <param name="param">截取参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>流程结束返回 Unverified（图片在后台线程保存，不等待保存结果）。</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(GetGridIconsParam param, CancellationToken ct)
    {
        this.ct = ct;
        this.gridScreenName = param.GridName;
        this.starAsSuffix = param.StarAsSuffix;
        this.maxNumToGet = param.MaxNumToGet;

        int count = this.maxNumToGet ?? int.MaxValue;
        string directory = Path.Combine(AppContext.BaseDirectory, "log/gridIcons", $"{this.gridScreenName}{DateTime.Now:yyyyMMddHHmmss}");
        Directory.CreateDirectory(directory);

        switch (this.gridScreenName)
        {
            case GridScreenName.Weapons:
            case GridScreenName.Artifacts:
            case GridScreenName.CharacterDevelopmentItems:
            case GridScreenName.Food:
            case GridScreenName.Materials:
            case GridScreenName.Gadget:
            case GridScreenName.Quest:
            case GridScreenName.PreciousItems:
            case GridScreenName.Furnishings:
                await new ReturnMainUiTask(_runtime).Start(ct);
                await AutoArtifactSalvageTask.OpenInventory(_runtime, this.gridScreenName, this.input, this.logger, this.ct);
                break;
            case GridScreenName.ArtifactSetFilter:
                logger.LogInformation("{name}暂不支持自动打开，请提前手动打开界面", gridScreenName.GetDescription());
                await GetArtifactSetFilterGridIcons(count, directory);
                return SoloTaskResult.Unverified($"{gridScreenName.GetDescription()}图标截取结束，保存目录：{directory}");
            default:
                logger.LogInformation("{name}暂不支持自动打开，请提前手动打开界面", gridScreenName.GetDescription());
                break;
        }

        await GetInventoryGridIcons(count, directory);
        return SoloTaskResult.Unverified($"{gridScreenName.GetDescription()}图标截取结束，保存目录：{directory}");
    }

    private async Task GetInventoryGridIcons(int count, string directory)
    {
        GridScreen gridScreen = new GridScreen(_runtime, GridParams.Templates[this.gridScreenName], this.logger, this.ct);
        gridScreen.OnAfterTurnToNewPage += GridScreen.DrawItemsAfterTurnToNewPage;
        gridScreen.OnBeforeScroll += () => _runtime.MaskWindowDrawingBoard.ClearAll();
        HashSet<string> fileNames = new HashSet<string>();
        try
        {
            await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
            {
                using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
                itemRegion.Click();
                await _runtime.Delay(300, ct);

                using var ra1 = _runtime.CaptureToRectArea();
                using ImageRegion nameRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.682), (int)(ra1.Width * 0.0625), (int)(ra1.Width * 0.256), (int)(ra1.Width * 0.03125)));
                var ocrResult = OcrFactory.Paddle.OcrResult(nameRegion.SrcMat);
                string itemName = ocrResult.Text;
                string itemStar = "";
                if (this.starAsSuffix)
                {
                    using ImageRegion starRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.682), (int)(ra1.Width * 0.1823), (int)(ra1.Width * 0.105), (int)(ra1.Width * 0.02345)));
                    itemStar = String.Join(string.Empty, Enumerable.Repeat("★", GetStars(starRegion.SrcMat)));
                }

                string fileName = itemName + itemStar;
                if (fileNames.Add(fileName))
                {
                    string filePath = Path.Combine(directory, $"{fileName}.png");
                    SaveGridIconAsync(filePath, fileName, itemRegion.SrcMat);
                }
                else
                {
                    logger.LogInformation("重复的物品：{Text}", fileName);
                }

                count--;
                if (count <= 0)
                {
                    logger.LogInformation("检查次数已耗尽");
                    break;
                }
            }
        }
        finally
        {
            _runtime.MaskWindowDrawingBoard.ClearAll();
        }
    }

    private async Task GetArtifactSetFilterGridIcons(int count, string directory)
    {
        ArtifactSetFilterScreen gridScreen = new ArtifactSetFilterScreen(_runtime, new GridParams(new Rect(40, 100, 1300, 852), 2, 3, 40, 40, 0.024), this.logger, this.ct);
        HashSet<string> fileNames = new HashSet<string>();
        await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
        {
            using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
            itemRegion.Click();
            await _runtime.Delay(300, ct);

            static bool tryGetFlower(GameRuntime runtime, out string flowerName)
            {
                using var ra1 = runtime.CaptureToRectArea();
                using ImageRegion nameRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.714), (int)(ra1.Width * 0.284), (int)(ra1.Width * 0.256), (int)(ra1.Width * 0.208)));
                var ocrResult = OcrFactory.Paddle.OcrResult(nameRegion.SrcMat);

                var flowerWithGlyph = ocrResult.Regions.OrderBy(r => r.Rect.Center.Y).SkipWhile(r => !r.Text.Contains("套装包含")).Skip(1).FirstOrDefault();
                if (flowerWithGlyph == default)
                {
                    nameRegion.Move();
                    flowerName = string.Empty;
                    return false;
                }
                // 可能带有花形符号
                Rect flowerWithGlyphRect = flowerWithGlyph.Rect.BoundingRect();
                // 费解的是，原图识别没问题，但为了排除名称前的花形符号，无论裁切还是不裁切只是将符号涂白，都会把一些花名识别出旧体字
                // 花形符号往往还被识别为空格，导致无法用识别框位置来区分

                // 截取没有符号的区域再识别一次
                Rect flowerWithoutGlyph = new Rect((int)(ra1.Width * 0.028), (int)(flowerWithGlyphRect.Y - flowerWithGlyphRect.Height * 0), (int)(ra1.Width * 0.228), (int)(flowerWithGlyphRect.Height * 1));
                using Mat roi = nameRegion.SrcMat.SubMat(flowerWithoutGlyph);
                var whiteOcrResult = OcrFactory.Paddle.OcrResult(roi);
                flowerName = whiteOcrResult.Text;
                // 所以只好识别两次，Trim后根据字数取原截图OCR的结果……
                flowerName = flowerWithGlyph.Text.Trim().Substring(flowerWithGlyph.Text.Trim().Length - flowerName.Trim().Length);
                return true;
            }

            if (!tryGetFlower(_runtime, out string flowerName))
            {
                await _runtime.Delay(100, this.ct);
                for (int i = 0; i < 5; i++)
                {
                    this.input.Mouse.VerticalScroll(-2);
                    await _runtime.Delay(40, this.ct);
                }
                await _runtime.Delay(300, this.ct);
                if (!tryGetFlower(_runtime, out flowerName))
                {
                    throw new Exception("尝试获取生之花失败");
                    //flowerName = $"识别失败{nameRegion.GetHashCode()}";
                }
            }

            string fileName = flowerName;
            if (fileNames.Add(fileName))
            {
                string filePath = Path.Combine(directory, $"{fileName}.png");
                using Mat img125 = CropResizeArtifactSetFilterGridIcon(itemRegion, _runtime.SystemInfo);
                SaveGridIconAsync(filePath, fileName, img125);
            }
            else
            {
                logger.LogInformation("重复的物品：{Text}", fileName);
            }

            count--;
            if (count <= 0)
            {
                logger.LogInformation("检查次数已耗尽");
                break;
            }
        }
    }

    /// <summary>
    /// 在调用线程立刻深拷贝像素，后台线程只使用这份私有 <see cref="Mat"/>。
    /// 不取得 <paramref name="src"/> 的所有权，调用返回后外部释放 <paramref name="src"/> 不影响保存。
    /// </summary>
    private void SaveGridIconAsync(string filePath, string fileName, Mat src)
    {
        Mat privateMat = src.Clone();
        Thread saveThread = new Thread(() =>
        {
            try
            {
                using (privateMat)
                using (var bitmap = privateMat.ToBitmap())
                using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    bitmap.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                }
                logger.LogInformation("图片保存成功：{Text}", fileName);
            }
            catch (Exception e)
            {
                logger.LogError(e, "图片保存失败：{Text}", fileName);
            }
        });
        saveThread.IsBackground = true;
        saveThread.Start();
    }

    /// <summary>
    /// 从套装筛选界面的格子中裁出生之花图标并缩放为 125x125
    /// </summary>
    /// <param name="itemRegion">格子区域</param>
    /// <param name="systemInfo">运行环境的画面信息，用于换算素材缩放比例</param>
    internal static Mat CropResizeArtifactSetFilterGridIcon(ImageRegion itemRegion, ISystemInfo systemInfo)
    {
        double scale = systemInfo.AssetScale;
        double width = 60;
        double height = 60; // 宽高缩放似乎不一致，似乎在2.05:2.15之间，但不知道怎么测定
        // 低分辨率下 237 * scale 的偏移量可能大于 itemRegion 中心位置，导致 X 为负，加保护
        Rect iconRect = new Rect(
            (int)(itemRegion.Width / 2 - 237 * scale - width / 2),
            (int)(itemRegion.Height / 2 - height / 2),
            (int)width, (int)height).ClampTo(itemRegion.SrcMat);
        using Mat crop = itemRegion.SrcMat.SubMat(iconRect);
        return crop.Resize(new Size(125, 125));
    }

    /// <summary>
    /// OCR检测★字符很不稳定，因此用cv
    /// 非常简陋的色彩检测，请传入聚焦的图像，勿带入可能的干扰
    /// </summary>
    /// <param name="mat"></param>
    /// <returns></returns>
    public static int GetStars(Mat mat)
    {
        Scalar yellowLower = new Scalar(50 - 5, 204 - 5, 255 - 5);
        Scalar yellowUpper = new Scalar(50 + 5, 204 + 5, 255 + 0);
        using Mat mask = mat.InRange(yellowLower, yellowUpper);
        var contours = mask.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        return contours?.Length ?? 0;
    }
}