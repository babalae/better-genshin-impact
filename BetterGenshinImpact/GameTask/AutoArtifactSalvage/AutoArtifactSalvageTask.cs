using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using Fischless.WindowsInput;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage;

/// <summary>
/// 圣遗物自动分解
/// </summary>
public class AutoArtifactSalvageTask : ISoloTask
{
    private readonly ILogger logger = App.GetLogger<AutoArtifactSalvageTask>();
    private readonly InputSimulator input = Simulation.SendInput;

    private CancellationToken ct;

    public string Name => "圣遗物分解独立任务";

    private readonly int star;

    private readonly string quickSelectLocalizedString;

    private readonly string[] numOfStarLocalizedString;

    private readonly string? javaScript;

    private readonly int? maxNumToCheck;

    private readonly bool returnToMainUi = true;

    private readonly CultureInfo cultureInfo;

    public AutoArtifactSalvageTask(int star, string? javaScript = null, int? maxNumToCheck = null)
    {
        this.star = star;
        this.javaScript = javaScript;
        this.maxNumToCheck = maxNumToCheck;
        IStringLocalizer<AutoArtifactSalvageTask> stringLocalizer = App.GetService<IStringLocalizer<AutoArtifactSalvageTask>>() ?? throw new NullReferenceException();
        this.cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
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

    public static async Task OpenBag(GridScreenName gridScreenName, InputSimulator input, ILogger logger, CancellationToken ct)
    {
        RecognitionObject? recognitionObjectChecked;
        RecognitionObject? recognitionObjectUnchecked;

        switch (gridScreenName)
        {
            case GridScreenName.Weapons:
                recognitionObjectChecked = ElementAssets.Instance.BagWeaponChecked;
                recognitionObjectUnchecked = ElementAssets.Instance.BagWeaponUnchecked;
                break;
            case GridScreenName.Artifacts:
                recognitionObjectChecked = ElementAssets.Instance.BagArtifactChecked;
                recognitionObjectUnchecked = ElementAssets.Instance.BagArtifactUnchecked;
                break;
            case GridScreenName.CharacterDevelopmentItems:
                recognitionObjectChecked = ElementAssets.Instance.BagCharacterDevelopmentItemChecked;
                recognitionObjectUnchecked = ElementAssets.Instance.BagCharacterDevelopmentItemUnchecked;
                break;
            case GridScreenName.Food:
                recognitionObjectChecked = ElementAssets.Instance.BagFoodChecked;
                recognitionObjectUnchecked = ElementAssets.Instance.BagFoodUnchecked;
                break;
            case GridScreenName.Materials:
                recognitionObjectChecked = ElementAssets.Instance.BagMaterialChecked;
                recognitionObjectUnchecked = ElementAssets.Instance.BagMaterialUnchecked;
                break;
            case GridScreenName.Gadget:
                recognitionObjectChecked = ElementAssets.Instance.BagGadgetChecked;
                recognitionObjectUnchecked = ElementAssets.Instance.BagGadgetUnchecked;
                break;
            case GridScreenName.Quest:
                recognitionObjectChecked = ElementAssets.Instance.BagQuestChecked;
                recognitionObjectUnchecked = ElementAssets.Instance.BagQuestUnchecked;
                break;
            case GridScreenName.PreciousItems:
                recognitionObjectChecked = ElementAssets.Instance.BagPreciousItemChecked;
                recognitionObjectUnchecked = ElementAssets.Instance.BagPreciousItemUnchecked;
                break;
            case GridScreenName.Furnishings:
                recognitionObjectChecked = ElementAssets.Instance.BagFurnishingChecked;
                recognitionObjectUnchecked = ElementAssets.Instance.BagFurnishingUnchecked;
                break;
            default:
                throw new NotSupportedException($"背包不支持的界面：{gridScreenName.GetDescription()}");
        }

        // B键打开背包
        input.SimulateAction(GIActions.OpenInventory);
        await Delay(1000, ct);

        var openBagSuccess = await NewRetry.WaitForAction(() =>
        {
            using var ra = CaptureToRectArea();
            using var artifactBtn = ra.Find(recognitionObjectChecked);
            if (artifactBtn.IsEmpty())
            {
                using var artifactBtn2 = ra.Find(recognitionObjectUnchecked);
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
            logger.LogError("未找到背包中{name}菜单按钮,打开背包失败", gridScreenName.GetDescription());
            return;
        }


        await Delay(800, ct);
    }

    public async Task Start(CancellationToken ct)
    {
        this.ct = ct;
        if (returnToMainUi)
        {
            await new ReturnMainUiTask().Start(ct);
        }

        await OpenBag(GridScreenName.Artifacts, this.input, this.logger, this.ct);

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
                if (javaScript != null)
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
        if (javaScript != null)
        {
            await Salvage5Star(this.javaScript, this.maxNumToCheck ?? throw new ArgumentException($"{nameof(this.maxNumToCheck)}不能为空"));
            logger.LogInformation("筛选完毕，请复查并手动分解");
        }
        else
        {
            input.Keyboard.KeyPress(User32.VK.VK_ESCAPE);

            if (returnToMainUi)
            {
                await new ReturnMainUiTask().Start(ct);
            }
        }
    }

    private async Task Salvage5Star(string javaScript, int maxNumToCheck)
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
                    ArtifactStat artifact = GetArtifactStat(card.SrcMat, OcrFactory.Paddle, this.cultureInfo, out string allText);

                    if (IsMatchJavaScript(artifact, javaScript))
                    {
                        // logger.LogInformation(message: msg);
                    }
                    else
                    {
                        itemRegion.Click(); // 反选取消
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

    /// <summary>
    /// 是否匹配JavaScript的计算结果
    /// </summary>
    /// <param name="artifact">作为JS入参，JS使用“ArtifactStat”获取</param>
    /// <param name="javaScript"></param>
    /// <param name="engine">由调用者控制生命周期</param>
    /// <returns>是否匹配。取JS的“Output”作为出参</returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="Exception"></exception>
    public static bool IsMatchJavaScript(ArtifactStat artifact, string javaScript)
    {
        using V8ScriptEngine engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.DisableGlobalMembers);
        try
        {
            // 传入输入参数
            engine.Script.ArtifactStat = artifact;

            // 执行JavaScript代码
            engine.Execute(javaScript);

            // 检查是否有输出
            if (!engine.Script.propertyIsEnumerable("Output"))
            {
                throw new InvalidOperationException("JavaScript没有设置Output输出");
            }

            if (engine.Script.Output is not bool)
            {
                throw new InvalidOperationException("JavaScript的Output输出不是布尔类型");
            }

            return (bool)engine.Script.Output;
        }
        catch (ScriptEngineException ex)
        {
            throw new Exception($"JavaScript execution error: {ex.Message}", ex);
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

    public static ArtifactStat GetArtifactStat(Mat src, IOcrService ocrService, CultureInfo cultureInfo, out string allText)
    {
        var ocrResult = ocrService.OcrResult(src);
        allText = ocrResult.Text;
        var lines = ocrResult.Text.Split('\n');
        string percentStr = "%";

        // 名称
        string name = lines[0];

        #region 主词条
        var defaultMainAffix = ArtifactAffix.DefaultStrDic.Select(kvp => kvp.Value).Distinct();
        string mainAffixTypeLine = lines.Single(l => defaultMainAffix.Contains(l));
        ArtifactAffixType mainAffixType = ArtifactAffix.DefaultStrDic.First(kvp => kvp.Value == mainAffixTypeLine).Key;
        string mainAffixValueLine = lines.Select(l =>
        {
            string pattern = @"^(\d+\.?\d*)(%?)$";
            pattern = pattern.Replace("%", percentStr);   // 这样一行一行写只是为了IDE能保持正则字符串高亮
            Match match = Regex.Match(l, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            else
            {
                return null;
            }
        }).Where(l => l != null).Cast<string>().Single();
        if (!float.TryParse(mainAffixValueLine, NumberStyles.Any, cultureInfo, out float value))
        {
            throw new Exception($"未识别的主词条数值：{mainAffixValueLine}");
        }
        ArtifactAffix mainAffix = new ArtifactAffix(mainAffixType, value);
        #endregion

        #region 副词条
        ArtifactAffix[] minorAffixes = lines.Select(l =>
        {
            string pattern = @"^[•·]?([^+]+)\+(\d+\.?\d*)(%?)$";
            pattern = pattern.Replace("%", percentStr);
            Match match = Regex.Match(l, pattern);
            if (match.Success)
            {
                ArtifactAffixType artifactAffixType;
                var dic = ArtifactAffix.DefaultStrDic;

                if (match.Groups[1].Value.Contains(dic[ArtifactAffixType.ATK]))
                {
                    if (String.IsNullOrEmpty(match.Groups[3].Value))
                    {
                        artifactAffixType = ArtifactAffixType.ATK;
                    }
                    else
                    {
                        artifactAffixType = ArtifactAffixType.ATKPercent;
                    }
                }
                else if (match.Groups[1].Value.Contains(dic[ArtifactAffixType.DEF]))
                {
                    if (String.IsNullOrEmpty(match.Groups[3].Value))
                    {
                        artifactAffixType = ArtifactAffixType.DEF;
                    }
                    else
                    {
                        artifactAffixType = ArtifactAffixType.DEFPercent;
                    }
                }
                else if (match.Groups[1].Value.Contains(dic[ArtifactAffixType.HP]))
                {
                    if (String.IsNullOrEmpty(match.Groups[3].Value))
                    {
                        artifactAffixType = ArtifactAffixType.HP;
                    }
                    else
                    {
                        artifactAffixType = ArtifactAffixType.HPPercent;
                    }
                }
                else if (match.Groups[1].Value.Contains(dic[ArtifactAffixType.CRITRate]))
                {
                    artifactAffixType = ArtifactAffixType.CRITRate;
                }
                else if (match.Groups[1].Value.Contains(dic[ArtifactAffixType.CRITDMG]))
                {
                    artifactAffixType = ArtifactAffixType.CRITDMG;
                }
                else if (match.Groups[1].Value.Contains(dic[ArtifactAffixType.ElementalMastery]))
                {
                    artifactAffixType = ArtifactAffixType.ElementalMastery;
                }
                else if (match.Groups[1].Value.Contains(dic[ArtifactAffixType.EnergyRecharge]))
                {
                    artifactAffixType = ArtifactAffixType.EnergyRecharge;
                }
                else
                {
                    throw new Exception($"未识别的副词条：{match.Groups[1].Value}");
                }

                if (!float.TryParse(match.Groups[2].Value, NumberStyles.Any, cultureInfo, out float value))
                {
                    throw new Exception($"未识别的副词条数值：{match.Groups[2].Value}");
                }
                return new ArtifactAffix(artifactAffixType, value);
            }
            else
            {
                return null;
            }
        }).Where(a => a != null).Cast<ArtifactAffix>().ToArray();
        #endregion

        #region 等级
        string levelLine = lines.Select(l =>
        {
            string pattern = @"^\+(\d*)$";
            Match match = Regex.Match(l, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            else
            {
                return null;
            }
        }).Where(l => l != null).Cast<string>().Single();
        if (!int.TryParse(levelLine, out int level) || level < 0 || level > 20)
        {
            throw new Exception($"未识别的等级：{levelLine}");
        }
        #endregion

        return new ArtifactStat(name, mainAffix, minorAffixes, level);
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