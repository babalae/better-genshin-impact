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
/// A domain-specific anomaly resolver that identifies and resolves sudden interruptions during auto-pathing.
/// 自动寻路异常解析器。负责在自动寻路过程中处理突发界面弹窗、强制剧情及其他阻断性交互。
/// </summary>
public class PathingAnomalyResolver
{
    private readonly CancellationToken _ct;
    private readonly Func<ImageRegion> _captureAction;
    private readonly Func<bool> _isAutoSkipEnabled;
    private readonly BlessingOfTheWelkinMoonTask _blessingOfTheWelkinMoonTask = new();
    private readonly ReturnMainUiTask _returnMainUiTask = new();
    
    private AutoSkipTrigger? _autoSkipTrigger;
    private const int AutoSkipPollingIntervalMilliseconds = 210;
    private const int MaxContinuousMissingUiTokens = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathingAnomalyResolver"/> class.
    /// 初始化 <see cref="PathingAnomalyResolver"/> 的新实例。
    /// </summary>
    /// <param name="ct">The cancellation token. 用于控制异步任务取消的令牌。</param>
    /// <param name="captureAction">A delegate to retrieve current viewport region. 用于获取当前游戏画面区域的委托方法。</param>
    /// <param name="isAutoSkipEnabled">A delegate determining if auto-skip is permitted. 用于判断当前是否启用了自动跳过剧情功能的委托方法。</param>
    /// <exception cref="ArgumentNullException">Thrown when delegates are null. 当委托为 null 时抛出异常。</exception>
    public PathingAnomalyResolver(CancellationToken ct, Func<ImageRegion> captureAction, Func<bool> isAutoSkipEnabled)
    {
        _ct = ct;
        _captureAction = captureAction ?? throw new ArgumentNullException(nameof(captureAction));
        _isAutoSkipEnabled = isAutoSkipEnabled ?? throw new ArgumentNullException(nameof(isAutoSkipEnabled));
    }

    /// <summary>
    /// Analyzes the visual buffer to mitigate out-of-band obstructions (e.g. forced dialogues, UI overwriting).
    /// 解析并处理当前画面中的异常状态（如意外打开的菜单、烹饪界面或剧情过场）。
    /// </summary>
    /// <param name="imageRegion">The visual state to inspect. Needs to be recaptured if null. 需要解析的图像区域。如果为 null 则动态获取。</param>
    /// <returns>An awaitable <see cref="Task"/> representing completion. 表示异步操作的任务对象。</returns>
    public async Task ResolveAnomalies(ImageRegion? imageRegion = null)
    {
        bool ownImageRegion = imageRegion == null;
        imageRegion ??= _captureAction();
        if (imageRegion == null) return;

        try
        {
            using var cookRegion = imageRegion.Find(AutoSkipAssets.Instance.CookRo);
            using var mainRegion = imageRegion.Find(AutoSkipAssets.Instance.PageCloseMainRo);
            using var whiteRegion = imageRegion.Find(ElementAssets.Instance.PageCloseWhiteRo);
            using var closeRegion = imageRegion.Find(AutoSkipAssets.Instance.PageCloseRo);

            bool hasBlockingUi = 
                cookRegion.IsExist() ||
                mainRegion.IsExist() ||
                whiteRegion.IsExist() ||
                closeRegion.IsExist();

            if (hasBlockingUi)
            {
                if (!Bv.IsInBigMapUi(imageRegion))
                {
                    await _returnMainUiTask.Start(_ct);
                }
            }

            await _blessingOfTheWelkinMoonTask.Start(_ct).ConfigureAwait(false);

            if (_isAutoSkipEnabled())
            {
                await AutoSkip().ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownImageRegion)
            {
                imageRegion?.Dispose();
            }
        }
    }

    /// <summary>
    /// Sequentially exhausts story dialogues by polling until spatial context unlocks.
    /// 执行剧情自动跳过逻辑，并持续监控画面直到底部UI按钮消失达到阈值。
    /// </summary>
    private async Task AutoSkip()
    {
        using (var initialCaptureRegion = _captureAction())
        {
            if (initialCaptureRegion == null) return;

            using var initialDisabledUiButtonRegion = initialCaptureRegion.Find(AutoSkipAssets.Instance.DisabledUiButtonRo);
            if (!initialDisabledUiButtonRegion.IsExist())
            {
                return;
            }
        }

        Logger?.LogWarning("进入剧情，自动点击剧情直到结束");

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
        
        while (!_ct.IsCancellationRequested)
        {
            using var captureRegion = _captureAction();
            if (captureRegion == null) break;

            using var disabledUiButtonRegion = captureRegion.Find(AutoSkipAssets.Instance.DisabledUiButtonRo);
            
            if (disabledUiButtonRegion.IsExist())
            {
                _autoSkipTrigger.OnCapture(new CaptureContent(captureRegion));
                missingUiTokenCount = 0; 
            }
            else
            {
                missingUiTokenCount++;
                if (missingUiTokenCount > MaxContinuousMissingUiTokens)
                {
                    Logger?.LogInformation("自动剧情结束");
                    break;
                }
            }

            await Delay(AutoSkipPollingIntervalMilliseconds, _ct).ConfigureAwait(false);
        }
    }
}
