using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoEat;

/// <summary>
/// 自动吃药任务
/// 检测红血自动使用便携营养袋
/// </summary>
public class AutoEatTask : BaseIndependentTask, ISoloTask<int?>
{
    public string Name => "自动吃药";

    private readonly AutoEatParam _taskParam;
    private readonly AutoEatConfig _config;
    private readonly ILogger _logger = App.GetLogger<AutoEatTask>();
    private readonly InputSimulator _input = Simulation.SendInput;
    private CancellationToken _ct;

    public AutoEatTask(AutoEatParam taskParam)
    {
        _taskParam = taskParam;
        _config = TaskContext.Instance().Config.AutoEatConfig;
    }

    async Task ISoloTask.Start(CancellationToken ct)
    {
        await Start(ct);
    }

    public async Task<int?> Start(CancellationToken ct)
    {
        _ct = ct;

        Init();
        _logger.LogInformation("自动吃药任务启动");

        if (String.IsNullOrWhiteSpace(_taskParam.FoodName))
        {
            if (!IsTakeFood())
            {
                _logger.LogWarning("未装备 \"{Tool}\"，无法启用自动吃药功能", "便携营养袋");
                return null;
            }

            try
            {
                await AutoEatLoop();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "自动吃药任务发生异常");
                throw;
            }
            finally
            {
                _logger.LogInformation("自动吃药任务结束");
            }

            return null;
        }
        else
        {
            _logger.LogInformation("打开背包寻找{name}……", _taskParam.FoodName);
            await new ReturnMainUiTask().Start(ct);
            await AutoArtifactSalvageTask.OpenInventory(GridScreenName.Food, _input, _logger, _ct);

            using InferenceSession session = GridIconsAccuracyTestTask.LoadModel(out Dictionary<string, float[]> prototypes);

            GridScreen gridScreen = new GridScreen(GridParams.Templates[GridScreenName.Food], _logger, _ct);
            gridScreen.OnAfterTurnToNewPage += GridScreen.DrawItemsAfterTurnToNewPage;
            gridScreen.OnBeforeScroll += () => VisionContext.Instance().DrawContent.ClearAll();
            int? count = null;
            try
            {
                await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
                {
                    using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
                    using Mat icon = itemRegion.SrcMat.GetGridIcon();
                    var result = GridIconsAccuracyTestTask.Infer(icon, session, prototypes);
                    string predName = result.Item1;
                    if (predName == _taskParam.FoodName)
                    {
                        // 点击item
                        itemRegion.Click();

                        #region 识别数量
                        string ocrText = itemRegion.SrcMat.GetGridItemIconText(OcrFactory.Paddle);
                        string numStr = StringUtils.ConvertFullWidthNumToHalfWidth(ocrText);
                        if (int.TryParse(numStr, out int num))
                        {
                            count = num - 1;    // 算上吃掉的1个
                        }
                        else
                        {
                            count = -2;
                            _logger.LogWarning("无法识别食物数量：{text}，依然尝试使用", numStr);
                        }
                        #endregion

                        await Delay(300, ct);
                        // 点击确定
                        using var ra0 = CaptureToRectArea();
                        using var ra = ra0.Find(ElementAssets.Instance.BtnWhiteConfirm);
                        if (ra.IsExist())
                        {
                            ra.Click();
                        }
                        _logger.LogInformation("吃了一份{name}，真香！", predName);
                        break;
                    }
                }
            }
            finally
            {
                VisionContext.Instance().DrawContent.ClearAll();
            }
            if (count == null)
            {
                count = -1;
                _logger.LogInformation("没有找到{name}", _taskParam.FoodName);
            }
            await new ReturnMainUiTask().Start(ct);

            return count;
        }
    }

    private void Init()
    {
        _logger.LogInformation("→ {Text} 检测间隔: {Interval}ms", "自动吃药，", _config.CheckInterval);
        _logger.LogInformation("→ {Text} 吃药间隔: {Interval}ms", "自动吃药，", _config.EatInterval);
    }

    /// <summary>
    /// 自动吃药主循环
    /// </summary>
    private async Task AutoEatLoop()
    {
        var lastEatTime = DateTime.MinValue;

        while (!_ct.IsCancellationRequested)
        {
            try
            {
                // 检测当前角色是否红血
                if (Bv.CurrentAvatarIsLowHp(CaptureToRectArea()))
                {
                    var now = DateTime.Now;
                    // 检查是否超过吃药间隔时间，避免重复吃药
                    if ((now - lastEatTime).TotalMilliseconds >= _config.EatInterval)
                    {
                        // 模拟按键 "Z" 使用便携营养袋
                        Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                        lastEatTime = now;

                        _logger.LogInformation("检测到红血，自动吃药");
                    }
                }

                await Delay(_config.CheckInterval, _ct);
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，正常退出
                break;
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "自动吃药检测时发生异常");
                await Delay(1000, _ct); // 异常时稍作等待
            }
        }
    }

    /// <summary>
    /// 检测是否装备了便携营养袋
    /// </summary>
    private bool IsTakeFood()
    {
        try
        {
            // 获取图像
            using var ra = CaptureToRectArea();
            // 识别道具图标下是否是数字
            var s = TaskContext.Instance().SystemInfo.AssetScale;
            var countArea = ra.DeriveCrop(1800 * s, 845 * s, 40 * s, 20 * s);
            var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.CacheGreyMat);
            return int.TryParse(count, out _);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "检测便携营养袋时发生异常");
            return false;
        }
    }
}