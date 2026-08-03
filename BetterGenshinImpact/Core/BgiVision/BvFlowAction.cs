using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.BgiVision;

public sealed class BvFlowAction
{
    private readonly BvFlow _flow;
    private readonly string _description;
    private readonly Func<BvFlowExecutionContext, Task> _action;
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
        EnsureNotCompleted();
        _timeout = BvFlow.ValidatePositive(milliseconds, nameof(milliseconds));
        return this;
    }

    public BvFlowAction WithRetryInterval(int milliseconds)
    {
        EnsureNotCompleted();
        _retryInterval = BvFlow.ValidatePositive(milliseconds, nameof(milliseconds));
        return this;
    }

    public BvFlow UntilText(string text, Rect rect = default)
    {
        return Until(_flow.CreateTextLocator(text, rect));
    }

    public BvFlow Until(BvLocator target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Complete(target.Clone(), false);
    }

    public BvFlow UntilDisappear(BvLocator target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Complete(target.Clone(), true);
    }

    private BvFlow Complete(BvLocator target, bool waitForDisappear)
    {
        EnsureNotCompleted();
        _completed = true;

        var targetDescription = DescribeTarget(target.RecognitionObject, waitForDisappear);
        return _flow.AddActionStep(new BvFlowActionSnapshot(
            _description,
            _action,
            target,
            targetDescription,
            _timeout ?? _flow.DefaultTimeout,
            _retryInterval ?? _flow.DefaultRetryInterval,
            waitForDisappear));
    }

    private void EnsureNotCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException("当前动作已经设置完成条件，不能再次修改或添加");
        }
    }

    internal static string DescribeTarget(RecognitionObject recognitionObject, bool waitForDisappear)
    {
        var target = recognitionObject.RecognitionType switch
        {
            RecognitionTypes.Ocr => $"文字[{recognitionObject.Text}]",
            RecognitionTypes.TemplateMatch => $"图像[{recognitionObject.Name}]",
            _ => "识别元素"
        };
        return waitForDisappear ? $"{target}消失" : $"{target}出现";
    }
}

internal sealed record BvFlowActionSnapshot(
    string Description,
    Func<BvFlowExecutionContext, Task> Action,
    BvLocator Target,
    string TargetDescription,
    int Timeout,
    int RetryInterval,
    bool WaitForDisappear);
