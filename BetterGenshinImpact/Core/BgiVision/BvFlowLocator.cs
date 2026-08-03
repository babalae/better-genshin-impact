using System;
using System.Collections.Generic;
using System.Linq;
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

    public BvFlowLocator WithRetryAction(Func<List<Region>, Task>? action)
    {
        _retryAction = action;
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
        var snapshot = CreateSnapshot();
        return _flow.AddStep(Describe("ClickUntilDisappears"), async () =>
        {
            Configure(snapshot);
            var locator = snapshot.Locator;
            (await locator.WaitFor()).First().Click();
            locator.WithRetryAction((Action<List<Region>>)(results => results.First().Click()));
            await locator.WaitForDisappear();
        });
    }

    public BvFlow ClickUntil(BvLocator target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var sourceSnapshot = CreateSnapshot();
        var targetSnapshot = CreateSnapshot(target);
        return _flow.AddStep(Describe("ClickUntil"), async () =>
        {
            Configure(targetSnapshot);
            targetSnapshot.Locator.RetryAction = CreateClickUntilRetryAction(
                sourceSnapshot.Locator.FindAll,
                sourceSnapshot.RetryAction);
            await targetSnapshot.Locator.WaitFor();
        });
    }

    private BvFlow AddLocatorStep(string operation, Func<BvLocator, Task> action)
    {
        var snapshot = CreateSnapshot();
        return _flow.AddStep(Describe(operation), async () =>
        {
            Configure(snapshot);
            await action(snapshot.Locator);
        });
    }

    internal BvFlowLocatorSnapshot CreateSnapshot(BvLocator? locator = null)
    {
        return new BvFlowLocatorSnapshot(
            (locator ?? _locator).Clone(),
            _timeout ?? _flow.DefaultTimeout,
            _retryInterval ?? _flow.DefaultRetryInterval,
            _retryAction);
    }

    internal static Func<List<Region>, Task> CreateClickUntilRetryAction(
        Func<List<Region>> findSource,
        Func<List<Region>, Task>? sourceRetryAction)
    {
        return async _ =>
        {
            var sourceRegions = findSource();
            if (sourceRegions.Count > 0)
            {
                sourceRegions.First().Click();
            }
            else if (sourceRetryAction != null)
            {
                await sourceRetryAction(sourceRegions);
            }
        };
    }

    private static void Configure(BvFlowLocatorSnapshot snapshot)
    {
        snapshot.Locator.WithTimeout(snapshot.Timeout)
            .WithRetryInterval(snapshot.RetryInterval);

        if (snapshot.RetryAction == null)
        {
            snapshot.Locator.WithRetryAction((Action<List<Region>>?)null);
        }
        else
        {
            snapshot.Locator.RetryAction = snapshot.RetryAction;
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

    internal sealed record BvFlowLocatorSnapshot(
        BvLocator Locator,
        int Timeout,
        int RetryInterval,
        Func<List<Region>, Task>? RetryAction);

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
