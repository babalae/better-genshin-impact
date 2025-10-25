using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Drawable;
using Fischless.WindowsInput;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using System;
using System.Collections.Frozen;
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
    private readonly ILogger logger;
    private readonly InputSimulator input = Simulation.SendInput;

    private CancellationToken ct;

    public string Name => "圣遗物分解独立任务";

    private readonly int star;

    private readonly string quickSelectLocalizedString;

    private readonly string[] numOfStarLocalizedString;

    private readonly string? javaScript;

    private readonly string? artifactSetFilter;

    private readonly int? maxNumToCheck;

    private readonly RecognitionFailurePolicy? recognitionFailurePolicy;

    private readonly bool returnToMainUi = true;

    private readonly CultureInfo? cultureInfo;

    private readonly FrozenDictionary<ArtifactAffixType, string> artifactAffixStrDic;

    public AutoArtifactSalvageTask(AutoArtifactSalvageTaskParam param, ILogger? logger = null)
    {
        this.star = param.Star;
        this.javaScript = param.JavaScript;
        this.artifactSetFilter = param.ArtifactSetFilter;
        this.maxNumToCheck = param.MaxNumToCheck;
        this.recognitionFailurePolicy = param.RecognitionFailurePolicy;
        this.logger = logger ?? App.GetLogger<AutoArtifactSalvageTask>();
        var stringLocalizer = param.StringLocalizer;
        this.cultureInfo = param.GameCultureInfo;
        quickSelectLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "快速选择");
        numOfStarLocalizedString =
        [
            stringLocalizer.WithCultureGet(cultureInfo, "1星圣遗物"),
            stringLocalizer.WithCultureGet(cultureInfo, "2星圣遗物"),
            stringLocalizer.WithCultureGet(cultureInfo, "3星圣遗物"),
            stringLocalizer.WithCultureGet(cultureInfo, "4星圣遗物")
        ];

        artifactAffixStrDic = ArtifactAffix.DefaultStrDic.Select(kvp => new KeyValuePair<ArtifactAffixType, string>(kvp.Key, stringLocalizer.WithCultureGet(cultureInfo, kvp.Value))).ToFrozenDictionary();
    }

    public static async Task OpenInventory(GridScreenName gridScreenName, InputSimulator input, ILogger logger, CancellationToken ct)
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
        await Delay(1200, ct);

        var openBagSuccess = await NewRetry.WaitForAction(() =>
        {
            using var ra = CaptureToRectArea();

            // 判断是否在提示对话框（物品过期提示）
            if (Bv.IsInPromptDialog(ra))
            {
                // 如果存在物品过期提示，则点击确认按钮
                Bv.ClickWhiteConfirmButton(ra.DeriveCrop(0, 0, ra.Width, ra.Height - ra.Height * 0.2));
                Sleep(300, ct);
                return false;
            }

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

        await OpenInventory(GridScreenName.Artifacts, this.input, this.logger, this.ct);

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
        bool quickSelectBtnFound = false;
        foreach (var ocr in ocrList)
        {
            if (Regex.IsMatch(ocr.Text, quickSelectLocalizedString))
            {
                quickSelectBtnFound = true;
                ocr.Click();
                await Delay(500, ct);
                break;
            }
        }
        if (!quickSelectBtnFound)
        {
            logger.LogError("没有找到可匹配{regex}的按钮，终止分解", quickSelectLocalizedString);
            return;
        }

        // 确认选择
        // 5.5 变成反选
        using var ra4 = CaptureToRectArea();
        if (star < 4)
        {
            List<Region> ocrList2 = ra4.FindMulti(RecognitionObject.Ocr(ra4.ToRect().CutLeft(0.20)));
            for (int i = star; i < 4; i++)
            {
                bool numOfStarFound = false;
                foreach (var ocr in ocrList2)
                {
                    if (Regex.IsMatch(ocr.Text, numOfStarLocalizedString[i]))
                    {
                        numOfStarFound = true;
                        ocr.Click();
                        await Delay(500, ct);
                        break;
                    }
                }
                if (!numOfStarFound)
                {
                    logger.LogError("没有找到可匹配{regex}的按钮，终止分解", numOfStarLocalizedString[i]);
                    return;
                }
            }
        }

        using var quickSelectConfirmBtn = ra4.Find(ElementAssets.Instance.BtnWhiteConfirm);
        if (quickSelectConfirmBtn.IsExist())
        {
            quickSelectConfirmBtn.Click();
        }
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
            if (!string.IsNullOrWhiteSpace(this.artifactSetFilter))
            {
                // 其实是点击筛选按钮……快速选择确认的这个按钮正好和筛选按钮位置重合，摆烂直接用了
                quickSelectConfirmBtn.Click();
                await Delay(400, ct);
                // 点击所属套装
                ra5.ClickTo(315, 190);
                await Delay(1000, ct);
                // 遍历套装Grid勾选套装
                using InferenceSession session = GridIconsAccuracyTestTask.LoadModel(out Dictionary<string, float[]> prototypes);
                ArtifactSetFilterScreen gridScreen = new ArtifactSetFilterScreen(new GridParams(new Rect(40, 100, 1300, 852), 2, 3, 40, 40, 0.024), this.logger, this.ct);
                string drawKey = "ArtifactSetFilter";
                var drawRectList = new List<RectDrawable>();
                var drawTextList = new List<TextDrawable>();
                gridScreen.OnBeforeScroll += () => { VisionContext.Instance().DrawContent.RemoveRect(drawKey); drawRectList.Clear(); drawTextList.Clear(); };
                try
                {
                    await foreach (ImageRegion itemRegion in gridScreen)
                    {
                        using Mat img125 = GetGridIconsTask.CropResizeArtifactSetFilterGridIcon(itemRegion);
                        (string? predName, _) = GridIconsAccuracyTestTask.Infer(img125, session, prototypes);
                        if (predName == null)
                        {
                            var rectDrawable = itemRegion.SelfToRectDrawable(drawKey);
                            drawRectList.Add(rectDrawable);
                            VisionContext.Instance().DrawContent.PutOrRemoveRectList(drawKey, drawRectList);
                            drawTextList.Add(new TextDrawable("识别失败", new System.Windows.Point(rectDrawable.Rect.X + rectDrawable.Rect.Width / 3, rectDrawable.Rect.Y)));
                            VisionContext.Instance().DrawContent.TextList.GetOrAdd(drawKey, drawTextList);
                        }
                        else
                        {
                            var rectDrawable = itemRegion.SelfToRectDrawable(drawKey, System.Drawing.Pens.Lime);
                            drawRectList.Add(rectDrawable);
                            VisionContext.Instance().DrawContent.PutOrRemoveRectList(drawKey, drawRectList);
                            drawTextList.Add(new TextDrawable(predName, new System.Windows.Point(rectDrawable.Rect.X + rectDrawable.Rect.Width / 3, rectDrawable.Rect.Y)));
                            VisionContext.Instance().DrawContent.TextList.GetOrAdd(drawKey, drawTextList);
                            if (this.artifactSetFilter.Contains(predName))
                            {
                                itemRegion.Click();
                                await Delay(100, ct);
                            }
                        }
                    }
                }
                finally
                {
                    VisionContext.Instance().DrawContent.ClearAll();
                }
                // 点击确认筛选
                using var confirmFilterBtnRegion = CaptureToRectArea();
                Bv.ClickWhiteConfirmButton(confirmFilterBtnRegion);
                await Delay(1500, ct);
                // 点击确认
                using var confirmBtnRegion = CaptureToRectArea();
                Bv.ClickWhiteConfirmButton(confirmBtnRegion);
                await Delay(600, ct);
            }

            // 逐一点选查看面板筛选
            await Salvage5Star();
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

    private async Task Salvage5Star()
    {
        string javaScript = this.javaScript ?? throw new ArgumentException($"{nameof(this.javaScript)}不能为空");
        int count = this.maxNumToCheck ?? throw new ArgumentException($"{nameof(this.maxNumToCheck)}不能为空");
        RecognitionFailurePolicy recognitionFailurePolicy = this.recognitionFailurePolicy ?? throw new ArgumentException($"{nameof(this.recognitionFailurePolicy)}不能为空");

        GridParams gridParams = GridParams.Templates[GridScreenName.ArtifactSalvage];
        GridScreen gridScreen = new GridScreen(gridParams, this.logger, this.ct); // 圣遗物分解Grid有4行9列
        gridScreen.OnAfterTurnToNewPage += GridScreen.DrawItemsAfterTurnToNewPage;
        gridScreen.OnBeforeScroll += () => VisionContext.Instance().DrawContent.ClearAll();
        try
        {
            await foreach (ImageRegion itemRegion in gridScreen)
            {
                Rect gridRect = itemRegion.ToRect();
                if (GetArtifactStatus(itemRegion.SrcMat) == ArtifactStatus.None)
                {
                    itemRegion.Click();
                    await Delay(300, ct);

                    using var ra1 = CaptureToRectArea();
                    using ImageRegion itemRegion1 = ra1.DeriveCrop(gridRect + new Point(gridParams.Roi.X, gridParams.Roi.Y));
                    if (GetArtifactStatus(itemRegion1.SrcMat) == ArtifactStatus.Selected)
                    {
                        using ImageRegion card = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.70), (int)(ra1.Height * 0.112), (int)(ra1.Width * 0.275), (int)(ra1.Height * 0.50)));

                        ArtifactStat artifact;
                        try
                        {
                            artifact = GetArtifactStat(card.SrcMat, OcrFactory.Paddle, out string allText);
                        }
                        catch (Exception e)
                        {
                            if (recognitionFailurePolicy == RecognitionFailurePolicy.Skip)
                            {
                                logger.LogError("识别失败，跳过当前圣遗物：{msg}", e.Message);

                                itemRegion.Click(); // 反选取消
                                await Delay(100, ct);
                                continue;
                            }
                            else
                            {
                                throw;
                            }
                        }

                        if (await IsMatchJavaScript(artifact, javaScript))
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
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }

    /// <summary>
    /// 是否匹配JavaScript的计算结果
    /// </summary>
    /// <param name="artifact">作为JS入参，JS使用“ArtifactStat”获取</param>
    /// <param name="javaScript"></param>
    /// <param name="cts">为空则默认创建一个3秒延迟的cts</param>
    /// <returns>是否匹配。取JS的“Output”作为出参</returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="Exception"></exception>
    public async static Task<bool> IsMatchJavaScript(ArtifactStat artifact, string javaScript, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        logger = logger ?? App.GetLogger<AutoArtifactSalvageTask>();
        using V8ScriptEngine engine = new V8ScriptEngine(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding | V8ScriptEngineFlags.DisableGlobalMembers);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3), timeProvider ?? TimeProvider.System);    // 这里只是用JS写一个自定义判断方法，由于每个圣遗物都会执行一次，这个方法不应执行太久
        cts.Token.Register(() =>
        {
            try
            {
                engine.Interrupt();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"中断失败: {ex.Message}");
            }
        });
        try
        {
            // 传入输入参数
            engine.Script.ArtifactStat = artifact;

            // 执行JavaScript代码
            await Task.Run(() => engine.Execute(javaScript));

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
        catch (ScriptInterruptedException)
        {
            logger.LogWarning("脚本执行超出3秒限制，请使用正确的JS代码（JavaScript execution timeout!）");
            throw;
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

    public ArtifactStat GetArtifactStat(Mat src, IOcrService ocrService, out string allText)
    {
        using Mat gray = src.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat hatKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 15)/*需根据实际文本大小调整*/);   // 顶帽运算核

        Mat nameRoi = gray.SubMat(new Rect(0, 0, src.Width, (int)(src.Height * 0.106)));
        //Cv2.ImShow("name", nameRoi);
        Mat typeRoi = gray.SubMat(new Rect(0, (int)(src.Height * 0.106), src.Width, (int)(src.Height * 0.106)));
        #region 主词条预处理 去除背景干扰
        Mat mainAffixRoi = gray.SubMat(new Rect(0, (int)(src.Height * 0.22), (int)(src.Width * 0.55), (int)(src.Height * 0.30)));
        using Mat mainAffixRoiBottomHat = mainAffixRoi.MorphologyEx(MorphTypes.TopHat, hatKernel);
        using Mat mainAffixRoiThreshold = mainAffixRoiBottomHat.Threshold(30, 255, ThresholdTypes.Binary);
        //Cv2.ImShow("mainAffix", mainAffixRoiThreshold);
        #endregion
        #region 副词条预处理 还是不处理效果最好……
        Mat levelAndMinorAffixRoi = gray.SubMat(new Rect(0, (int)(src.Height * 0.52), src.Width, (int)(src.Height * 0.48)));
        //using Mat levelAndMinorAffixRoiThreshold = new Mat();
        //double otsu = Cv2.Threshold(levelAndMinorAffixRoi, levelAndMinorAffixRoiThreshold, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        // //using Mat levelAndMinorAffixRoiThreshold = levelAndMinorAffixRoi.Threshold(170, 255, ThresholdTypes.Binary);
        //Cv2.ImShow($"levelAndMinorAffixRoi = {otsu}", levelAndMinorAffixRoiThreshold);
        #endregion
        //Cv2.WaitKey();

        var nameOcrResult = ocrService.OcrResult(nameRoi);
        var typeOcrResult = ocrService.OcrResult(typeRoi);
        var mainAffixOcrResult = ocrService.OcrResult(mainAffixRoiThreshold);
        string mainAffixText = string.Join("\n", mainAffixOcrResult.Regions.Where(r => r.Score > 0.5).OrderBy(r => r.Rect.Center.Y).ThenBy(r => r.Rect.Center.X).Select(r => r.Text));
        var mainAffixLines = mainAffixText.Split('\n');
        var levelAndMinorAffixOcrResult = ocrService.OcrResult(levelAndMinorAffixRoi);
        (string Text, Rect Rect)[] levelAndMinorAffixResult = levelAndMinorAffixOcrResult.Regions.Where(r => r.Score > 0.5)
            .Where(r => r.Rect.BoundingRect().Left < levelAndMinorAffixRoi.Width * 0.1) // 一定是贴着左边的，排除套装效果文字也存在类似+15%的情况
            .OrderBy(r => r.Rect.Center.Y).ThenBy(r => r.Rect.Center.X).Select(r => (r.Text, r.Rect.BoundingRect())).ToArray();
        var levelAndMinorAffixLines = levelAndMinorAffixResult.Select(r => r.Text).ToArray();
        allText = string.Join('\n', new[]
        {
            nameOcrResult.Text,
            typeOcrResult.Text,
            mainAffixText
        }.Concat(levelAndMinorAffixLines));

        string percentStr = "%";

        // 名称
        string name = nameOcrResult.Text;

        #region 主词条
        var defaultMainAffix = this.artifactAffixStrDic.Select(kvp => kvp.Value).Distinct();
        string mainAffixTypeLine = mainAffixLines.SingleOrDefault(l => defaultMainAffix.Contains(l)) ?? throw new Exception($"未找到主词条对应的行：\n{mainAffixText}");
        ArtifactAffixType mainAffixType = this.artifactAffixStrDic.First(kvp => kvp.Value == mainAffixTypeLine).Key;
        string mainAffixValueLine = mainAffixLines.Select(l =>
        {
            string pattern = @"^([\d., ]*)(%?)$";
            pattern = pattern.Replace("%", percentStr);   // 这样一行一行写只是为了IDE能保持正则字符串高亮
            Match match = Regex.Match(l, pattern);
            if (match.Success)
            {
                if (mainAffixType == ArtifactAffixType.ATK && !String.IsNullOrEmpty(match.Groups[2].Value))
                {
                    mainAffixType = ArtifactAffixType.ATKPercent;
                }
                if (mainAffixType == ArtifactAffixType.DEF && !String.IsNullOrEmpty(match.Groups[2].Value))
                {
                    mainAffixType = ArtifactAffixType.DEFPercent;
                }
                if (mainAffixType == ArtifactAffixType.HP && !String.IsNullOrEmpty(match.Groups[2].Value))
                {
                    mainAffixType = ArtifactAffixType.HPPercent;
                }
                return match.Groups[1].Value;
            }
            else
            {
                return null;
            }
        }).Where(l => l != null).Cast<string>().SingleOrDefault() ?? throw new Exception($"未找到主词条数值对应的行：\n{mainAffixText}");
        if (!float.TryParse(mainAffixValueLine, NumberStyles.Any, this.cultureInfo, out float value))
        {
            throw new Exception($"未识别的主词条数值：{mainAffixValueLine}");
        }
        ArtifactAffix mainAffix = new ArtifactAffix(mainAffixType, value);
        #endregion

        #region 副词条
        var minorAffixes = new List<ArtifactAffix>();
        string pattern = @"^([^+:：]+)\+([\d., ]*)(%?).*$";
        pattern = pattern.Replace("%", percentStr);
        foreach (var r in levelAndMinorAffixResult)
        {
            Match match = Regex.Match(r.Text, pattern);
            if (!match.Success)
            {
                continue;
            }
            ArtifactAffixType artifactAffixType;
            var dic = this.artifactAffixStrDic;
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

            if (!float.TryParse(match.Groups[2].Value, NumberStyles.Any, cultureInfo, out float affixValue))
            {
                throw new Exception($"未识别的副词条数值：{match.Groups[2].Value}");
            }

            bool isUnactivated = false;
            // 只有在已经成功识别至少 3 个词条后才执行额外的直方图分析。
            if (minorAffixes.Count >= 3)
            {
                using var lineRoi = levelAndMinorAffixRoi.SubMat(r.Rect);
                using var lineHistogram = new Mat();
                Cv2.CalcHist(
                    images: [lineRoi],
                    channels: [0],
                    mask: null,
                    hist: lineHistogram,
                    dims: 1,
                    histSize: [256],
                    ranges: [new Rangef(0, 256)]
                );
                lineHistogram.GetArray(out float[] histogramFrequencies);
                // 检查背景和前景像素是否符合未激活的特征。
                const int backgroundIntensity = 222;
                const int foregroundIntensity = 152;
                var backgroundFrequency = histogramFrequencies[backgroundIntensity];
                var foregroundFrequency = histogramFrequencies[foregroundIntensity];
                var noiseFrequencyUpperBound = Math.Min(backgroundFrequency, foregroundFrequency);
                // 检查这两个强度是否比所有其他强度更常见
                isUnactivated = backgroundFrequency > 0 &&
                                foregroundFrequency > 0 &&
                                backgroundFrequency > foregroundFrequency &&
                                !histogramFrequencies
                                    .Where((frequency, intensity) =>
                                        intensity != backgroundIntensity &&
                                        intensity != foregroundIntensity &&
                                        frequency > noiseFrequencyUpperBound)
                                    .Any();
            }
            minorAffixes.Add(new ArtifactAffix(artifactAffixType, affixValue, isUnactivated));
        }
        #endregion

        #region 等级
        string levelLine = levelAndMinorAffixLines.Select(l =>
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
        }).Where(l => l != null).Cast<string>().SingleOrDefault() ?? throw new Exception($"未找到等级对应的行：\n{levelAndMinorAffixLines}");
        if (!int.TryParse(levelLine, out int level) || level < 0 || level > 20)
        {
            throw new Exception($"未识别的等级：{levelLine}");
        }
        #endregion

        return new ArtifactStat(name, mainAffix, minorAffixes.ToArray(), level);
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