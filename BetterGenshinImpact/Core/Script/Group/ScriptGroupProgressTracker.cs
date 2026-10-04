using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.ViewModel.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Group;

/// <summary>
/// 配置组进度的展示状态。既是本地显示的取值来源，也是 Worker → Controller 单向回传的载荷。
/// </summary>
public sealed record ScriptGroupProgressState
{
    /// <summary>是否正在执行；false 表示控制端与叠加层都应当隐藏进度</summary>
    public bool Running { get; init; }

    public string GroupName { get; init; } = string.Empty;

    /// <summary>进度百分比 0~100</summary>
    public double Progress { get; init; }

    public string ElapsedText { get; init; } = string.Empty;

    public string RemainingText { get; init; } = string.Empty;

    /// <summary>
    /// 本次执行还有后续配置组时为 true，控制端据此决定是否显示「预计总剩余时间」
    /// </summary>
    public bool HasTotalRemaining { get; init; }

    /// <summary>含后续配置组的预计总剩余时间</summary>
    public string TotalRemainingText { get; init; } = string.Empty;
}

/// <summary>
/// 当前正在执行的配置组的进度与预计剩余时间，用于主窗口和游戏内叠加层展示。
/// 总时长只由地图追踪 JSON 的估算耗时累加，其他类型的脚本不参与估算，
/// 因此不含地图追踪的配置组不会显示进度。
/// 一次执行覆盖多个配置组（连续执行 / 一条龙 / Worker 连续执行）时，
/// 除了当前配置组的预计剩余时间，还会额外给出「预计总剩余时间」。
/// </summary>
public partial class ScriptGroupProgressTracker : ObservableObject
{
    private readonly Stopwatch _stopwatch = new();
    private readonly ScriptGroupProgressPlan _plan = new();

    /// <summary>
    /// 后台秒表线程：不能用 DispatcherTimer。任务的日志与绘制会把 UI 线程占满，
    /// 低优先级的 DispatcherTimer 会被饿死，表现为「倒计时跑一会就不动了」。
    /// </summary>
    private System.Threading.Timer? _ticker;
    private int _refreshQueued;
    private long _token;
    private double _stepStartedAt;
    private bool _batchActive;
    private ScriptGroupProgressState _published = new();

    private static readonly TimeSpan MinTickInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 当前配置组内的进度下限：补时会让本组总预估变大，进度条不能因此回退
    /// </summary>
    private double _progressFloor;

    /// <summary>排刷新用的调度器（没有 WPF 应用时为 null，此时不自动刷新）</summary>
    private Dispatcher? _tickerDispatcher;

    /// <summary>触发一次回血给当前脚本补的基准时长（秒）：吃药 / 换人 / 复活动作本身的开销</summary>
    public const double HealExtraSeconds = 30;

    /// <summary>
    /// 回七天神像回血的补时：基准开销 + 玩家在设置界面设置的「回血等待间隔」。
    /// TpTask 传送到神像后会按该配置等待，所以必须算进补时里。
    /// <para>
    /// 等待时长由调用方从配置读出后传入：本类位于 Core 且被单测直接构造，
    /// 不能触碰 <c>TaskContext.Instance().Config</c>（会连带 ConfigService 静态初始化与主程序启动副作用）。
    /// </para>
    /// </summary>
    public static double GetStatueHealExtraSeconds(double hpRestoreWaitSeconds)
        => HealExtraSeconds + Math.Max(0, hpRestoreWaitSeconds);

    public static ScriptGroupProgressTracker Instance { get; } = new();

    /// <summary>
    /// 单例之外单独建一个实例，仅供测试使用：进度跟踪依赖真实时钟，共享单例会被其它
    /// 触碰到进度（如连接断开时的清空）的代码打乱断言。
    /// </summary>
    internal static ScriptGroupProgressTracker CreateIsolated() => new();

    private static ILogger Logger => App.GetLogger<ScriptGroupProgressTracker>();

    /// <summary>
    /// 展示状态变化。Worker 侧据此把进度回传控制端；值相同的重复状态不会触发。
    /// 无头 Worker 之外没有订阅者。
    /// </summary>
    public event Action<ScriptGroupProgressState>? StateChanged;

    [ObservableProperty] private bool _isRunning;

    [ObservableProperty] private string _groupName = string.Empty;

    /// <summary>
    /// 进度百分比 0~100
    /// </summary>
    [ObservableProperty] private double _progress;

    [ObservableProperty] private string _elapsedText = "0:00";

    [ObservableProperty] private string _remainingText = "0:00";

    /// <summary>
    /// 本次执行是否还有后续配置组（只有多配置组时才显示「预计总剩余时间」）
    /// </summary>
    [ObservableProperty] private bool _hasTotalRemaining;

    [ObservableProperty] private string _totalRemainingText = "0:00";

    /// <summary>
    /// 跟踪一次配置组执行，作用域释放后自动停止跟踪。
    /// 用于 RunMulti 这类方法级作用域，即使抛出异常也能保证进度被隐藏。
    /// 连续执行 / 一条龙 / Worker 连续执行会先 <see cref="BeginBatch"/> 预置整批配置组，
    /// 此时这里只负责把游标移动到当前配置组。
    /// </summary>
    public IDisposable BeginTracking(string groupName, IReadOnlyList<ScriptGroupProgressStep> steps)
    {
        return new TrackingScope(this, Start(groupName, steps));
    }

    /// <summary>
    /// 预置一批即将依次执行的配置组，让预计剩余时间覆盖整批（而不是只算当前这一组）。
    /// 由连续执行 / 一条龙 / Worker 连续执行的循环入口调用，作用域释放时结束这一批。
    /// </summary>
    public IDisposable BeginBatch(IEnumerable<ScriptGroup>? groups)
    {
        var steps = new List<ScriptGroupProgressStep>();
        if (groups != null)
        {
            foreach (var group in groups)
            {
                steps.AddRange(BuildSteps(group.Name, group.Projects));
            }
        }

        return BeginBatchSteps(steps);
    }

    /// <summary>
    /// 用现成的脚本计划开始一批执行。批次是新的执行，必须重新起表，
    /// 否则后续配置组会因为「沿用整批计时」而拿到一个从未启动的秒表，倒计时永远不动。
    /// </summary>
    public IDisposable BeginBatchSteps(IEnumerable<ScriptGroupProgressStep> steps)
    {
        var list = steps.ToList();

        RunOnUiThread(() =>
        {
            _plan.Reset();
            _plan.Append(list);
            _batchActive = true;
            _stopwatch.Restart();
        });

        return new BatchScope(this);
    }

    /// <summary>
    /// 配置组里的第 index 个脚本开始执行。走在前面的脚本（跳过 / 禁用的）会立刻退出预计剩余时间。
    /// </summary>
    public void OnStepStarted(int index)
    {
        RunOnUiThread(() =>
        {
            if (!IsRunning)
            {
                return;
            }

            _plan.StartStep(index);
            _stepStartedAt = ElapsedSeconds;
            Refresh();
        });
    }

    /// <summary>
    /// 触发了回血（队伍回血 / 战斗中被击败复活）：路线往往要重试，
    /// 给当前脚本补一段预估，两个剩余时间同时变长。
    /// </summary>
    /// <param name="extraSeconds">补的秒数，默认是基准开销；回七天神像用 <see cref="GetStatueHealExtraSeconds"/></param>
    public void NotifyHealTriggered(double extraSeconds = HealExtraSeconds)
    {
        AddExtraTime(extraSeconds);
    }

    /// <summary>
    /// 触发了需要重走路线的回血（回七天神像、复苏后重试路线）：当前脚本从头再来，
    /// 因此把当前脚本的计时清零，再补一段回血开销。
    /// 清零会让本组已完成的部分回退，进度条本身由下限保证不回退。
    /// </summary>
    /// <param name="extraSeconds">补的秒数，默认是基准开销；回七天神像用 <see cref="GetStatueHealExtraSeconds"/></param>
    public void NotifyRouteReset(double extraSeconds = HealExtraSeconds)
    {
        RunOnUiThread(() =>
        {
            if (!IsRunning)
            {
                return;
            }

            _stepStartedAt = ElapsedSeconds;
            _plan.AddExtraSecondsToCurrentStep(extraSeconds);
            Refresh();
        });
    }

    /// <summary>
    /// 给当前正在执行的脚本补预估秒数。执行已经结束时忽略，任意线程可调用。
    /// </summary>
    public void AddExtraTime(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (!IsRunning)
            {
                return;
            }

            _plan.AddExtraSecondsToCurrentStep(seconds);
            Refresh();
        });
    }

    /// <summary>
    /// 生成配置组内每个脚本的预估（与 <see cref="ScriptGroupProject.RunNum"/> 折算后的执行列表一一对应）。
    /// </summary>
    public static List<ScriptGroupProgressStep> BuildSteps(string groupName, IEnumerable<ScriptGroupProject>? projects)
    {
        var steps = new List<ScriptGroupProgressStep>();
        if (projects == null)
        {
            return steps;
        }

        foreach (var project in projects)
        {
            if (project == null)
            {
                continue;
            }

            // 禁用的脚本不会执行，预估按 0 计，避免把它的时间算进剩余时间
            var seconds = project.Status == "Disabled"
                ? 0
                : EstimateProjectSeconds(project) * Math.Max(1, project.RunNum);
            steps.Add(new ScriptGroupProgressStep(groupName, project.Name, seconds));
        }

        return steps;
    }

    /// <summary>
    /// 当前配置组还剩多少预估时间，用于开始执行时的日志与通知。
    /// 没有正在跟踪的执行时返回传入的单组预估。
    /// </summary>
    public double GetGroupRemainingEstimate(double fallbackSeconds)
    {
        if (!IsRunning)
        {
            return fallbackSeconds;
        }

        return _plan.Calculate(Math.Max(0, ElapsedSeconds - _stepStartedAt)).GroupRemainingSeconds;
    }

    private double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;

    /// <summary>
    /// 开始跟踪一次配置组执行，返回本次执行的令牌。
    /// 令牌用于嵌套执行（如优先执行配置组）时避免内层结束后误关外层进度。
    /// </summary>
    private long Start(string groupName, IReadOnlyList<ScriptGroupProgressStep> steps)
    {
        var token = Interlocked.Increment(ref _token);
        RunOnUiThread(() =>
        {
            if (token != Interlocked.Read(ref _token))
            {
                return;
            }

            // 已经被停止：不要再为后续配置组亮起面板
            if (CancellationContext.Instance.IsAborted)
            {
                _batchActive = false;
                HideAndReset();
                return;
            }

            // 同一批执行里的下一个配置组：沿用整批的计时与计划
            var continuing = _batchActive && _plan.ContainsPendingGroup(groupName);
            if (!continuing)
            {
                _batchActive = false;
                _plan.Reset();
                _stopwatch.Restart();
            }
            else if (!_stopwatch.IsRunning)
            {
                _stopwatch.Start();
            }

            _plan.StartGroup(groupName, steps);
            _stepStartedAt = ElapsedSeconds;
            _progressFloor = 0;
            GroupName = groupName;

            if (!_plan.HasSteps || _plan.TotalEstimatedSeconds <= 0)
            {
                IsRunning = false;
                GroupName = string.Empty;
                StopTicker();
                PublishState();
                return;
            }

            Refresh();
            IsRunning = true;
            PublishState();
            StartTicker();
        });

        return token;
    }

    /// <summary>
    /// 结束跟踪。同一批执行里还有后续配置组时保持显示（不让进度条在两组之间闪没），
    /// 否则隐藏并清空计划。
    /// </summary>
    private void Stop(long token)
    {
        if (token != Interlocked.Read(ref _token))
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (token != Interlocked.Read(ref _token))
            {
                return;
            }

            _plan.EndGroup();

            if (CancellationContext.Instance.IsAborted)
            {
                // 用户停止 / 任务被取消：立即隐藏。不能沿用「等下一个配置组接上」的显示，
                // 否则剩下的配置组被跳过时面板会一路往前推，界面看起来就是倒计时被快进到 0。
                _batchActive = false;
                HideAndReset();
                return;
            }

            if (_batchActive && _plan.HasPendingSteps)
            {
                // 保持显示并继续走表：下一个配置组紧接着开始，中途停表会留下一个不会再刷新的面板
                Refresh();
                return;
            }

            _batchActive = false;
            HideAndReset();
        });
    }

    /// <summary>这一批已经结束（正常结束或被异常打断）</summary>
    private void EndBatch()
    {
        RunOnUiThread(() =>
        {
            _batchActive = false;
            if (!_plan.HasPendingSteps)
            {
                return;
            }

            // 还有配置组没跑（异常 / 用户中断）：不再有后续配置组接上，直接隐藏
            HideAndReset();
        });
    }

    private void HideAndReset()
    {
        StopTicker();
        _stopwatch.Reset();
        _plan.Reset();
        GroupName = string.Empty;
        Progress = 0;
        _progressFloor = 0;
        ElapsedText = FormatDuration(0);
        RemainingText = FormatDuration(0);
        HasTotalRemaining = false;
        TotalRemainingText = FormatDuration(0);
        IsRunning = false;
        PublishState();
    }

    /// <summary>
    /// 起表。定时器跑在线程池上，只负责把刷新排进 UI 队列：
    /// 这样即使 UI 线程被任务日志/绘制占满，读数也不会停住。
    /// </summary>
    private void StartTicker()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            // 没有 WPF 应用（如单测宿主）：不起定时器，刷新只由各接口调用触发
            return;
        }

        _tickerDispatcher = dispatcher;
        _ticker ??= new System.Threading.Timer(_ => QueueRefresh());
        ScheduleNextTick(MinTickInterval);
    }

    private void StopTicker()
    {
        _tickerDispatcher = null;
        _ticker?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 安排下一次刷新。用一次性定时器而不是固定周期：刷新时刻对齐到「显示值真正变化」的
    /// 那一刻，倒计时才会每秒稳稳地掉 1 秒 —— 固定周期与整秒边界各自跑会错位，
    /// 表现为「等 2 秒没动」或「1 秒掉 2 秒」。
    /// </summary>
    private void ScheduleNextTick(TimeSpan delay) => _ticker?.Change(delay, Timeout.InfiniteTimeSpan);

    private void QueueRefresh()
    {
        var dispatcher = _tickerDispatcher;
        if (dispatcher is null)
        {
            return;
        }

        // 上一帧还没轮到执行时不重复排队，避免 UI 忙时堆积；
        // 已排队的那一帧执行完会自己安排下一次刷新，不会漏拍
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            if (IsRunning)
            {
                Refresh();
            }
        });
    }

    /// <summary>
    /// 距离「显示值下一次变化」还有多久：剩余时间的小数部分走完的那一刻。
    /// 正好是整数秒时下一次变化在 1 秒后；已超时（剩余 0）则尽快刷新，让超时保护补时。
    /// </summary>
    internal static TimeSpan GetDelayUntilDisplayChanges(double remainingSeconds)
    {
        if (remainingSeconds <= 0)
        {
            return MinTickInterval;
        }

        var fraction = remainingSeconds - Math.Floor(remainingSeconds);
        // 落到边界之后再加一点点，避免刚好卡在边界上算出旧值
        var delay = (fraction <= 0.001 ? 1.0 : fraction) + 0.02;
        return TimeSpan.FromSeconds(Math.Clamp(delay, MinTickInterval.TotalSeconds, 1.0));
    }

    private void Refresh()
    {
        var elapsed = ElapsedSeconds;
        var result = _plan.Calculate(Math.Max(0, elapsed - _stepStartedAt));
        // 回血补时会让本组总预估变大，进度只增不减
        var progress = Math.Max(result.Progress, _progressFloor);
        _progressFloor = progress;
        Progress = progress;
        ElapsedText = FormatDuration(elapsed);
        // 倒计时向上取整：60 秒的脚本一开始就显示 1:00，也不会提前跳到 0:00
        RemainingText = FormatDuration(result.GroupRemainingSeconds, roundUp: true);
        HasTotalRemaining = _plan.HasLaterGroups;
        TotalRemainingText = FormatDuration(result.TotalRemainingSeconds, roundUp: true);
        PublishState();
        // 下一次刷新安排在显示值真正变化的时刻，倒计时才会均匀地每秒掉 1 秒
        ScheduleNextTick(GetDelayUntilDisplayChanges(result.GroupRemainingSeconds));
    }

    /// <summary>
    /// 把当前展示状态推给订阅者（无头 Worker 用它回传控制端），状态没变化时不重复推送
    /// </summary>
    private void PublishState()
    {
        var state = IsRunning ? CurrentState : new ScriptGroupProgressState();
        if (state == _published)
        {
            return;
        }

        _published = state;

        try
        {
            StateChanged?.Invoke(state);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "推送配置组进度失败");
        }
    }

    private ScriptGroupProgressState CurrentState => new()
    {
        Running = IsRunning,
        GroupName = GroupName,
        Progress = Progress,
        ElapsedText = ElapsedText,
        RemainingText = RemainingText,
        HasTotalRemaining = HasTotalRemaining,
        TotalRemainingText = TotalRemainingText,
    };

    /// <summary>
    /// 控制端专用：用 Worker 上报的状态刷新显示。只改展示字段，不启动本地计时器。
    /// </summary>
    public void ApplyRemote(ScriptGroupProgressState state)
    {
        RunOnUiThread(() =>
        {
            _published = state;
            GroupName = state.GroupName;
            Progress = state.Progress;
            ElapsedText = state.ElapsedText;
            RemainingText = state.RemainingText;
            HasTotalRemaining = state.HasTotalRemaining;
            TotalRemainingText = state.TotalRemainingText;
            IsRunning = state.Running;
        });
    }

    /// <summary>
    /// 把预估秒数格式化成便于阅读的时长（如 <c>1分27秒</c>），用于日志与通知
    /// </summary>
    public static string FormatEstimatedDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}小时{time.Minutes}分";
        }

        return time.TotalMinutes >= 1 ? $"{time.Minutes}分{time.Seconds}秒" : $"{time.Seconds}秒";
    }

    /// <summary>
    /// 秒数格式化成进度面板用的时长。
    /// <para>
    /// 小时数用总小时数而不是 TimeSpan 的 <c>h</c> 分量：<c>h</c> 会在 24 小时处回绕，
    /// 把 28 小时 35 分显示成 4:35:24，比单个 15 小时的任务还小。
    /// 倒计时向上取整（60 秒的脚本一开始就是 1:00，也不会提前跳到 0:00），已用时间向下取整。
    /// </para>
    /// </summary>
    private static string FormatDuration(double seconds, bool roundUp = false)
    {
        var total = Math.Max(0, roundUp ? Math.Ceiling(seconds) : Math.Floor(seconds));
        var hours = (int)(total / 3600);
        var minutes = (int)(total % 3600 / 60);
        var secs = (int)(total % 60);
        return hours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, secs)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, secs);
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    /// <summary>
    /// 单个脚本的预估耗时，非地图追踪脚本与读取失败的脚本都返回 0
    /// </summary>
    public static double EstimateProjectSeconds(ScriptGroupProject project)
    {
        if (project.Type != "Pathing" || string.IsNullOrWhiteSpace(project.Name))
        {
            return 0;
        }

        var filePath = Path.Combine(MapPathingViewModel.PathJsonPath, project.FolderName, project.Name);
        if (!File.Exists(filePath))
        {
            return 0;
        }

        try
        {
            return PathingTimeEstimator.EstimateSeconds(PathingTask.BuildFromFilePath(filePath));
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "估算地图追踪脚本 {Path} 的耗时失败", filePath);
            return 0;
        }
    }

    private sealed class TrackingScope(ScriptGroupProgressTracker tracker, long token) : IDisposable
    {
        public void Dispose()
        {
            tracker.Stop(token);
        }
    }

    private sealed class BatchScope(ScriptGroupProgressTracker tracker) : IDisposable
    {
        public void Dispose()
        {
            tracker.EndBatch();
        }
    }
}
