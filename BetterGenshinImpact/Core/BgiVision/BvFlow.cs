using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.BgiVision;

public sealed class BvFlow
{
    private readonly BvPage _page;
    private readonly BvFlowServices _services;
    private readonly List<BvFlowStep> _steps = [];
    private readonly object _syncRoot = new();
    private bool _hasSteps;
    private bool _hasStarted;
    private int _isRunning;

    internal int DefaultTimeout { get; private set; }
    internal int DefaultRetryInterval { get; private set; }

    internal BvFlow(BvPage page, int defaultTimeout, int defaultRetryInterval)
        : this(page, defaultTimeout, defaultRetryInterval, BvFlowServices.Create(page))
    {
    }

    internal BvFlow(BvPage page, int defaultTimeout, int defaultRetryInterval, BvFlowServices services)
    {
        _page = page;
        _services = services;
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

    public BvFlowAction Do(dynamic action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CreateAction("Do", async _ =>
        {
            object? result;
            try
            {
                result = action is Delegate callback ? callback.DynamicInvoke() : action();
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            if (result is Task task)
            {
                await task;
            }
        });
    }

    public BvFlowAction KeyPress(string key)
    {
        var virtualKey = User32Helper.ToVk(key);
        return CreateAction($"KeyPress({key})", _ =>
        {
            _services.KeyPress(virtualKey);
            return Task.CompletedTask;
        });
    }

    public BvFlowAction Click()
    {
        return CreateImplicitPointAction("Click(previous)", _services.LeftClick);
    }

    public BvFlowAction Click(double x, double y)
    {
        return CreatePointAction($"Click({x}, {y})", x, y, _services.LeftClick);
    }

    public BvFlowAction RightClick()
    {
        return CreateImplicitPointAction("RightClick(previous)", _services.RightClick);
    }

    public BvFlowAction RightClick(double x, double y)
    {
        return CreatePointAction($"RightClick({x}, {y})", x, y, _services.RightClick);
    }

    public BvFlowAction MiddleClick()
    {
        return CreateImplicitPointAction("MiddleClick(previous)", _services.MiddleClick);
    }

    public BvFlowAction MiddleClick(double x, double y)
    {
        return CreatePointAction($"MiddleClick({x}, {y})", x, y, _services.MiddleClick);
    }

    public BvFlowAction MoveTo()
    {
        return CreateImplicitPointAction("MoveTo(previous)", _services.MoveTo);
    }

    public BvFlowAction MoveTo(double x, double y)
    {
        return CreatePointAction($"MoveTo({x}, {y})", x, y, _services.MoveTo);
    }

    public BvFlowAction Drag(double fromX, double fromY, double toX, double toY, int duration = 300)
    {
        ValidatePositive(duration, nameof(duration));
        return CreateAction($"Drag({fromX}, {fromY}, {toX}, {toY})",
            _ => _services.Drag(fromX, fromY, toX, toY, duration));
    }

    public BvFlowAction DragTo(double toX, double toY, int duration = 300)
    {
        ValidatePositive(duration, nameof(duration));
        return CreateAction($"DragTo({toX}, {toY})", context =>
        {
            var (fromX, fromY) = context.GetLastMatchCenter();
            return _services.Drag(fromX, fromY, toX, toY, duration);
        });
    }

    public BvFlowAction DragFrom(double fromX, double fromY, int duration = 300)
    {
        ValidatePositive(duration, nameof(duration));
        return CreateAction($"DragFrom({fromX}, {fromY})", context =>
        {
            var (toX, toY) = context.GetLastMatchCenter();
            return _services.Drag(fromX, fromY, toX, toY, duration);
        });
    }

    public BvFlow WaitUntilText(string text, Rect rect = default, int? timeout = null, int? retryInterval = null)
    {
        return WaitUntil(CreateTextLocator(text, rect), timeout, retryInterval);
    }

    public BvFlow WaitUntilAnyText(
        object texts,
        Rect rect = default,
        int? timeout = null,
        int? retryInterval = null)
    {
        return WaitUntil(CreateAnyTextLocator(texts, rect), timeout, retryInterval);
    }

    public BvFlow WaitUntil(BvLocator target, int? timeout = null, int? retryInterval = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        return AddWaitStep([target.Clone()], BvFlowCondition.AnyAppear, timeout, retryInterval);
    }

    public BvFlow WaitUntilAny(object targets, int? timeout = null, int? retryInterval = null)
    {
        return AddWaitStep(ParseTargets(targets, nameof(targets)), BvFlowCondition.AnyAppear,
            timeout, retryInterval);
    }

    public BvFlow WaitUntilDisappear(BvLocator target, int? timeout = null, int? retryInterval = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        return AddWaitStep([target.Clone()], BvFlowCondition.AllDisappear, timeout, retryInterval);
    }

    public BvFlow WaitUntilAllDisappear(object targets, int? timeout = null, int? retryInterval = null)
    {
        return AddWaitStep(ParseTargets(targets, nameof(targets)), BvFlowCondition.AllDisappear,
            timeout, retryInterval);
    }

    public BvFlow Wait(int milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "milliseconds 不能小于 0");
        }

        return AddStep($"Wait({milliseconds})", _ => _services.Delay(milliseconds));
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

            var context = new BvFlowExecutionContext();
            for (var i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                try
                {
                    _services.ThrowIfCancellationRequested();
                    await step.Action(context);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (NormalEndException)
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

    internal BvFlow AddActionStep(BvFlowActionSnapshot snapshot)
    {
        return AddStep(snapshot.Description, context => ExecuteActionStep(snapshot, context));
    }

    internal BvFlow AddOnceActionStep(string description, Func<BvFlowExecutionContext, Task> action)
    {
        return AddStep(description, action);
    }

    internal BvFlowAction CreateAction(string description, Func<BvFlowExecutionContext, Task> action)
    {
        lock (_syncRoot)
        {
            if (_hasStarted)
            {
                throw new InvalidOperationException("BvFlow 已经开始执行，不能再添加步骤");
            }
        }

        return new BvFlowAction(this, description, action);
    }

    internal BvLocator CreateTextLocator(string text, Rect rect)
    {
        return _page.Locator(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = rect,
            Text = text
        });
    }

    internal BvLocator CreateAnyTextLocator(object texts, Rect rect)
    {
        return _page.GetByAnyText(texts, rect);
    }

    internal static IReadOnlyList<BvLocator> ParseTargets(object targets, string paramName)
    {
        return BvPage.ParseCollection<BvLocator>(targets, paramName)
            .Select(target => target.Clone())
            .ToArray();
    }

    internal static int ValidatePositive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} 必须大于 0");
        }

        return value;
    }

    private BvFlowAction CreateImplicitPointAction(string description, Action<double, double> action)
    {
        return CreateAction(description, context =>
        {
            var (x, y) = context.GetLastMatchCenter();
            action(x, y);
            return Task.CompletedTask;
        });
    }

    private BvFlowAction CreatePointAction(string description, double x, double y, Action<double, double> action)
    {
        return CreateAction(description, _ =>
        {
            action(x, y);
            return Task.CompletedTask;
        });
    }

    private async Task ExecuteActionStep(BvFlowActionSnapshot snapshot, BvFlowExecutionContext context)
    {
        var startedAt = _services.GetTimestamp();
        var attempts = 0;

        while (true)
        {
            _services.ThrowIfCancellationRequested();
            attempts++;
            await snapshot.Action(context);

            var remainingMilliseconds = snapshot.Timeout - _services.GetElapsedMilliseconds(startedAt);
            if (remainingMilliseconds <= 0)
            {
                throw new TimeoutException(
                    $"动作 {snapshot.Description} 执行 {attempts} 次后，等待 {snapshot.TargetDescription} 超时（{snapshot.Timeout}ms）");
            }

            await _services.Delay(GetDelayMilliseconds(snapshot.RetryInterval, remainingMilliseconds));

            if (_services.GetElapsedMilliseconds(startedAt) >= snapshot.Timeout)
            {
                throw new TimeoutException(
                    $"动作 {snapshot.Description} 执行 {attempts} 次后，等待 {snapshot.TargetDescription} 超时（{snapshot.Timeout}ms）");
            }

            var result = FindTargets(snapshot.Targets, snapshot.Condition);
            _services.ThrowIfCancellationRequested();
            if (_services.GetElapsedMilliseconds(startedAt) >= snapshot.Timeout)
            {
                throw new TimeoutException(
                    $"动作 {snapshot.Description} 执行 {attempts} 次后，等待 {snapshot.TargetDescription} 超时（{snapshot.Timeout}ms）");
            }

            if (result.Succeeded)
            {
                context.LastMatchRect = result.Match is null
                    ? null
                    : _services.GetMatchRect(result.Match);
                return;
            }
        }
    }

    private BvFlow AddWaitStep(
        IReadOnlyList<BvLocator> targets,
        BvFlowCondition condition,
        int? timeout,
        int? retryInterval)
    {
        var actualTimeout = timeout is { } timeoutValue
            ? ValidatePositive(timeoutValue, nameof(timeout))
            : DefaultTimeout;
        var actualRetryInterval = retryInterval is { } retryIntervalValue
            ? ValidatePositive(retryIntervalValue, nameof(retryInterval))
            : DefaultRetryInterval;
        var targetDescription = BvFlowAction.DescribeTargets(targets, condition);

        return AddStep($"WaitUntil {targetDescription}",
            context => ExecuteWaitStep(targets, targetDescription, condition,
                actualTimeout, actualRetryInterval, context));
    }

    private async Task ExecuteWaitStep(
        IReadOnlyList<BvLocator> targets,
        string targetDescription,
        BvFlowCondition condition,
        int timeout,
        int retryInterval,
        BvFlowExecutionContext context)
    {
        var startedAt = _services.GetTimestamp();

        while (true)
        {
            _services.ThrowIfCancellationRequested();
            var elapsedMilliseconds = _services.GetElapsedMilliseconds(startedAt);
            if (elapsedMilliseconds >= timeout)
            {
                throw new TimeoutException($"等待 {targetDescription} 超时（{timeout}ms）");
            }

            var result = FindTargets(targets, condition);
            _services.ThrowIfCancellationRequested();
            if (_services.GetElapsedMilliseconds(startedAt) >= timeout)
            {
                throw new TimeoutException($"等待 {targetDescription} 超时（{timeout}ms）");
            }

            if (result.Succeeded)
            {
                context.LastMatchRect = result.Match is null
                    ? null
                    : _services.GetMatchRect(result.Match);
                return;
            }

            var remainingMilliseconds = timeout - _services.GetElapsedMilliseconds(startedAt);
            if (remainingMilliseconds <= 0)
            {
                throw new TimeoutException($"等待 {targetDescription} 超时（{timeout}ms）");
            }

            await _services.Delay(GetDelayMilliseconds(retryInterval, remainingMilliseconds));
        }
    }

    private BvFlowConditionResult FindTargets(
        IReadOnlyList<BvLocator> targets,
        BvFlowCondition condition)
    {
        return _services.FindTargets(targets, condition);
    }

    private static int GetDelayMilliseconds(int retryInterval, double remainingMilliseconds)
    {
        return Math.Min(retryInterval, Math.Max(1, (int)Math.Ceiling(remainingMilliseconds)));
    }

    private BvFlow AddStep(string description, Func<BvFlowExecutionContext, Task> action)
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

    private sealed record BvFlowStep(string Description, Func<BvFlowExecutionContext, Task> Action);

}

internal sealed record BvFlowConditionResult(bool Succeeded, Region? Match);

internal sealed class BvFlowExecutionContext
{
    public Rect? LastMatchRect { get; set; }

    public (double X, double Y) GetLastMatchCenter()
    {
        if (LastMatchRect is not { } rect)
        {
            throw new InvalidOperationException("没有可用的上一步识别位置，无法执行隐式坐标操作");
        }

        return (rect.X + rect.Width / 2d, rect.Y + rect.Height / 2d);
    }
}

internal sealed class BvFlowServices
{
    public required Action ThrowIfCancellationRequested { get; set; }
    public required Func<IReadOnlyList<BvLocator>, BvFlowCondition, BvFlowConditionResult> FindTargets { get; set; }
    public required Func<Region, Rect> GetMatchRect { get; set; }
    public required Func<int, Task> Delay { get; set; }
    public required Func<long> GetTimestamp { get; set; }
    public required Func<long, double> GetElapsedMilliseconds { get; set; }
    public required Action<Vanara.PInvoke.User32.VK> KeyPress { get; set; }
    public required Action<double, double> LeftClick { get; set; }
    public required Action<double, double> RightClick { get; set; }
    public required Action<double, double> MiddleClick { get; set; }
    public required Action<double, double> MoveTo { get; set; }
    public required Func<double, double, double, double, int, Task> Drag { get; set; }

    public static BvFlowServices Create(BvPage page)
    {
        return new BvFlowServices
        {
            ThrowIfCancellationRequested = page.ThrowIfCancellationRequested,
            FindTargets = (targets, condition) =>
            {
                using var screen = page.Screenshot();
                return EvaluateTargets(targets, condition, target => target.FindAll(screen));
            },
            GetMatchRect = region =>
            {
                var rect = region.ConvertPositionToGameCaptureRegion(0, 0, region.Width, region.Height);
                var scale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
                return new Rect(
                    (int)Math.Round(rect.X / scale),
                    (int)Math.Round(rect.Y / scale),
                    (int)Math.Round(rect.Width / scale),
                    (int)Math.Round(rect.Height / scale));
            },
            Delay = async milliseconds => await page.Wait(milliseconds),
            GetTimestamp = System.Diagnostics.Stopwatch.GetTimestamp,
            GetElapsedMilliseconds = startedAt => System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            KeyPress = key => Simulation.SendInput.Keyboard.KeyPress(key),
            LeftClick = GameCaptureRegion.GameRegion1080PPosClick,
            RightClick = (x, y) =>
            {
                GameCaptureRegion.GameRegion1080PPosMove(x, y);
                Simulation.SendInput.Mouse.RightButtonClick();
            },
            MiddleClick = (x, y) =>
            {
                GameCaptureRegion.GameRegion1080PPosMove(x, y);
                Simulation.SendInput.Mouse.MiddleButtonClick();
            },
            MoveTo = GameCaptureRegion.GameRegion1080PPosMove,
            Drag = async (fromX, fromY, toX, toY, duration) =>
            {
                GameCaptureRegion.GameRegion1080PPosMove(fromX, fromY);
                Simulation.SendInput.Mouse.LeftButtonDown();
                try
                {
                    var firstDelay = duration / 2;
                    await page.Wait(firstDelay);
                    GameCaptureRegion.GameRegion1080PPosMove(toX, toY);
                    await page.Wait(duration - firstDelay);
                }
                finally
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                }
            }
        };
    }

    internal static BvFlowConditionResult EvaluateTargets(
        IReadOnlyList<BvLocator> targets,
        BvFlowCondition condition,
        Func<BvLocator, List<Region>> findAll)
    {
        foreach (var target in targets)
        {
            var matches = findAll(target);
            if (condition == BvFlowCondition.AnyAppear && matches.Count > 0)
            {
                return new BvFlowConditionResult(true, matches[0]);
            }

            if (condition == BvFlowCondition.AllDisappear && matches.Count > 0)
            {
                return new BvFlowConditionResult(false, null);
            }
        }

        return condition == BvFlowCondition.AllDisappear
            ? new BvFlowConditionResult(true, null)
            : new BvFlowConditionResult(false, null);
    }
}
