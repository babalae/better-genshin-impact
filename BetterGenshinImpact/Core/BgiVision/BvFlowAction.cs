using System;
using System.Collections.Generic;
using System.Linq;
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

    private BvFlow Complete(IReadOnlyList<BvLocator> targets, BvFlowCondition condition)
    {
        EnsureNotCompleted();
        _completed = true;

        var targetDescription = DescribeTargets(targets, condition);
        return _flow.AddActionStep(new BvFlowActionSnapshot(
            _description,
            _action,
            targets,
            targetDescription,
            _timeout ?? _flow.DefaultTimeout,
            _retryInterval ?? _flow.DefaultRetryInterval,
            condition));
    }

    private void EnsureNotCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException("当前动作已经设置完成条件，不能再次修改或添加");
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
