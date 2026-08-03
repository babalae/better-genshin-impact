using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.BgiVision;

public sealed class BvFlow
{
    private readonly BvPage _page;
    private readonly List<BvFlowStep> _steps = [];
    private readonly object _syncRoot = new();
    private bool _hasSteps;
    private bool _hasStarted;
    private int _isRunning;

    internal int DefaultTimeout { get; private set; }
    internal int DefaultRetryInterval { get; private set; }

    internal BvFlow(BvPage page, int defaultTimeout, int defaultRetryInterval)
    {
        _page = page;
        DefaultTimeout = ValidatePositive(defaultTimeout, nameof(defaultTimeout));
        DefaultRetryInterval = ValidatePositive(defaultRetryInterval, nameof(defaultRetryInterval));
    }

    public BvFlow WithDefaultTimeout(int milliseconds)
    {
        var timeout = ValidatePositive(milliseconds, nameof(milliseconds));
        lock (_syncRoot)
        {
            EnsureDefaultsCanChange();
            DefaultTimeout = timeout;
        }
        return this;
    }

    public BvFlow WithDefaultRetryInterval(int milliseconds)
    {
        var retryInterval = ValidatePositive(milliseconds, nameof(milliseconds));
        lock (_syncRoot)
        {
            EnsureDefaultsCanChange();
            DefaultRetryInterval = retryInterval;
        }
        return this;
    }

    public BvFlowLocator GetByText(string text = "", Rect rect = default)
    {
        return Locator(_page.GetByText(text, rect));
    }

    public BvFlowLocator GetByImage(BvImage image)
    {
        return Locator(_page.GetByImage(image));
    }

    public BvFlowLocator Locator(RecognitionObject recognitionObject)
    {
        return Locator(_page.Locator(recognitionObject));
    }

    public BvFlowLocator Locator(BvLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return new BvFlowLocator(this, locator);
    }

    public BvFlow Wait(int milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "milliseconds 不能小于 0");
        }

        return AddStep($"Wait({milliseconds})", () => _page.Wait(milliseconds));
    }

    public async Task<BvPage> Run()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            throw new InvalidOperationException("同一个 BvFlow 不能并发执行");
        }

        try
        {
            BvFlowStep[] steps;
            lock (_syncRoot)
            {
                _hasStarted = true;
                steps = _steps.ToArray();
            }

            for (var i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                try
                {
                    await step.Action();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"BvFlow 第 {i + 1} 步执行失败：{step.Description}", ex);
                }
            }

            return _page;
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }

    internal BvFlow AddStep(string description, Func<Task> action)
    {
        lock (_syncRoot)
        {
            if (_hasStarted)
            {
                throw new InvalidOperationException("BvFlow 已经开始执行，不能再添加步骤");
            }

            _hasSteps = true;
            _steps.Add(new BvFlowStep(description, action));
        }

        return this;
    }

    private void EnsureDefaultsCanChange()
    {
        if (_hasSteps)
        {
            throw new InvalidOperationException("添加流程步骤后不能修改流程默认配置");
        }
    }

    private static int ValidatePositive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} 必须大于 0");
        }

        return value;
    }

    private sealed record BvFlowStep(string Description, Func<Task> Action);
}
