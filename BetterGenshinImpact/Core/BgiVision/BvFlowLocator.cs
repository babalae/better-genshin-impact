using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.BgiVision;

public sealed class BvFlowLocator
{
    private readonly BvFlow _flow;
    private readonly BvLocator _locator;
    private int? _timeout;
    private int? _retryInterval;
    private Func<List<Region>, Task>? _retryAction;

    internal BvFlowLocator(BvFlow flow, BvLocator locator)
    {
        _flow = flow;
        _locator = locator;
    }

    public BvFlowLocator WithRoi(Rect rect)
    {
        _locator.WithRoi(rect);
        return this;
    }

    public BvFlowLocator WithRoi(Func<Rect, Rect> deltaFunc)
    {
        _locator.WithRoi(deltaFunc);
        return this;
    }

    public BvFlowLocator WithTimeout(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "milliseconds 必须大于 0");
        }

        _timeout = milliseconds;
        return this;
    }

    public BvFlowLocator WithRetryInterval(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "milliseconds 必须大于 0");
        }

        _retryInterval = milliseconds;
        return this;
    }

    public BvFlowLocator WithRetryAction(Action<List<Region>>? action)
    {
        _retryAction = action == null
            ? null
            : results =>
            {
                action(results);
                return Task.CompletedTask;
            };
        return this;
    }

    public BvFlowLocator WithRetryAction(dynamic action)
    {
        if (action == null)
        {
            _retryAction = null;
        }
        else
        {
            _retryAction = async results =>
            {
                var result = action(results);
                if (result is Task task)
                {
                    await task;
                }
            };
        }

        return this;
    }

    public BvFlowLocator WithRetryLeftClick(double x, double y)
    {
        return SetRetryAction(() => GameCaptureRegion.GameRegion1080PPosClick(x, y));
    }

    public BvFlowLocator WithRetryRightClick(double x, double y)
    {
        return SetRetryAction(() =>
        {
            GameCaptureRegion.GameRegion1080PPosMove(x, y);
            Simulation.SendInput.Mouse.RightButtonClick();
        });
    }

    public BvFlowLocator WithRetryMiddleClick(double x, double y)
    {
        return SetRetryAction(() =>
        {
            GameCaptureRegion.GameRegion1080PPosMove(x, y);
            Simulation.SendInput.Mouse.MiddleButtonClick();
        });
    }

    public BvFlowLocator WithRetryKeyPress(string key)
    {
        var virtualKey = User32Helper.ToVk(key);
        return SetRetryAction(() => Simulation.SendInput.Keyboard.KeyPress(virtualKey));
    }

    public BvFlow WaitFor()
    {
        return AddLocatorStep("WaitFor", async locator => await locator.WaitFor());
    }

    public BvFlow WaitDisappear()
    {
        return AddLocatorStep("WaitDisappear", async locator => await locator.WaitForDisappear());
    }

    public BvFlow Click()
    {
        return AddLocatorStep("Click", async locator => await locator.Click());
    }

    public BvFlow DoubleClick()
    {
        return AddLocatorStep("DoubleClick", async locator => await locator.DoubleClick());
    }

    public BvFlow ClickUntilDisappears()
    {
        return AddLocatorStep("ClickUntilDisappears", async locator => await locator.ClickUntilDisappears());
    }

    public BvFlow ClickUntil(BvLocator target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _flow.AddStep(Describe("ClickUntil"), async () =>
        {
            Configure(target);
            target.WithRetryAction(_ =>
            {
                var sourceRegions = _locator.FindAll();
                if (sourceRegions.Count > 0)
                {
                    sourceRegions[0].Click();
                }
            });
            await target.WaitFor();
        });
    }

    private BvFlow AddLocatorStep(string operation, Func<BvLocator, Task> action)
    {
        return _flow.AddStep(Describe(operation), async () =>
        {
            Configure(_locator);
            await action(_locator);
        });
    }

    private void Configure(BvLocator locator)
    {
        locator.WithTimeout(_timeout ?? _flow.DefaultTimeout)
            .WithRetryInterval(_retryInterval ?? _flow.DefaultRetryInterval);

        if (_retryAction == null)
        {
            locator.WithRetryAction((Action<List<Region>>?)null);
        }
        else
        {
            locator.RetryAction = _retryAction;
        }
    }

    private BvFlowLocator SetRetryAction(Action action)
    {
        _retryAction = _ =>
        {
            action();
            return Task.CompletedTask;
        };
        return this;
    }

    private string Describe(string operation)
    {
        var recognitionObject = _locator.RecognitionObject;
        var target = recognitionObject.RecognitionType switch
        {
            RecognitionTypes.Ocr => $"文字[{recognitionObject.Text}]",
            RecognitionTypes.TemplateMatch => $"图像[{recognitionObject.Name}]",
            _ => "识别元素"
        };
        return $"{operation} {target}";
    }
}
