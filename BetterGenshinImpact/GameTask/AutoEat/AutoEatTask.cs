using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Helpers;
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
public class AutoEatTask : BaseIndependentTask, ISoloTask<AutoEatParam, AutoEatData>
{
    /// <summary>
    /// 本次运行使用的运行环境，截图、等待与遮罩清理都只使用它
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 本次运行的任务参数，在 <see cref="RunAsync"/> 开头赋值
    /// </summary>
    private AutoEatParam _taskParam = null!;

    private readonly AutoEatConfig _config;
    private readonly ILogger _logger = App.GetLogger<AutoEatTask>();
    private IInputChannel _input => InputHub.Foreground;
    private CancellationToken _ct;

    /// <summary>
    /// 创建自动吃药任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public AutoEatTask(GameRuntime runtime, AllConfig config) : base(runtime.SystemInfo)
    {
        _runtime = runtime;
        _config = config.AutoEatConfig;
    }

    /// <summary>
    /// 运行自动吃药。未指定食物时持续检测红血并使用便携营养袋，直到取消；
    /// 指定食物时打开背包吃一份，并在结果数据中返回剩余数量。取消时抛出异常
    /// </summary>
    /// <param name="param">任务参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务结果，<see cref="AutoEatData.Count"/> 为原来的返回值</returns>
    public async Task<SoloTaskResult<AutoEatData>> RunAsync(AutoEatParam param, CancellationToken ct)
    {
        _taskParam = param;
        _ct = ct;

        Init();
        _logger.LogInformation("自动吃药任务启动");

        if (String.IsNullOrWhiteSpace(_taskParam.FoodName))
        {
            if (!IsTakeFood())
            {
                _logger.LogWarning("未装备 \"{Tool}\"，无法启用自动吃药功能", "便携营养袋");
                return SoloTaskResult.Failed("gadget_not_equipped", "未装备“便携营养袋”，无法启用自动吃药功能");
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

            // 主循环只会因取消结束，取消时向上抛出
            _ct.ThrowIfCancellationRequested();
            return SoloTaskResult.Unverified(new AutoEatData(), "自动吃药任务结束");
        }
        else
        {
            _logger.LogInformation("打开背包寻找{name}……", _taskParam.FoodName);
            await new ReturnMainUiTask(_runtime).Start(ct);
            await AutoArtifactSalvageTask.OpenInventory(_runtime, GridScreenName.Food, _input, _logger, _ct);

            using IItemIconRecognizer iconRecognizer = ItemIconRecognizerFactory.CreateConfigured();

            GridScreen gridScreen = new GridScreen(_runtime, GridParams.Templates[GridScreenName.Food], _logger, _ct);
            gridScreen.OnAfterTurnToNewPage += GridScreen.DrawItemsAfterTurnToNewPage;
            gridScreen.OnBeforeScroll += () => _runtime.MaskWindowDrawingBoard.ClearAll();
            int? count = null;
            try
            {
                await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
                {
                    using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
                    using Mat icon = itemRegion.SrcMat.GetGridIcon();
                    string? predName = iconRecognizer.Recognize(icon);
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

                        await _runtime.Delay(300, ct);
                        // 点击确定
                        using var ra0 = _runtime.CaptureToRectArea();
                        using var ra = ra0.Find(ElementRecognition.Get("BtnWhiteConfirm", ra0));
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
                _runtime.MaskWindowDrawingBoard.ClearAll();
            }
            if (count == null)
            {
                count = -1;
                _logger.LogInformation("没有找到{name}", _taskParam.FoodName);
            }
            await new ReturnMainUiTask(_runtime).Start(ct);

            var data = new AutoEatData { Count = count };
            if (count == -1)
            {
                // 背包中没有该食物：业务失败，但仍携带原来的返回值 -1
                return new SoloTaskResult<AutoEatData>(SoloTaskResultKind.Failed, $"没有找到{_taskParam.FoodName}",
                    "food_not_found", data);
            }

            // 点击确认后没有再确认是否真的吃下，结果为未确认
            return SoloTaskResult.Unverified(data, $"吃了一份{_taskParam.FoodName}");
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
                using var capture = _runtime.CaptureToRectArea();
                if (Bv.CurrentAvatarIsLowHp(capture))
                {
                    var now = DateTime.Now;
                    // 检查是否超过吃药间隔时间，避免重复吃药
                    if ((now - lastEatTime).TotalMilliseconds >= _config.EatInterval)
                    {
                        // 模拟按键 "Z" 使用便携营养袋
                        InputHub.Foreground.SimulateAction(GIActions.QuickUseGadget);
                        lastEatTime = now;

                        _logger.LogInformation("检测到红血，自动吃药");
                    }
                }

                await _runtime.Delay(_config.CheckInterval, _ct);
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，正常退出
                break;
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "自动吃药检测时发生异常");
                await _runtime.Delay(1000, _ct); // 异常时稍作等待
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
            using var ra = _runtime.CaptureToRectArea();
            // 识别道具图标下是否是数字
            var s = AssetScale;
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
