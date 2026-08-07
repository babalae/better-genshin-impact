using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.BgiVision;

public sealed class BvFlowAction
{
    private readonly BvFlow _flow;
    private readonly string _description;
    private readonly Func<BvFlowExecutionContext, Task> _action;
    private readonly object _syncRoot = new();
    private int? _timeout;
    private int? _retryInterval;
    private bool _completed;

    internal BvFlowAction(BvFlow flow, string description, Func<BvFlowExecutionContext, Task> action)
    {
        _flow = flow;
        _description = description;
        _action = action;
    }

    public BvFlowAction WithTimeout(int milliseconds)
    {
        var timeout = BvFlow.ValidatePositive(milliseconds, nameof(milliseconds));
        lock (_syncRoot)
        {
            EnsureNotCompleted();
            _timeout = timeout;
        }
        return this;
    }

    public BvFlowAction WithRetryInterval(int milliseconds)
    {
        var retryInterval = BvFlow.ValidatePositive(milliseconds, nameof(milliseconds));
        lock (_syncRoot)
        {
            EnsureNotCompleted();
            _retryInterval = retryInterval;
        }
        return this;
    }

    public BvFlowAction Do(dynamic action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CompleteOnce().Do(action);
    }

    public BvFlowAction KeyPress(string key)
    {
        _ = User32Helper.ToVk(key);
        return CompleteOnce().KeyPress(key);
    }

    public BvFlowAction Click()
    {
        return CompleteOnce().Click();
    }

    public BvFlowAction Click(double x, double y)
    {
        return CompleteOnce().Click(x, y);
    }

    public BvFlowAction RightClick()
    {
        return CompleteOnce().RightClick();
    }

    public BvFlowAction RightClick(double x, double y)
    {
        return CompleteOnce().RightClick(x, y);
    }

    public BvFlowAction MiddleClick()
    {
        return CompleteOnce().MiddleClick();
    }

    public BvFlowAction MiddleClick(double x, double y)
    {
        return CompleteOnce().MiddleClick(x, y);
    }

    public BvFlowAction MoveTo()
    {
        return CompleteOnce().MoveTo();
    }

    public BvFlowAction MoveTo(double x, double y)
    {
        return CompleteOnce().MoveTo(x, y);
    }

    public BvFlowAction Drag(double fromX, double fromY, double toX, double toY, int duration = 300)
    {
        BvFlow.ValidatePositive(duration, nameof(duration));
        return CompleteOnce().Drag(fromX, fromY, toX, toY, duration);
    }

    public BvFlowAction DragTo(double toX, double toY, int duration = 300)
    {
        BvFlow.ValidatePositive(duration, nameof(duration));
        return CompleteOnce().DragTo(toX, toY, duration);
    }

    public BvFlowAction DragFrom(double fromX, double fromY, int duration = 300)
    {
        BvFlow.ValidatePositive(duration, nameof(duration));
        return CompleteOnce().DragFrom(fromX, fromY, duration);
    }

    public BvFlow WaitUntilText(string text, Rect rect = default, int? timeout = null, int? retryInterval = null)
    {
        ValidateWaitOptions(timeout, retryInterval);
        return CompleteOnce().WaitUntilText(text, rect, timeout, retryInterval);
    }

    public BvFlow WaitUntilAnyText(
        object texts,
        Rect rect = default,
        int? timeout = null,
        int? retryInterval = null)
    {
        _ = _flow.CreateAnyTextLocator(texts, rect);
        ValidateWaitOptions(timeout, retryInterval);
        return CompleteOnce().WaitUntilAnyText(texts, rect, timeout, retryInterval);
    }

    public BvFlow WaitUntil(BvLocator target, int? timeout = null, int? retryInterval = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateWaitOptions(timeout, retryInterval);
        return CompleteOnce().WaitUntil(target, timeout, retryInterval);
    }

    public BvFlow WaitUntilAny(object targets, int? timeout = null, int? retryInterval = null)
    {
        _ = BvFlow.ParseTargets(targets, nameof(targets));
        ValidateWaitOptions(timeout, retryInterval);
        return CompleteOnce().WaitUntilAny(targets, timeout, retryInterval);
    }

    public BvFlow WaitUntilDisappear(BvLocator target, int? timeout = null, int? retryInterval = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateWaitOptions(timeout, retryInterval);
        return CompleteOnce().WaitUntilDisappear(target, timeout, retryInterval);
    }

    public BvFlow WaitUntilAllDisappear(object targets, int? timeout = null, int? retryInterval = null)
    {
        _ = BvFlow.ParseTargets(targets, nameof(targets));
        ValidateWaitOptions(timeout, retryInterval);
        return CompleteOnce().WaitUntilAllDisappear(targets, timeout, retryInterval);
    }

    public BvFlow Wait(int milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "milliseconds 不能小于 0");
        }

        return CompleteOnce().Wait(milliseconds);
    }

    public BvFlow UntilText(string text, Rect rect = default)
    {
        return Until(_flow.CreateTextLocator(text, rect));
    }

    public BvFlow UntilAnyText(object texts, Rect rect = default)
    {
        return Until(_flow.CreateAnyTextLocator(texts, rect));
    }

    public BvFlow Until(BvLocator target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Complete([target.Clone()], BvFlowCondition.AnyAppear);
    }

    public BvFlow UntilAny(object targets)
    {
        return Complete(BvFlow.ParseTargets(targets, nameof(targets)), BvFlowCondition.AnyAppear);
    }

    public BvFlow UntilDisappear(BvLocator target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Complete([target.Clone()], BvFlowCondition.AllDisappear);
    }

    public BvFlow UntilAllDisappear(object targets)
    {
        return Complete(BvFlow.ParseTargets(targets, nameof(targets)), BvFlowCondition.AllDisappear);
    }

    public Task<BvPage> Run()
    {
        return CompleteOnce().Run();
    }

    private BvFlow CompleteOnce()
    {
        lock (_syncRoot)
        {
            EnsureNotCompleted();
            if (_timeout is not null || _retryInterval is not null)
            {
                throw new InvalidOperationException(
                    "一次性动作不支持 WithTimeout 或 WithRetryInterval，请使用 Until 系列方法设置重试条件");
            }

            _completed = true;
        }

        return _flow.AddOnceActionStep(_description, _action);
    }

    private BvFlow Complete(IReadOnlyList<BvLocator> targets, BvFlowCondition condition)
    {
        int timeout;
        int retryInterval;
        lock (_syncRoot)
        {
            EnsureNotCompleted();
            _completed = true;
            timeout = _timeout ?? _flow.DefaultTimeout;
            retryInterval = _retryInterval ?? _flow.DefaultRetryInterval;
        }

        var targetDescription = DescribeTargets(targets, condition);
        return _flow.AddActionStep(new BvFlowActionSnapshot(
            _description,
            _action,
            targets,
            targetDescription,
            timeout,
            retryInterval,
            condition));
    }

    private void EnsureNotCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException("当前动作已经设置完成条件，不能再次修改或添加");
        }
    }

    private static void ValidateWaitOptions(int? timeout, int? retryInterval)
    {
        if (timeout is { } timeoutValue)
        {
            BvFlow.ValidatePositive(timeoutValue, nameof(timeout));
        }

        if (retryInterval is { } retryIntervalValue)
        {
            BvFlow.ValidatePositive(retryIntervalValue, nameof(retryInterval));
        }
    }

    internal static string DescribeTargets(IReadOnlyList<BvLocator> targets, BvFlowCondition condition)
    {
        var descriptions = targets.Select(DescribeTarget);
        var targetDescription = targets.Count == 1
            ? descriptions.First()
            : $"目标[{string.Join(" | ", descriptions)}]";
        if (condition == BvFlowCondition.AllDisappear)
        {
            return targets.Count == 1
                ? $"{targetDescription}消失"
                : $"{targetDescription}全部消失";
        }

        return targets.Count == 1 && targets[0].AnyTexts.Count == 0
            ? $"{targetDescription}出现"
            : $"{targetDescription}中的任意一个出现";
    }

    private static string DescribeTarget(BvLocator locator)
    {
        if (locator.AnyTexts.Count > 0)
        {
            return $"文字[{string.Join('|', locator.AnyTexts)}]";
        }

        return locator.RecognitionObject.RecognitionType switch
        {
            RecognitionTypes.Ocr => $"文字[{locator.RecognitionObject.Text}]",
            RecognitionTypes.TemplateMatch => $"图像[{locator.RecognitionObject.Name}]",
            _ => "识别元素"
        };
    }
}

internal enum BvFlowCondition
{
    AnyAppear,
    AllDisappear
}

internal sealed record BvFlowActionSnapshot(
    string Description,
    Func<BvFlowExecutionContext, Task> Action,
    IReadOnlyList<BvLocator> Targets,
    string TargetDescription,
    int Timeout,
    int RetryInterval,
    BvFlowCondition Condition);
