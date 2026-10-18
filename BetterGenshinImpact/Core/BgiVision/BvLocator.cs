using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;


namespace BetterGenshinImpact.Core.BgiVision;

/// <summary>
/// 针对 Region 体系的包装
/// </summary>
public class BvLocator
{
    private static readonly ILogger Logger = App.GetLogger<BvLocator>();
    private readonly CancellationToken _cancellationToken;
    private readonly IReadOnlyList<string> _anyTexts;
    private int? _timeout;
    private int? _retryInterval;

    /// <summary>
    /// 定位器绑定的运行环境（由创建它的 BvPage 传入），截图、等待与 ROI 换算都基于它
    /// </summary>
    private readonly GameRuntime _runtime;

    public RecognitionObject RecognitionObject { get; }

    public Func<List<Region>, Task>? RetryAction { get; set; }

    public static int DefaultTimeout { get; set; } = 10000;

    public static int DefaultRetryInterval { get; set; } = 250;

    /// <summary>
    /// 创建绑定到指定运行环境的定位器
    /// </summary>
    /// <param name="runtime">运行环境</param>
    /// <param name="recognitionObject">识别对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    public BvLocator(GameRuntime runtime, RecognitionObject recognitionObject, CancellationToken cancellationToken)
        : this(runtime, recognitionObject, cancellationToken, [])
    {
    }

    /// <summary>
    /// 创建绑定到指定运行环境、带候选文本的定位器
    /// </summary>
    /// <param name="runtime">运行环境</param>
    /// <param name="recognitionObject">识别对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="anyTexts">OCR 候选文本，命中任意一个即视为找到</param>
    internal BvLocator(
        GameRuntime runtime,
        RecognitionObject recognitionObject,
        CancellationToken cancellationToken,
        IReadOnlyList<string> anyTexts)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        RecognitionObject = recognitionObject.Clone();
        _cancellationToken = cancellationToken;
        _anyTexts = anyTexts.ToArray();
    }

    internal IReadOnlyList<string> AnyTexts => _anyTexts;

    internal BvLocator Clone()
    {
        return new BvLocator(_runtime, RecognitionObject, _cancellationToken, _anyTexts);
    }

    /// <summary>
    /// 根据传入的位置信息定位元素
    /// 不建议外部调用使用
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public List<Region> FindAll()
    {
        using var screen = _runtime.CaptureToRectArea();
        return FindAll(screen);
    }

    internal List<Region> FindAll(ImageRegion screen)
    {
        if (RecognitionObject.RecognitionType == RecognitionTypes.TemplateMatch)
        {
            var region = screen.Find(RecognitionObject);
            if (region.IsExist())
            {
                return [region];
            }

            return [];
        }
        else if (RecognitionObject.RecognitionType == RecognitionTypes.Ocr)
        {
            var results = screen.FindMulti(RecognitionObject);
            return FilterOcrResults(results, _anyTexts, RecognitionObject.Text);
        }
        else
        {
            throw new NotSupportedException($"不被 Locator 支持的识别类型: {RecognitionObject.RecognitionType}");
        }
    }

    internal static List<Region> FilterOcrResults(
        List<Region> results,
        IReadOnlyList<string> anyTexts,
        string text)
    {
        if (anyTexts.Count > 0)
        {
            return results.FindAll(region =>
                anyTexts.Any(candidate => region.Text.Contains(candidate, StringComparison.Ordinal)));
        }

        return string.IsNullOrEmpty(text)
            ? results
            : results.FindAll(region => region.Text.Contains(text, StringComparison.Ordinal));
    }

    public bool IsExist()
    {
        return FindAll().Count > 0;
    }

    public async Task<Region> Click(int? timeout = null)
    {
        return (await WaitFor(timeout)).First().Click();
    }

    public async Task<Region> ClickUntilDisappears(int? timeout = null)
    {
        var region = (await WaitFor(timeout)).First().Click();
        await Clone()
            .WithRetryAction(resList => { resList.First().Click(); }).WaitForDisappear();
        return region;
    }

    public async Task<Region> DoubleClick(int? timeout = null)
    {
        var list = await WaitFor(timeout);
        return list.First().DoubleClick();
    }

    public async Task<List<Region>> WaitFor(int? timeout = null)
    {
        var actualTimeout = timeout ?? _timeout ?? DefaultTimeout;
        var actualRetryInterval = _retryInterval ?? DefaultRetryInterval;
        var retryCount = Math.Max(1, actualTimeout / actualRetryInterval);

        List<Region> results = [];
        var retryRes = await NewRetry.WaitForAction(_runtime, async () =>
        {
            results = FindAll();
            var b = results.Count > 0;
            if (!b && RetryAction != null)
            {
                await RetryAction(results);
            }
            return b;
        }, _cancellationToken, retryCount, actualRetryInterval);

        if (retryRes)
        {
            return results;
        }
        else
        {
            throw BuildTimeoutException(actualTimeout);
        }
    }

    private TimeoutException BuildTimeoutException(int actualTimeout)
    {
        if (RecognitionObject.RecognitionType == RecognitionTypes.Ocr)
        {
            if (_anyTexts.Count > 0)
            {
                return new TimeoutException(
                    $"识别任意文字[{string.Join('|', _anyTexts)}]在 {actualTimeout}ms 后超时未出现！");
            }

            return new TimeoutException($"识别文字[{RecognitionObject.Text}]在 {actualTimeout}ms 后超时未出现！");
        }
        else if (RecognitionObject.RecognitionType == RecognitionTypes.TemplateMatch)
        {
            return new TimeoutException($"识别图像[{RecognitionObject.Name}]在 {actualTimeout}ms 后超时未出现！");
        }
        else
        {
            return new TimeoutException($"识别元素在 {actualTimeout}ms 后超时未出现！");
        }
    }

    public async Task<List<Region>> TryWaitFor(int? timeout = null)
    {
        try
        {
            return await WaitFor(timeout);
        }
        catch
        {
            return [];
        }
    }

    public async Task WaitForDisappear(int? timeout = null)
    {
        var actualTimeout = timeout ?? _timeout ?? DefaultTimeout;
        var actualRetryInterval = _retryInterval ?? DefaultRetryInterval;
        var retryCount = Math.Max(1, actualTimeout / actualRetryInterval);

        var retryRes = await NewRetry.WaitForAction(_runtime, async () =>
        {
            var results = FindAll();
            var b = results.Count == 0;
            if (!b && RetryAction != null)
            {
                await RetryAction(results);
            }

            return b;
        }, _cancellationToken, retryCount, actualRetryInterval);

        if (!retryRes)
        {
            throw new TimeoutException($"识别元素在 {actualTimeout}ms 后超时未消失！");
        }
    }

    public async Task TryWaitForDisappear(int? timeout = null)
    {
        try
        {
            await WaitForDisappear(timeout);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// 方便优雅的设置感兴趣区域 (ROI)
    /// 该方法会覆盖 RecognitionObject.RegionOfInterest 的值。
    /// </summary>
    /// <param name="rect"></param>
    /// <returns></returns>
    public BvLocator WithRoi(Rect rect)
    {
        RecognitionObject.RegionOfInterest = rect;
        return this;
    }

    public BvLocator WithRoi(Func<Rect, Rect> deltaFunc)
    {
        // 使用创建方 BvPage 传入的运行环境的 SystemInfo 换算 1080P 下的截图区域
        var captureAreaRect = _runtime.SystemInfo.ScaleMax1080PCaptureRect;
        var rect = deltaFunc(captureAreaRect);
        RecognitionObject.RegionOfInterest = rect;
        return this;
    }

    public BvLocator WithRetryAction(Action<List<Region>>? action)
    {
        if (action == null)
        {
            RetryAction = null;
        }
        else
        {
            RetryAction = (results) =>
            {
                action(results);
                return Task.CompletedTask;
            };
        }
        return this;
    }

    /// <summary>
    /// 设置超时时间（毫秒）
    /// </summary>
    /// <param name="timeout">超时时间（毫秒）</param>
    /// <returns></returns>
    public BvLocator WithTimeout(int timeout)
    {
        if (timeout <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "timeout 必须大于 0");
        }
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// 设置重试间隔（毫秒）
    /// </summary>
    /// <param name="retryInterval">重试间隔（毫秒）</param>
    /// <returns></returns>
    public BvLocator WithRetryInterval(int retryInterval)
    {
        if (retryInterval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryInterval), "retryInterval 必须大于 0");
        }
        _retryInterval = retryInterval;
        return this;
    }

    /// <summary>
    /// 为 JavaScript 提供的动态参数重载
    /// 解决 ClearScript 无法将 JS 函数隐式转换为 Action 委托的问题
    /// 支持同步和异步 JS 函数
    /// </summary>
    /// <param name="action">JS 回调函数</param>
    /// <returns></returns>
    public BvLocator WithRetryAction(dynamic action)
    {
        if (action == null)
        {
            RetryAction = null;
        }
        else
        {
            RetryAction = async (results) =>
            {
                var taskResult = action(results);
                // 如果 JS 返回的是 Promise/Task，等待其完成
                if (taskResult is Task task)
                {
                    await task;
                }
            };
        }
        return this;
    }

}
