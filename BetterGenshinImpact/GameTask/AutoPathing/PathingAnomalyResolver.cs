using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 自动寻路异常解析器。
/// 负责在自动寻路过程中处理突发界面弹窗、强制剧情及其他阻断性交互。
/// </summary>
public class PathingAnomalyResolver
{
    private readonly CancellationToken _ct;
    private readonly Func<ImageRegion> _captureAction;
    private readonly Func<bool> _isAutoSkipEnabled;
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();
    
    private AutoSkipTrigger? _autoSkipTrigger;

    // 常量定义，替换原有的魔法数字
    private const int UiCloseDelayMilliseconds = 1000;
    private const int AutoSkipPollingIntervalMilliseconds = 210;
    private const int MaxContinuousMissingUiTokens = 10;

    /// <summary>
    /// 初始化 <see cref="PathingAnomalyResolver"/> 的新实例。
    /// </summary>
    /// <param name="ct">用于控制异步任务取消的令牌。</param>
    /// <param name="captureAction">用于获取当前游戏画面区域的委托方法。</param>
    /// <param name="isAutoSkipEnabled">用于判断当前是否启用了自动跳过剧情功能的委托方法。</param>
    public PathingAnomalyResolver(CancellationToken ct, Func<ImageRegion> captureAction, Func<bool> isAutoSkipEnabled)
    {
        _ct = ct;
        _captureAction = captureAction;
        _isAutoSkipEnabled = isAutoSkipEnabled;
    }

    /// <summary>
    /// 解析并处理当前画面中的异常状态（如意外打开的菜单、烹饪界面或剧情过场）。
    /// </summary>
    /// <param name="imageRegion">需要解析的图像区域。如果为 null，则将调用注入的捕获委托获取。</param>
    /// <returns>表示异步操作的任务对象。</returns>
    public async Task ResolveAnomalies(ImageRegion? imageRegion = null)
    {
        imageRegion ??= _captureAction();

        // 优化图像匹配性能：使用短路逻辑 (Short-Circuit) 替代全量匹配，一旦识别到目标界面即停止查找后续界面
        // 避免不必要的图像运算资源浪费。
        bool hasBlockingUi = 
            imageRegion.Find(AutoSkipAssets.Instance.CookRo).IsExist() ||
            imageRegion.Find(AutoSkipAssets.Instance.PageCloseMainRo).IsExist() ||
            imageRegion.Find(ElementAssets.Instance.PageCloseWhiteRo).IsExist() ||
            imageRegion.Find(AutoSkipAssets.Instance.PageCloseRo).IsExist();

        if (hasBlockingUi)
        {
            if (!Bv.IsInBigMapUi(imageRegion))
            {
                Logger.LogInformation("检测到其他界面，使用ESC关闭界面");
                Simulation.SendInput.Keyboard.KeyPress(Vanara.PInvoke.User32.VK.VK_ESCAPE);
                await Delay(UiCloseDelayMilliseconds, _ct);
            }
        }

        await _blessingOfTheWelkinMoonTask.Start(_ct);

        if (_isAutoSkipEnabled())
        {
            await AutoSkip();
        }
    }

    /// <summary>
    /// 执行剧情自动跳过逻辑，并持续监控画面直到底部UI按钮消失达到阈值。
    /// </summary>
    /// <returns>表示异步操作的任务。</returns>
    private async Task AutoSkip()
    {
        var captureRegion = _captureAction();
        var disabledUiButtonRegion = captureRegion.Find(AutoSkipAssets.Instance.DisabledUiButtonRo);
        
        if (!disabledUiButtonRegion.IsExist())
        {
            return;
        }

        Logger.LogWarning("进入剧情，自动点击剧情直到结束");

        if (_autoSkipTrigger == null)
        {
            _autoSkipTrigger = new AutoSkipTrigger(new AutoSkipConfig
            {
                Enabled = true,
                QuicklySkipConversationsEnabled = true,
                ClosePopupPagedEnabled = true,
                ClickChatOption = "优先选择最后一个选项"
            });
            _autoSkipTrigger.Init();
        }

        int missingUiTokenCount = 0;
        
        while (true)
        {
            captureRegion = _captureAction();
            disabledUiButtonRegion = captureRegion.Find(AutoSkipAssets.Instance.DisabledUiButtonRo);
            
            if (disabledUiButtonRegion.IsExist())
            {
                _autoSkipTrigger.OnCapture(new CaptureContent(captureRegion));
                missingUiTokenCount = 0; // 重置丢失计数
            }
            else
            {
                missingUiTokenCount++;
                if (missingUiTokenCount > MaxContinuousMissingUiTokens)
                {
                    Logger.LogInformation("自动剧情结束");
                    break;
                }
            }

            await Delay(AutoSkipPollingIntervalMilliseconds, _ct);
        }
    }
}
