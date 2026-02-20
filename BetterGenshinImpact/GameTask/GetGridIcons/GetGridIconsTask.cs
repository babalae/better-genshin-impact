using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Drawable;
using Fischless.WindowsInput;
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
public class GetGridIconsTask : ISoloTask
{
    private readonly ILogger logger = App.GetLogger<GetGridIconsTask>();
    private readonly InputSimulator input = Simulation.SendInput;

    private CancellationToken ct;

    public string Name => "获取Grid界面物品图标独立任务";

    private readonly int? maxNumToGet;

    private readonly GridScreenName gridScreenName;

    private readonly bool starAsSuffix;

    public GetGridIconsTask(GridScreenName gridScreenName, bool starAsSuffix, int? maxNumToGet = null)
    {
        this.gridScreenName = gridScreenName;
        this.starAsSuffix = starAsSuffix;
        this.maxNumToGet = maxNumToGet;
    }

    public async Task Start(CancellationToken ct)
    {
        this.ct = ct;

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
                await new ReturnMainUiTask().Start(ct);
                await AutoArtifactSalvageTask.OpenInventory(this.gridScreenName, this.input, this.logger, this.ct);
                break;
            case GridScreenName.ArtifactSetFilter:
                logger.LogInformation("{name}暂不支持自动打开，请提前手动打开界面", gridScreenName.GetDescription());
                await GetArtifactSetFilterGridIcons(count, directory);
                return;
            default:
                logger.LogInformation("{name}暂不支持自动打开，请提前手动打开界面", gridScreenName.GetDescription());
                break;
        }

        await GetInventoryGridIcons(count, directory);
    }

    private async Task GetInventoryGridIcons(int count, string directory)
    {
        GridScreen gridScreen = new GridScreen(GridParams.Templates[this.gridScreenName], this.logger, this.ct);
        gridScreen.OnAfterTurnToNewPage += GridScreen.DrawItemsAfterTurnToNewPage;
        gridScreen.OnBeforeScroll += () => VisionContext.Instance().DrawContent.ClearAll();
        HashSet<string> fileNames = new HashSet<string>();
        try
        {
            await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
            {
                using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
                itemRegion.Click();
                await Delay(300, ct);

                using var ra1 = CaptureToRectArea();
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
                    Thread saveThread = new Thread(() =>
                    {
                        try
                        {
                            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                itemRegion.SrcMat.ToBitmap().Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                            }
                            logger.LogInformation("图片保存成功：{Text}", fileName);
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "图片保存失败：{Text}", fileName);
                        }
                    });
                    saveThread.IsBackground = true; // 设置为后台线程
                    saveThread.Start();
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
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }

    private async Task GetArtifactSetFilterGridIcons(int count, string directory)
    {
        ArtifactSetFilterScreen gridScreen = new ArtifactSetFilterScreen(new GridParams(new Rect(40, 100, 1300, 852), 2, 3, 40, 40, 0.024), this.logger, this.ct);
        HashSet<string> fileNames = new HashSet<string>();
        await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
        {
            using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
            itemRegion.Click();
            await Delay(300, ct);

            static bool tryGetFlower(out string flowerName)
            {
                using var ra1 = CaptureToRectArea();
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

            if (!tryGetFlower(out string flowerName))
            {
                await TaskControl.Delay(100, this.ct);
                for (int i = 0; i < 5; i++)
                {
                    this.input.Mouse.VerticalScroll(-2);
                    await TaskControl.Delay(40, this.ct);
                }
                await TaskControl.Delay(300, this.ct);
                if (!tryGetFlower(out flowerName))
                {
                    throw new Exception("尝试获取生之花失败");
                    //flowerName = $"识别失败{nameRegion.GetHashCode()}";
                }
            }

            string fileName = flowerName;
            if (fileNames.Add(fileName))
            {
                string filePath = Path.Combine(directory, $"{fileName}.png");
                Thread saveThread = new Thread(() =>
                {
                    try
                    {
                        using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            using Mat img125 = CropResizeArtifactSetFilterGridIcon(itemRegion);
                            img125.ToBitmap().Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        logger.LogInformation("图片保存成功：{Text}", fileName);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "图片保存失败：{Text}", fileName);
                    }
                });
                saveThread.IsBackground = true; // 设置为后台线程
                saveThread.Start();
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

    internal static Mat CropResizeArtifactSetFilterGridIcon(ImageRegion itemRegion, ISystemInfo? systemInfo = null)
    {
        double scale = (systemInfo ?? TaskContext.Instance().SystemInfo).AssetScale;
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