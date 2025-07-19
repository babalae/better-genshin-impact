using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
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

    public RecognitionObject RecognitionObject { get; }

    public Action? RetryAction { get; set; }

    public static int DefaultTimeout { get; set; } = 10000;

    public static int DefaultRetryInterval { get; set; } = 250;

    public BvLocator(RecognitionObject recognitionObject, CancellationToken cancellationToken)
    {
        RecognitionObject = recognitionObject;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// 根据传入的位置信息定位元素
    /// 不建议外部调用使用
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public List<Region> FindAll()
    {
        using var screen = CaptureToRectArea();

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
            if (!string.IsNullOrEmpty(RecognitionObject.Text))
            {
                return results.FindAll(r => r.Text.Contains(RecognitionObject.Text));
            }

            return results;
        }
        else
        {
            throw new NotSupportedException($"不被 Locator 支持的识别类型: {RecognitionObject.RecognitionType}");
        }
    }

    public bool IsExist()
    {
        return FindAll().Count > 0;
    }

    public async Task<Region> Click(int? timeout = null)
    {
        return (await WaitFor(timeout)).First().Click();
    }
    
    public async Task<Region> DoubleClick(int? timeout = null)
    {
        var list = await WaitFor(timeout);
        return list.First().DoubleClick();
    }

    public async Task<List<Region>> WaitFor(int? timeout = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var retryCount = actualTimeout / DefaultRetryInterval;

        List<Region> results = [];
        var retryRes = await NewRetry.WaitForAction(() =>
        {
            RetryAction?.Invoke();
            results = FindAll();
            return results.Count > 0;
        }, _cancellationToken, retryCount, DefaultRetryInterval);

        if (retryRes)
        {
            return results;
        }
        else
        {
            throw new TimeoutException($"识别元素在 {actualTimeout}ms 后超时未出现！");
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
        var actualTimeout = timeout ?? DefaultTimeout;
        var retryCount = actualTimeout / DefaultRetryInterval;

        var retryRes = await NewRetry.WaitForAction(() =>
        {
            RetryAction?.Invoke();
            var results = FindAll();
            return results.Count == 0;
        }, _cancellationToken, retryCount, DefaultRetryInterval);

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

    public BvLocator WithRetryAction(Action? action)
    {
        RetryAction = action;
        return this;
    }
}