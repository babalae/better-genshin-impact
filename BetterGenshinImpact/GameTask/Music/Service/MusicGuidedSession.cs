using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Music.Model;
using BetterGenshinImpact.GameTask.Runtime;
using Fischless.GameCapture;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Music.Service;

/// <summary>
/// 引导式跟弹的可视化覆盖层。View 层实现（WPF 置顶窗口），
/// 服务层只依赖这个抽象，方便以后换实现或做无界面测试。
/// </summary>
public interface IMusicGuidedOverlay
{
    /// <summary>覆盖层是否已经可以画面</summary>
    bool IsAttached { get; }

    /// <summary>绑定到游戏窗口，并显示覆盖层。句柄为 0 时返回 false</summary>
    bool Attach(nint gameHandle);

    /// <summary>覆盖层画布尺寸（= 游戏画面尺寸，单位：画面像素）</summary>
    (double Width, double Height) CanvasSize { get; }

    /// <summary>隐藏覆盖层</summary>
    void UpdateDetach();

    /// <summary>刷新要显示的段落。index 为段序号（0 起），total 为总段数；
    /// satisfiedPressIndices 为本段内已经弹完的按键序号（段内序号，允许不连续）；
    /// presses 里的每一个元素都是「一次按键」（同时按下的和弦算一次）。</summary>
    void UpdateSegment(int index, int total, IReadOnlyList<MusicGuidedPress> presses,
        IReadOnlyDictionary<char, GuidedKeyPosition> positions, string trackName,
        IReadOnlyList<int> satisfiedPressIndices);

    /// <summary>本段进度变化。satisfiedPressIndices 为本段内已弹完的按键序号，
    /// heldKeys 为当前按住（但这次按键还没按全）的键。</summary>
    void UpdateProgress(IReadOnlyList<int> satisfiedPressIndices, IReadOnlyList<char> heldKeys);

    /// <summary>玩家在覆盖层上要求退出（例如按了 Esc 或点了右上角的 ✕）</summary>
    event Action? ExitRequested;

    /// <summary>玩家按错了：当前段被清空重来</summary>
    void UpdateError(string wrongKeys);

    /// <summary>催促玩家按顺序弹：expectedKeys 是当前该弹的那一次按键（如 "E + T"）。
    /// 用于「段内必须从左往右」时提前按了后面的键——不算按错、不清段，只提示一下。</summary>
    void UpdateOrderHint(string expectedKeys);

    /// <summary>整首曲子弹完了</summary>
    void UpdateCompleted(string trackName);

    /// <summary>显示一行键位诊断说明（自动识别的结果 / 失败原因 / 分辨率信息）</summary>
    void UpdateDetection(string text);

    /// <summary>会话被停止</summary>
    void UpdateStopped(string reason);
}

/// <summary>
/// 引导式跟弹服务：监听玩家的真实按键，按「每 N 次按键为一段」逐段解锁引导圈。
///
/// 判定方式（用户反馈后明确）：**直接判定键盘是否按下**——所有判定都来自
/// <see cref="MusicPlayerInputHook"/> 的真实 KeyDown 事件，不做画面识别、不依赖覆盖层，
/// 覆盖层只负责显示。段内默认**必须按从左往右的顺序**弹（<see cref="MusicGuidedSessionOptions.StrictOrder"/>）：
/// 提前按了后面的键不算数、只提示该先按哪个；关掉严格顺序就退回「段内任意顺序」。
///
/// 设计约束（与用户确认过的行为）：
/// - 只显示引导、绝不代按：本服务不会向游戏发送任何按键；
/// - 完全按原谱键位，没有节奏限制，玩家弹得快慢都行；
/// - 和弦（同一时刻的多个键）合并算「一次按键」，每个键各画一个圈、同时收缩，和弦内部的几个键不分先后；
/// - 按错只把当前段清空、重按本段，已完成的段落保留；
/// - 只有当前段全部弹对，才显示下一段的键位引导。
/// </summary>
public sealed class MusicGuidedSession : IDisposable
{
    private readonly object _sync = new();
    private readonly IReadOnlyList<MusicGuidedPress> _presses;
    private readonly HashSet<char> _keySet;
    private readonly MusicGuidedSessionOptions _options;

    private MusicPlayerInputHook? _hook;
    private int _segmentIndex;
    private int _pressIndex;

    /// <summary>本段按键快照（每次换段/重来时重装）</summary>
    private MusicGuidedPress[] _segmentPresses = [];

    /// <summary>本段内还没弹完的按键序号（段内序号，按谱面顺序）</summary>
    private readonly List<int> _unplayedPresses = [];

    /// <summary>本段内已经弹完的按键序号（段内序号，允许不连续）</summary>
    private readonly List<int> _satisfiedPresses = [];

    /// <summary>每个还没弹完的按键上已经按下的键（和弦用）</summary>
    private readonly Dictionary<int, HashSet<char>> _pendingChords = [];

    /// <summary>当前按住、且属于本段待完成按键的键（仅用于界面高亮）</summary>
    private readonly HashSet<char> _heldKeys = [];

    private DateTime _errorCooldownUntil;
    private IReadOnlyDictionary<char, GuidedKeyPosition> _positions = new Dictionary<char, GuidedKeyPosition>();
    private double _layoutWidth;
    private double _layoutHeight;
    private bool _disposed;

    /// <summary>自动识别键位的结果说明（显示在覆盖层的诊断行上，失败时也会写明原因）</summary>
    private string _detectionText = string.Empty;

    /// <summary>
    /// 正在重新识别键位。识别期间玩家按下的键一律忽略：那会儿覆盖层已经撤下去、键位参数也还没定，
    /// 判定没有意义，反而会把当前这一段的进度记成按错。
    /// </summary>
    private bool _redetecting;

    /// <summary>自动识别次数与间隔：第一次就失败时再给两次机会，避开游戏界面正在切换的那一瞬间</summary>
    private const int DetectAttempts = 3;

    /// <inheritdoc cref="DetectAttempts" />
    private const int DetectRetryDelayMilliseconds = 130;

    /// <summary>识别结果和内置默认值的最大相对偏差，超过就认为量出来的东西不可信、继续用配置参数</summary>
    /// <remarks>
    /// 比的是内置默认值而不是当前配置：默认值本身就是 1080P 的实测真值，识别出来的比例坐标在任何分辨率/宽高比下
    /// 都应该贴着它（16:10 上下留黑边也只有百分之几的差），所以这条只是「防止量出一个荒唐结果」的兜底。
    /// </remarks>
    private const double MaxAcceptedDeviation = 0.45;

    /// <summary>截图宽高比与游戏窗口宽高比允许的偏差</summary>
    private const double AspectTolerance = 0.02;

    public MusicGuidedSession(IReadOnlyList<MusicGuidedPress> presses, MusicGuidedSessionOptions options)
    {
        _presses = presses;
        _options = options;
        _keySet = new HashSet<char>(presses.SelectMany(p => p.Keys));
    }

    /// <summary>总段落数</summary>
    public int SegmentCount { get; private set; }

    /// <summary>覆盖层实现（由 View 持有，用来在窗口尺寸变化时驱动 <see cref="Pump"/>()）</summary>
    public IMusicGuidedOverlay? Overlay => _options.Overlay;

    /// <summary>当前段落序号（0 起）</summary>
    public int SegmentIndex => _segmentIndex;

    /// <summary>会话是否已经结束（弹完或被停止）</summary>
    public bool IsFinished { get; private set; }

    /// <summary>累计按错次数</summary>
    public int ErrorCount { get; private set; }

    /// <summary>当前段内已经弹对的按键次数</summary>
    public int SegmentProgress => _pressIndex;

    /// <summary>播放按钮 / 停止按钮是否应该显示为「演奏中」</summary>
    public bool IsRunning => !IsFinished;

    /// <summary>
    /// 启动引导：安装键盘钩子、显示覆盖层、显示第 1 段。
    /// </summary>
    public bool Start()
    {
        ThrowIfDisposed();
        if (_presses.Count == 0)
        {
            return false;
        }

        SegmentCount = (_presses.Count + _options.SegmentSize - 1) / _options.SegmentSize;

        // 先量琴键位置、再贴覆盖层：覆盖层自己也是画在游戏画面上的，
        // 等它显示出来再截图就有可能把引导圈一起截进去（用 BitBlt 截图时会截到）。
        DetectKeysOnStart();

        var overlay = _options.Overlay;
        if (overlay != null && _options.OverlayEnabled)
        {
            overlay.ExitRequested += OnOverlayExitRequested;
            var handle = ResolveGameHandle();
            if (handle == 0 || !overlay.Attach(handle))
            {
                // 覆盖层不显示也允许继续：状态面板缺失时仍然可以靠主界面的进度提示跟弹。
                overlay = null;
            }
        }
        else
        {
            overlay = null;
        }

        lock (_sync)
        {
            _segmentIndex = 0;
            IsFinished = false;
            LoadSegmentLocked(0);
        }

        _hook = new MusicPlayerInputHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        if (!_hook.Start())
        {
            _hook.Dispose();
            _hook = null;
            overlay?.UpdateDetach();
            _options.Logger?.Invoke("引导式跟弹启动失败：无法安装键盘钩子。");
            return false;
        }

        // 覆盖层已经贴好游戏窗口，先把键位位置算出来再显示第一段。
        Pump();
        PushSegment();
        _options.Overlay?.UpdateDetection(_detectionText);
        _options.Logger?.Invoke($"引导式跟弹开始：共 {SegmentCount} 段，每段 {_options.SegmentSize} 次按键。");
        return true;
    }

    /// <summary>
    /// 重新识别键位（覆盖层上按 Ctrl+R 调用）：先让覆盖层躲开截图，量完再贴回来并按新位置重画当前段。
    ///
    /// 抓帧与识别放在后台线程里跑：低级键盘钩子的回调就在安装钩子的线程（UI 线程）上执行，
    /// 在这里同步抓帧 + 识别会把回调堵住，轻则按键迟滞，重则超过系统的 LowLevelHooksTimeout
    /// 被摘掉钩子、之后再也收不到按键。所以本方法必须在 UI 线程调用：await 之后要回到 UI 线程操作覆盖层。
    /// </summary>
    /// <returns>是否识别成功</returns>
    public async Task<bool> RedetectKeysAsync()
    {
        if (IsFinished || _disposed)
        {
            return false;
        }

        var overlay = _options.Overlay;
        var wasAttached = overlay is { IsAttached: true };

        lock (_sync)
        {
            if (_redetecting)
            {
                return false;
            }

            _redetecting = true;
        }

        MusicDetectedKeyGrid? grid;
        try
        {
            if (wasAttached)
            {
                overlay!.UpdateDetach();
                await Task.Delay(DetectRetryDelayMilliseconds);
            }

            // 这是用户主动要求的重新识别，不受「自动识别」开关影响。
            grid = await Task.Run(TryDetectKeys);
        }
        finally
        {
            // 识别期间会话可能已经被停掉 / 释放（玩家按 Esc 退出、切歌、离开页面都会走 Stop）：
            // 这时不能再把覆盖层贴回去。_redetecting 无论如何都要清掉。
            if (wasAttached && !IsFinished && !_disposed)
            {
                overlay!.Attach(ResolveGameHandle());
            }

            lock (_sync)
            {
                _redetecting = false;
            }
        }

        // 同理：会话已经结束就不要再写回键位参数了，那会覆盖掉后来那次会话的共享 KeyLayout。
        if (IsFinished || _disposed)
        {
            return false;
        }

        if (grid == null)
        {
            // 没量出来也要把刚才那一段画回去，别让玩家对着一个空覆盖层发呆。
            if (wasAttached)
            {
                RefreshLayout();
                _options.Overlay?.UpdateDetection(_detectionText);
            }

            return false;
        }

        ApplyDetectedGrid(grid);
        _options.Logger?.Invoke($"已重新识别键位：{_detectionText}");
        if (wasAttached)
        {
            RefreshLayout();
            _options.Overlay?.UpdateDetection(_detectionText);
        }

        return true;
    }

    /// <summary>
    /// 开始前从一帧游戏画面里量出琴键的实际位置，并写回 <see cref="MusicGuidedSessionOptions.KeyLayout"/>。
    ///
    /// 为什么要这么做：琴键在画面上的位置由**游戏自己**的界面缩放决定，1080P / 2K / 4K、16:10、21:9
    /// 各有各的比例，靠「默认参数 × 换算比例」推算总有偏差（用户反馈的「2K 下对不准」就是这个原因）。
    /// 量出来的结果会换算成 1080P 基准写回配置，于是三种分辨率都自动适配，也留作下次识别失败时的回退值。
    /// 识别失败不影响使用：只记一条日志，仍然用配置里的参数。
    /// </summary>
    private void DetectKeysOnStart()
    {
        // 玩家自己微调过就以他的为准：识别会把手动调好的位置覆盖掉，
        // 而「调好一次、以后每次打开都还是这个位置」是明确要求过的行为。
        if (_options.KeyLayout.ManualCalibrated)
        {
            _detectionText = "键位：使用上次手动校准的参数（想重新识别按 Ctrl+R）";
            _options.Logger?.Invoke("键位参数是玩家手动校准过的，本次不自动识别（Ctrl+R 可强制重新识别）。");
            return;
        }

        if (!_options.AutoDetectKeys)
        {
            _detectionText = "键位：使用配置参数（自动识别已关闭）";
            return;
        }

        var grid = TryDetectKeys();
        if (grid == null)
        {
            return;
        }

        ApplyDetectedGrid(grid);
        _options.Logger?.Invoke(_detectionText);
    }

    /// <summary>把量出来的琴键阵列写回键位参数（单位换回 1080P 基准）</summary>
    private void ApplyDetectedGrid(MusicDetectedKeyGrid grid)
    {
        var layout = _options.KeyLayout;
        layout.BaseX = grid.OriginX1080P;
        layout.BaseY = grid.OriginY1080P;
        layout.ColumnSpacing = grid.ColumnSpacing1080P;
        layout.RowSpacing = grid.RowSpacing1080P;
        layout.RingRadius = grid.Radius1080P;

        // 原神三排琴键的左边缘是上下对齐的（实测 611.2 / 611.3 / 611.4 像素），所以这里顺手归零：
        // 旧版本把 RowIndent 的默认值写成过 48，那套数字会留在玩家配置里，
        // 让最下一排整体右移 2×48×缩放（2K 下正好 +128 像素）——识别成功就没有理由再留着它。
        layout.RowIndent = 0;

        // 参数已经是「量出来的」而不是「手动调出来的」，下次开始可以继续自动识别。
        layout.ManualCalibrated = false;

        _detectionText = grid.Describe();
    }

    /// <summary>抓一帧游戏画面并量出琴键位置；任何一步不成立都返回 null（绝不把异常抛给引导流程）</summary>
    private MusicDetectedKeyGrid? TryDetectKeys()
    {
        // 先用配置里的参数当先验——玩家可能用 Ctrl 微调过，那最接近他屏幕上的真实位置。
        var priors = _options.KeyLayout;
        var grid = DetectWithPriors(priors);

        // 配置被旧版本写歪、或者按它试不出来时，换内置默认值（16:9 的实测真值）再试一次，
        // 免得「配置里的旧数值」反而成了自动识别的绊脚石。
        if (grid == null)
        {
            var defaults = new MusicGuidedKeyLayoutConfig();
            if (priors.BaseX != defaults.BaseX || priors.BaseY != defaults.BaseY ||
                priors.ColumnSpacing != defaults.ColumnSpacing || priors.RowSpacing != defaults.RowSpacing ||
                priors.RingRadius != defaults.RingRadius)
            {
                grid = DetectWithPriors(defaults);
            }
        }

        if (grid == null)
        {
            _detectionText = "键位：使用配置参数（自动识别没能在画面里找到琴键，可按 Ctrl+R 重试）";
            return null;
        }

        var deviation = MaxRelativeDeviation(grid, new MusicGuidedKeyLayoutConfig());
        if (deviation > MaxAcceptedDeviation)
        {
            _options.Logger?.Invoke(
                $"自动识别键位的结果和默认位置相差 {deviation:P0}，超过 {MaxAcceptedDeviation:P0}，为稳妥起见仍使用配置参数。");
            _detectionText = $"键位：自动识别结果偏差过大（{deviation:P0}），使用配置参数";
            return null;
        }

        return grid;
    }

    /// <summary>按给定的先验参数抓画面找琴键（最多 <see cref="DetectAttempts" /> 次，避开界面切换的那一两帧）</summary>
    private MusicDetectedKeyGrid? DetectWithPriors(MusicGuidedKeyLayoutConfig priors)
    {
        var handle = ResolveGameHandle();
        for (var attempt = 0; attempt < DetectAttempts; attempt++)
        {
            if (attempt > 0)
            {
                System.Threading.Thread.Sleep(DetectRetryDelayMilliseconds);
            }

            Mat? frame = null;
            try
            {
                frame = OpenGameFrame(handle);
                if (frame == null || frame.Empty())
                {
                    continue;
                }

                if (!IsCaptureAspectPlausible(frame.Width, frame.Height, handle))
                {
                    _detectionText = "键位：使用配置参数（截到的画面比例和游戏窗口不一致）";
                    return null;
                }

                var grid = MusicKeyAutoDetector.TryDetect(
                    frame, priors.ColumnSpacing, priors.RowSpacing, priors.RingRadius);
                if (grid != null)
                {
                    return grid;
                }
            }
            catch (Exception ex)
            {
                _options.Logger?.Invoke($"自动识别键位失败：{ex.Message}");
                _detectionText = "键位：自动识别失败（使用配置参数，可按 Ctrl+R 重试）";
                return null;
            }
            finally
            {
                frame?.Dispose();
            }
        }

        return null;
    }

    /// <summary>
    /// 抓一帧游戏画面，返回的画面由调用方负责释放。
    ///
    /// 优先用 BetterGI 自己的截图器（<c>TaskControl.CaptureToRectArea</c>：会按
    /// min(宽/1920, 高/1080) 归一化，与任务里看到的画面完全一致，也就是用户说的「截图器识别到的分辨率」）；
    /// 如果玩家只是打开演奏页面、没点过主界面的「启动」，截图器还没启动，就临时建一个、抓一帧后立刻释放，
    /// 这样自动识别键位不依赖任务是不是正在运行。
    /// </summary>
    private static Mat? OpenGameFrame(nint gameHandle)
    {
        if (TaskContext.Instance().IsInitialized)
        {
            using var region = Common.TaskControl.CaptureToRectArea(forceNew: true);
            var mat = region.SrcMat;
            return mat == null || mat.Empty() ? null : mat.Clone();
        }

        if (gameHandle == 0)
        {
            return null;
        }

        var config = TaskContext.Instance().Config;

        CaptureModes mode;
        try
        {
            mode = config.CaptureMode.ToCaptureMode();
        }
        catch (Exception)
        {
            mode = CaptureModes.BitBlt;
        }

        var capture = GameCaptureFactory.Create(mode);
        try
        {
            capture.Start(gameHandle, GameCaptureSettings.From(config));
            var frame = capture.Capture()?.Frame;
            return frame == null || frame.Empty() ? null : frame.Clone();
        }
        finally
        {
            capture.Dispose();
        }
    }

    /// <summary>
    /// 截图必须是「整个游戏窗口画面」的等比缩放（BetterGI 的截图会按 min(宽/1920, 高/1080) 归一化），
    /// 否则量出来的比例坐标不能直接搬回覆盖层。比例对不上就放弃自动识别。
    /// </summary>
    private bool IsCaptureAspectPlausible(double captureWidth, double captureHeight, nint handle)
    {
        if (captureWidth <= 0 || captureHeight <= 0)
        {
            return false;
        }

        if (handle == 0)
        {
            return true;
        }

        var rect = SystemControl.GetCaptureRect(handle);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return true;
        }

        var captureAspect = captureWidth / captureHeight;
        var windowAspect = rect.Width / (double)rect.Height;
        return Math.Abs(captureAspect - windowAspect) / windowAspect <= AspectTolerance;
    }

    private static double MaxRelativeDeviation(MusicDetectedKeyGrid grid, MusicGuidedKeyLayoutConfig layout)
    {
        return new[]
        {
            RelativeDeviation(grid.OriginX1080P, layout.BaseX),
            RelativeDeviation(grid.OriginY1080P, layout.BaseY),
            RelativeDeviation(grid.ColumnSpacing1080P, layout.ColumnSpacing),
            RelativeDeviation(grid.RowSpacing1080P, layout.RowSpacing),
            RelativeDeviation(grid.Radius1080P, layout.RingRadius)
        }.Max();
    }

    private static double RelativeDeviation(double value, double reference)
    {
        return Math.Abs(reference) < 1e-6 ? 0 : Math.Abs(value - reference) / Math.Abs(reference);
    }

    /// <summary>
    /// 主动停止（用户按了停止 / 切走页面）。
    /// </summary>
    public void Stop(string reason)
    {
        if (IsFinished)
        {
            return;
        }

        IsFinished = true;
        DetachHook();
        if (_options.Overlay != null)
        {
            _options.Overlay.ExitRequested -= OnOverlayExitRequested;
        }

        _options.Overlay?.UpdateStopped(reason);
        _options.Logger?.Invoke($"引导式跟弹结束：{reason}");
    }

    /// <summary>
    /// 由覆盖层每帧调用：游戏窗口尺寸变化时重新换算键位位置。
    /// </summary>
    public void Pump()
    {
        if (IsFinished)
        {
            return;
        }

        var overlay = _options.Overlay;
        if (overlay == null || !overlay.IsAttached)
        {
            return;
        }

        var canvas = overlay.CanvasSize;
        if (Math.Abs(canvas.Width - _layoutWidth) < 0.5 && Math.Abs(canvas.Height - _layoutHeight) < 0.5)
        {
            return;
        }

        RebuildLayout(canvas.Width, canvas.Height);

        // 位置变了要立刻按新位置重画当前段，否则圈要等到下一次按键才出现在正确位置。
        RedrawCurrentSegment();
    }

    /// <summary>
    /// 键位参数被现场微调（覆盖层上按住 Ctrl + 方向键等）后立刻重算并重画当前段。
    /// 覆盖层与会话共用同一个 <see cref="MusicGuidedSessionOptions.KeyLayout"/> 实例。
    /// </summary>
    public void RefreshLayout()
    {
        var overlay = _options.Overlay;
        if (overlay == null || IsFinished || !overlay.IsAttached)
        {
            return;
        }

        var canvas = overlay.CanvasSize;
        RebuildLayout(canvas.Width, canvas.Height);
        RedrawCurrentSegment();
    }

    /// <summary>
    /// 用当前键位位置重画当前段（窗口刚显示、或游戏窗口被移动/缩放时用）。
    /// </summary>
    private void RedrawCurrentSegment()
    {
        var overlay = _options.Overlay;
        if (overlay == null || IsFinished || _segmentIndex >= SegmentCount)
        {
            return;
        }

        MusicGuidedPress[]? segment = null;
        int[] satisfied = [];
        char[] held = [];
        lock (_sync)
        {
            if (_segmentIndex < SegmentCount)
            {
                segment = _segmentPresses.ToArray();
                satisfied = _satisfiedPresses.ToArray();
                held = _heldKeys.ToArray();
            }
        }

        if (segment == null)
        {
            return;
        }

        overlay.UpdateSegment(_segmentIndex, SegmentCount, segment, _positions, _options.TrackName, satisfied);
        overlay.UpdateProgress(satisfied, held);
    }

    private void RebuildLayout(double width, double height)
    {
        _layoutWidth = width;
        _layoutHeight = height;

        var profile = _options.KeyProfile;
        if (profile == null || width <= 0 || height <= 0)
        {
            _positions = new Dictionary<char, GuidedKeyPosition>();
            return;
        }

        _positions = MusicKeyLayout.Resolve(profile, _options.KeyLayout, width, height);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachHook();
    }

    private void DetachHook()
    {
        var hook = _hook;
        _hook = null;
        if (hook == null)
        {
            return;
        }

        hook.KeyPressed -= OnKeyPressed;
        hook.KeyReleased -= OnKeyReleased;
        hook.Dispose();
    }

    private void OnOverlayExitRequested()
    {
        ExitRequested?.Invoke();
    }

    /// <summary>玩家在覆盖层上要求退出（Esc / ✕），由 ViewModel 订阅后结束会话</summary>
    public event Action? ExitRequested;

    private nint ResolveGameHandle()
    {
        var handle = TaskContext.Instance().GameHandle;
        return handle != nint.Zero ? handle : SystemControl.FindGenshinImpactHandle();
    }

    private void OnKeyPressed(Keys key)
    {
        // Ctrl / Alt 组合键是覆盖层自己的功能键（Ctrl+R 重新识别、Ctrl+方向键现场微调），
        // 带着它们按下的字母不是琴键，不能拿去判定。
        if ((Control.ModifierKeys & (Keys.Control | Keys.Alt)) != 0)
        {
            return;
        }

        var pianoKey = ToPianoKey(key);
        if (pianoKey == null)
        {
            return;
        }

        var value = pianoKey.Value;
        var advance = false;
        lock (_sync)
        {
            if (IsFinished || _redetecting || _options.IsGameActive?.Invoke() == false)
            {
                return;
            }

            if (!_keySet.Contains(value))
            {
                // 整首曲子里都没有这个键：按错。
                HandleWrongKeyLocked(value.ToString());
                return;
            }

            advance = HandleCorrectKeyLocked(value);
        }

        if (advance)
        {
            AdvanceLocked();
        }
    }

    private void OnKeyReleased(Keys key)
    {
        var pianoKey = ToPianoKey(key);
        if (pianoKey == null)
        {
            return;
        }

        var value = pianoKey.Value;
        lock (_sync)
        {
            // 释放必须清掉「已按下」状态：就算此刻按着 Ctrl / Alt（组合键是覆盖层自己的功能键），
            // 或者正在重新识别，也不能把已经松开的键留在 _heldKeys 里，否则覆盖层会一直显示它被按住。
            // 没记过的键（比如 Ctrl+R 里的 R）自然删不掉，直接返回即可。
            var removed = _heldKeys.Remove(value);
            if (IsFinished || _redetecting || !removed)
            {
                return;
            }

            // 松手只影响「已按下」高亮，不影响完成判定（完成在按下那一刻就定了）。
            _options.Overlay?.UpdateProgress(_satisfiedPresses, _heldKeys.ToArray());
        }
    }

    private void HandleWrongKeyLocked(string wrongKeys)
    {
        var now = DateTime.UtcNow;
        if (now < _errorCooldownUntil)
        {
            return;
        }

        _errorCooldownUntil = now.AddMilliseconds(Math.Max(250, _options.SegmentSize * 120));
        ErrorCount++;

        // 需求：按错只把当前段清空、重按本段，已完成段落保留。
        _unplayedPresses.Clear();
        for (var i = 0; i < _segmentPresses.Length; i++)
        {
            _unplayedPresses.Add(i);
        }

        _satisfiedPresses.Clear();
        _pendingChords.Clear();
        _heldKeys.Clear();
        _pressIndex = 0;

        _options.Overlay?.UpdateProgress(_satisfiedPresses, _heldKeys.ToArray());
        _options.Overlay?.UpdateError(wrongKeys);
    }

    /// <summary>
    /// 处理一次真实按下的键。
    /// 返回 true 表示本段已经按完，需要换到下一段（调用方必须在锁外调用 <see cref="AdvanceLocked"/>）。
    ///
    /// 顺序规则由 <see cref="MusicGuidedSessionOptions.StrictOrder"/> 决定：
    /// - 开启（默认，用户明确要求「必须按从左往右顺序按下才能判定为正确」）：只有当前该弹的那一次按键算数。
    ///   提前按了后面的键不算按错、也不清段，只提示该先按哪个——因为那个键本段里确实有，玩家只是抢拍了。
    /// - 关闭：段内任意顺序，按下的键只要属于本段某一个还没弹完的按键就先记到它头上。
    /// 两种情况都要求「这个键属于本段」，否则才算真按错。
    /// </summary>
    private bool HandleCorrectKeyLocked(char key)
    {
        // 严格顺序下唯一合法的目标就是第一个还没弹完的按键（_unplayedPresses 在严格模式下始终保持有序）。
        var required = _options.StrictOrder && _unplayedPresses.Count > 0 ? _unplayedPresses[0] : -1;

        var target = -1;
        foreach (var local in _unplayedPresses)
        {
            if (Array.IndexOf(_segmentPresses[local].Keys, key) >= 0)
            {
                target = local;
                break;
            }
        }

        if (target < 0)
        {
            // 这个键属于后面的段落：算按错，当前段重来。
            HandleWrongKeyLocked(key.ToString());
            return false;
        }

        if (required >= 0 && target != required)
        {
            // 键是对的，只是提前按了：不计数、不报错，提示一下当前该按的是哪一次按键。
            _options.Overlay?.UpdateOrderHint(_segmentPresses[required].Display);
            return false;
        }

        if (!_pendingChords.TryGetValue(target, out var pending))
        {
            pending = [];
            _pendingChords[target] = pending;
        }

        if (!pending.Add(key))
        {
            // 系统自动重复（长按）送进来的同一个键：不重复计数。
            return false;
        }

        _heldKeys.Add(key);

        // 界面先把这次按对的键标出来；和弦要等所有键都齐了才算完成。
        if (!_segmentPresses[target].Keys.All(pending.Contains))
        {
            _options.Overlay?.UpdateProgress(_satisfiedPresses, _heldKeys.ToArray());
            return false;
        }

        // 和弦已经按全：直接完成这一次按键，不必等松手（避免玩家长按卡住）。
        _pendingChords.Remove(target);
        _unplayedPresses.Remove(target);
        _satisfiedPresses.Add(target);
        _pressIndex = _satisfiedPresses.Count;

        // 这一次按键算完成了：让覆盖层把对应的圈收掉、状态面板打勾。
        _options.Overlay?.UpdateProgress(_satisfiedPresses, _heldKeys.ToArray());
        return _unplayedPresses.Count == 0;
    }

    private void LoadSegmentLocked(int segmentIndex)
    {
        _segmentPresses = GetSegment(segmentIndex).ToArray();
        _unplayedPresses.Clear();
        _satisfiedPresses.Clear();
        _pendingChords.Clear();
        _heldKeys.Clear();
        _pressIndex = 0;

        for (var i = 0; i < _segmentPresses.Length; i++)
        {
            _unplayedPresses.Add(i);
        }
    }

    private void AdvanceLocked()
    {
        MusicGuidedPress[]? nextSegment = null;
        var segmentIndex = -1;
        var totalSegments = 0;
        string? completedTrack = null;
        var finished = false;

        lock (_sync)
        {
            if (IsFinished)
            {
                return;
            }

            _segmentIndex++;
            if (_segmentIndex >= SegmentCount)
            {
                IsFinished = true;
                finished = true;
                completedTrack = _options.TrackName;
            }
            else
            {
                LoadSegmentLocked(_segmentIndex);
                nextSegment = _segmentPresses.ToArray();
                segmentIndex = _segmentIndex;
                totalSegments = SegmentCount;
            }
        }

        if (finished)
        {
            DetachHook();
            if (_options.Overlay != null)
            {
                _options.Overlay.ExitRequested -= OnOverlayExitRequested;
            }

            _options.Overlay?.UpdateCompleted(completedTrack ?? "当前曲目");
            _options.Logger?.Invoke("引导式跟弹：整首曲目已全部弹对。");
            return;
        }

        _options.Overlay?.UpdateSegment(segmentIndex, totalSegments, nextSegment!, _positions,
            _options.TrackName, []);
        _options.OnSegmentChanged?.Invoke(segmentIndex, totalSegments);
    }

    private void PushSegment()
    {
        MusicGuidedPress[] segment;
        int index;
        int total;
        int[] satisfied;
        lock (_sync)
        {
            segment = _segmentPresses.ToArray();
            index = _segmentIndex;
            total = SegmentCount;
            satisfied = _satisfiedPresses.ToArray();
        }

        _options.Overlay?.UpdateSegment(index, total, segment, _positions, _options.TrackName, satisfied);
        _options.OnSegmentChanged?.Invoke(index, total);
    }

    private IReadOnlyList<MusicGuidedPress> GetSegment(int segmentIndex)
    {
        var start = segmentIndex * _options.SegmentSize;
        var count = Math.Min(_options.SegmentSize, _presses.Count - start);
        if (count <= 0)
        {
            return [];
        }

        var result = new List<MusicGuidedPress>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(_presses[start + i]);
        }

        return result;
    }

    private static char? ToPianoKey(Keys key)
    {
        if (key < Keys.A || key > Keys.Z)
        {
            return null;
        }

        return (char)('A' + (key - Keys.A));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MusicGuidedSession));
        }
    }
}

/// <summary>
/// 引导式跟弹会话的构造参数。
/// </summary>
public sealed class MusicGuidedSessionOptions
{
    /// <summary>每段包含的按键次数（和弦算一次）</summary>
    public int SegmentSize { get; init; } = 5;

    /// <summary>是否显示置顶覆盖层</summary>
    public bool OverlayEnabled { get; init; } = true;

    /// <summary>按错闪烁时长</summary>
    public int ErrorFlashMilliseconds { get; init; } = 260;

    /// <summary>当前曲目名（仅用于界面提示）</summary>
    public string TrackName { get; init; } = string.Empty;

    /// <summary>当前输出乐器名（仅用于界面提示）</summary>
    public string InstrumentName { get; init; } = string.Empty;

    /// <summary>当前输出乐器档案：用来把键位换算成屏幕位置</summary>
    public InstrumentProfile? KeyProfile { get; init; }

    /// <summary>键位提示板布局配置</summary>
    public MusicGuidedKeyLayoutConfig KeyLayout { get; init; } = new();

    /// <summary>开始时是否从游戏画面自动识别琴键的实际位置（识别结果会写回 <see cref="KeyLayout"/>）</summary>
    public bool AutoDetectKeys { get; init; } = true;

    /// <summary>段内是否必须按从左往右的顺序弹（默认开；关掉则段内任意顺序都算对）</summary>
    public bool StrictOrder { get; init; } = true;

    /// <summary>覆盖层实现</summary>
    public IMusicGuidedOverlay? Overlay { get; init; }

    /// <summary>判定是否位于游戏窗口前台（返回 false 时忽略按键）</summary>
    public Func<bool>? IsGameActive { get; init; }

    /// <summary>日志输出</summary>
    public Action<string>? Logger { get; init; }

    /// <summary>段落切换回调（段序号从 0 起）</summary>
    public Action<int, int>? OnSegmentChanged { get; init; }
}
