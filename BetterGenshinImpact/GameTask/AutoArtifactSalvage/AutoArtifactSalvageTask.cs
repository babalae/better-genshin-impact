using BetterGenshinImpact.GameTask.Common.BgiVision;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Microsoft.Extensions.Localization;
using System.Globalization;
using BetterGenshinImpact.Helpers;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.Model.Area;
using System.Collections.Generic;
using Fischless.WindowsInput;
using OpenCvSharp;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.GameUI;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage;

/// <summary>
/// 圣遗物自动分解
/// </summary>
public class AutoArtifactSalvageTask : ISoloTask
{
    private readonly ILogger logger = App.GetLogger<AutoArtifactSalvageTask>();
    private readonly InputSimulator input = Simulation.SendInput;
    private readonly ReturnMainUiTask _returnMainUiTask = new();

    private CancellationToken ct;

    public string Name => "圣遗物分解独立任务";

    private readonly int star;

    private readonly string quickSelectLocalizedString;

    private readonly string[] numOfStarLocalizedString;

    private readonly string? regularExpression;

    private readonly int? maxNumToCheck;

    private bool returnToMainUi = true;

    public AutoArtifactSalvageTask(int star, string? regularExpression = null, int? maxNumToCheck = null)
    {
        this.star = star;
        this.regularExpression = regularExpression;
        this.maxNumToCheck = maxNumToCheck;
        IStringLocalizer<AutoArtifactSalvageTask> stringLocalizer = App.GetService<IStringLocalizer<AutoArtifactSalvageTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        quickSelectLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "快速选择");
        numOfStarLocalizedString =
        [
            stringLocalizer.WithCultureGet(cultureInfo, "1星圣遗物"),
            stringLocalizer.WithCultureGet(cultureInfo, "2星圣遗物"),
            stringLocalizer.WithCultureGet(cultureInfo, "3星圣遗物"),
            stringLocalizer.WithCultureGet(cultureInfo, "4星圣遗物")
        ];
    }

    public AutoArtifactSalvageTask(int star, bool returnToMainUi) : this(star)
    {
        this.returnToMainUi = returnToMainUi;
    }

    public async Task Start(CancellationToken ct)
    {
        this.ct = ct;
        if (returnToMainUi)
        {
            await _returnMainUiTask.Start(ct);
        }

        // B键打开背包
        input.SimulateAction(GIActions.OpenInventory);
        await Delay(1000, ct);

        var openBagSuccess = await NewRetry.WaitForAction(() =>
        {
            // 选择圣遗物
            using var ra = CaptureToRectArea();
            using var artifactBtn = ra.Find(ElementAssets.Instance.BagArtifactChecked);
            if (artifactBtn.IsEmpty())
            {
                using var artifactBtn2 = ra.Find(ElementAssets.Instance.BagArtifactUnchecked);
                if (artifactBtn2.IsExist())
                {
                    artifactBtn2.Click();
                    return true;
                }
            }
            else
            {
                return true;
            }

            // 如果还在主界面就尝试再按下B键打开背包
            if (Bv.IsInMainUi(ra))
            {
                Debug.WriteLine("背包打开失败,再次尝试打开背包");
                input.SimulateAction(GIActions.OpenInventory);
            }

            return false;
        }, ct, 5);

        if (!openBagSuccess)
        {
            logger.LogError("未找到背包中圣遗物菜单按钮,打开背包失败");
            return;
        }


        await Delay(800, ct);

        // 点击分解按钮打开分解界面
        using var ra2 = CaptureToRectArea();
        using var salvageBtn = ra2.Find(ElementAssets.Instance.BtnArtifactSalvage);
        if (salvageBtn.IsExist())
        {
            salvageBtn.Click();
            await Delay(1000, ct);
        }
        else
        {
            logger.LogError("未找到圣遗物分解按钮");
            return;
        }

        // 快速选择
        using var ra3 = CaptureToRectArea();
        var ocrList = ra3.FindMulti(RecognitionObject.Ocr(ra3.ToRect().CutLeftBottom(0.25, 0.1)));
        foreach (var ocr in ocrList)
        {
            if (Regex.IsMatch(ocr.Text, quickSelectLocalizedString))
            {
                ocr.Click();
                await Delay(500, ct);
                break;
            }
        }

        // 确认选择
        // 5.5 变成反选
        using var ra4 = CaptureToRectArea();
        if (star < 4)
        {
            List<Region> ocrList2 = ra4.FindMulti(RecognitionObject.Ocr(ra4.ToRect().CutLeft(0.20)));
            for (int i = star; i < 4; i++)
            {
                foreach (var ocr in ocrList2)
                {
                    if (Regex.IsMatch(ocr.Text, numOfStarLocalizedString[i]))
                    {
                        ocr.Click();
                        await Delay(500, ct);
                        break;
                    }
                }
            }
        }

        Bv.ClickWhiteConfirmButton(ra4);
        await Delay(1500, ct);


        // 点击分解
        using var ra5 = CaptureToRectArea();
        var salvageBtnConfirm = ra5.Find(ElementAssets.Instance.BtnArtifactSalvageConfirm);
        if (salvageBtnConfirm.IsExist())
        {
            salvageBtnConfirm.Click();
            await Delay(1000, ct);
            // 点击确认
            using var ra6 = CaptureToRectArea();
            if (Bv.ClickBlackConfirmButton(ra6))
            {
                logger.LogInformation("完成{Star}星圣遗物快速分解", star);
                await Delay(400, ct);
                if (regularExpression != null)
                {
                    input.Mouse.LeftButtonClick();
                    await Delay(1000, ct);
                }
            }
            else
            {
                logger.LogInformation("未找到进行分解按钮，可能发生了卡顿");
            }
        }
        else
        {
            logger.LogInformation("未找到圣遗物分解按钮，可能已经没有圣遗物需要快速分解");
        }

        // 分解5星
        if (regularExpression != null)
        {
            await Salvage5Star(this.regularExpression, this.maxNumToCheck ?? throw new ArgumentException($"{nameof(this.maxNumToCheck)}不能为空"));
            logger.LogInformation("筛选完毕，请复查并手动分解");
        }
        else
        {
            input.Keyboard.KeyPress(User32.VK.VK_ESCAPE);

            if (returnToMainUi)
            {
                await _returnMainUiTask.Start(ct);
            }
        }
    }

    private async Task Salvage5Star(string regularExpression, int maxNumToCheck)
    {
        int count = maxNumToCheck;

        using var ra0 = CaptureToRectArea();
        GridScreenParams gridParams = GridScreenParams.Templates[GridScreenName.ArtifactSalvage];
        Rect gridRoi = gridParams.GetRect(ra0);
        GridScreen gridScreen = new GridScreen(gridRoi, gridParams, this.logger, this.ct); // 圣遗物分解Grid有4行9列
        await foreach (ImageRegion itemRegion in gridScreen)
        {
            Rect gridRect = itemRegion.ToRect();
            if (GetArtifactStatus(itemRegion.SrcMat) == ArtifactStatus.None)
            {
                itemRegion.Click();
                await Delay(300, ct);

                using var ra1 = CaptureToRectArea();
                using ImageRegion itemRegion1 = ra1.DeriveCrop(gridRect + new Point(gridRoi.X, gridRoi.Y));
                if (GetArtifactStatus(itemRegion1.SrcMat) == ArtifactStatus.Selected)
                {
                    using ImageRegion card = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.70), (int)(ra1.Width * 0.055), (int)(ra1.Width * 0.24), (int)(ra1.Width * 0.29)));
                    string affixes = GetArtifactAffixes(card.SrcMat, OcrFactory.Paddle);

                    if (IsMatchRegularExpression(affixes, regularExpression, out string msg))
                    {
                        logger.LogInformation(message: msg);
                    }
                    else
                    {
                        itemRegion.Click();
                        await Delay(100, ct);
                    }
                }

                count--;
                if (count <= 0)
                {
                    logger.LogInformation("检查次数已耗尽");
                    break;
                }
            }
        }
    }

    public static bool IsMatchRegularExpression(string affixes, string regularExpression, out string msg)
    {
        Match match = Regex.Match(affixes, regularExpression);
        if (match.Success)
        {
            if (string.IsNullOrEmpty(match.Value))
            {
                msg = "匹配成功！";
            }
            else
            {
                msg = $"匹配成功：{match.Value}";
            }
        }
        else
        {
            msg = "匹配失败！";
        }

        return match.Success;
    }

    public static string GetArtifactAffixes(Mat src, IOcrService ocrService)
    {
        var ocrResult = ocrService.OcrResult(src);
        return ocrResult.Text;
    }

    public static ArtifactStatus GetArtifactStatus(Mat src)
    {
        using Mat upperLine = new Mat(src, new Rect(0, 0, src.Width, (int)(src.Height * 0.19)));
        //using Mat hsvMat = upperLine.CvtColor(ColorConversionCodes.BGR2HSV_FULL);
        //var pixel_hsv_pink = hsvMat.At<Vec3b>(17, 12);    // 注意是（Y，X）
        //var pixel_hsv_grren = hsvMat.At<Vec3b>(8, 105);   // 注意是（Y，X）

        // 粉色锁
        Scalar pinkhsv = OpenCvCommonHelper.CommonHSV2OpenCVHSVFull(new Scalar(9, 0.54, 1.00));
        var lowPink = new Scalar(pinkhsv.Val0 - 3, pinkhsv.Val1 - 25, pinkhsv.Val2 - 25);
        var highPink = new Scalar(pinkhsv.Val0 + 3, pinkhsv.Val1 + 25, pinkhsv.Val2);
        using Mat pinkMask = OpenCvCommonHelper.InRangeHsvFull(upperLine, lowPink, highPink);

        using Mat pinkThreshold = pinkMask.Threshold(0, 255, ThresholdTypes.Binary); //二值化

        Cv2.FindContours(pinkThreshold, out var contours, out _, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple, null);

        var allPinkContours = contours.Where(c => c.Max(p => p.X) < pinkMask.Width * 0.2) // 都在左侧
            .SelectMany(c => c); // 拼凑零碎的像素
        if (allPinkContours.Any())
        {
            var bounding = Cv2.BoundingRect(allPinkContours);
            if (bounding.Width > pinkMask.Width * 0.07 && bounding.Height > pinkMask.Height * 0.3) //不能太小
            {
                return ArtifactStatus.Locked;
            }
        }

        // 绿色线
        Scalar greenhsv = OpenCvCommonHelper.CommonHSV2OpenCVHSVFull(new Scalar(80, 0.76, 1.00));
        var lowGreen = new Scalar(greenhsv.Val0 - 3, greenhsv.Val1 - 10, greenhsv.Val2 - 5);
        var highGreen = new Scalar(greenhsv.Val0 + 3, greenhsv.Val1 + 10, greenhsv.Val2);
        using Mat greenMask = OpenCvCommonHelper.InRangeHsvFull(upperLine, lowGreen, highGreen);

        Cv2.Threshold(greenMask, greenMask, 0, 255, ThresholdTypes.Binary); //二值化

        Cv2.FindContours(greenMask, out contours, out _, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple, null);

        var allGreenContours = contours.SelectMany(c => c); // 拼凑零碎的像素
        if (allGreenContours.Any())
        {
            var bounding = Cv2.BoundingRect(allGreenContours);
            if (bounding.Width > greenMask.Width * 0.2 && bounding.Height > greenMask.Height * 0.8) //不能太小；至少存在右上角勾号的绿色背景，忽略上底边的绿线（因有缩放动画，每次都要重新框定不利缩减步骤）
            {
                return ArtifactStatus.Selected;
            }
        }

        return ArtifactStatus.None;
    }

    /// <summary>
    /// 圣遗物分解界面Grid元素的状态
    /// </summary>
    public enum ArtifactStatus
    {
        /// <summary>
        /// 啥也没有
        /// </summary>
        None,

        /// <summary>
        /// 左上角有粉色锁定标记
        /// </summary>
        Locked,

        /// <summary>
        /// 上下有绿色选择框
        /// </summary>
        Selected
    }
}