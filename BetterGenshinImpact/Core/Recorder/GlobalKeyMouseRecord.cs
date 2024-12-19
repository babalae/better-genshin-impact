using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Model;
using Gma.System.MouseKeyHook;
using Microsoft.Extensions.Logging;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Core.Recorder;

public class GlobalKeyMouseRecord : Singleton<GlobalKeyMouseRecord>
{
    private readonly ILogger<GlobalKeyMouseRecord> _logger = App.GetLogger<GlobalKeyMouseRecord>();

    private KeyMouseRecorder? _recorder;

    private readonly Dictionary<Keys, bool> _keyDownState = [];

    private DirectInputMonitor? _directInputMonitor;

    private readonly System.Timers.Timer _timer = new();

    private bool _isInMainUi = false; // 是否在主界面

    public KeyMouseRecorderStatus Status { get; set; } = KeyMouseRecorderStatus.Stop;

    public GlobalKeyMouseRecord()
    {
        _timer.Elapsed += Tick;
        _timer.Interval = 50; // ms
    }

    public async Task StartRecord()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            Toast.Warning("请先在启动页，启动截图器再使用本功能");
            return;
        }

        if (Status != KeyMouseRecorderStatus.Stop)
        {
            Toast.Warning("已经在录制状态，请不要重复启动录制功能");
            return;
        }

        Status = KeyMouseRecorderStatus.Start;

        SystemControl.ActivateWindow();

        _logger.LogInformation("录制：{Text}", "实时任务已暂停");
        _logger.LogInformation("注意：录制时遇到主界面（鼠标永远在界面中心）和其他界面（鼠标可自由移动，比如地图等）的切换，请把手离开鼠标等待录制模式切换日志");

        for (var i = 3; i >= 1; i--)
        {
            _logger.LogInformation("{Sec}秒后启动录制...", i);
            await Task.Delay(1000);
        }

        TaskTriggerDispatcher.Instance().StopTimer();

        _timer.Start();

        _recorder = new KeyMouseRecorder();
        _directInputMonitor = new DirectInputMonitor();
        _directInputMonitor.Start();

        Status = KeyMouseRecorderStatus.Recording;

        _logger.LogInformation("录制：{Text}", "已启动");
    }

    public string StopRecord()
    {
        if (Status != KeyMouseRecorderStatus.Recording)
        {
            throw new InvalidOperationException("未处于录制中状态，无法停止");
        }

        var macro = _recorder?.ToJsonMacro() ?? string.Empty;
        _recorder = null;
        _directInputMonitor?.Stop();
        _directInputMonitor?.Dispose();
        _directInputMonitor = null;

        _timer.Stop();

        _logger.LogInformation("录制：{Text}", "结束录制");

        TaskTriggerDispatcher.Instance().StartTimer();

        Status = KeyMouseRecorderStatus.Stop;

        return macro;
    }

    public void Tick(object? sender, EventArgs e)
    {
        var ra = TaskControl.CaptureToRectArea();
        var iconRa = ra.Find(ElementAssets.Instance.FriendChat);
        var exist = iconRa.IsExist();
        if (exist != _isInMainUi)
        {
            _logger.LogInformation("录制：{Text}", exist ? "进入主界面，捕获鼠标相对移动" : "离开主界面，捕获鼠标绝对移动");
        }
        _isInMainUi = exist;
        iconRa.Dispose();
        ra.Dispose();
    }

    public void GlobalHookKeyDown(KeyEventArgs e, uint time)
    {
        // 排除热键
        if (e.KeyCode.ToString() == TaskContext.Instance().Config.HotKeyConfig.KeyMouseMacroRecordHotkey)
        {
            return;
        }

        if (_keyDownState.TryGetValue(e.KeyCode, out var v))
        {
            if (v)
            {
                return; // 处于按下状态的不再记录
            }
            else
            {
                _keyDownState[e.KeyCode] = true;
            }
        }
        else
        {
            _keyDownState.Add(e.KeyCode, true);
        }
        // Debug.WriteLine($"KeyDown: {e.KeyCode}");
        _recorder?.KeyDown(e, time);
    }

    public void GlobalHookKeyUp(KeyEventArgs e, uint time)
    {
        if (e.KeyCode.ToString() == TaskContext.Instance().Config.HotKeyConfig.Test1Hotkey)
        {
            return;
        }

        if (_keyDownState.TryGetValue(e.KeyCode, out bool state) && state)
        {
            // Debug.WriteLine($"KeyUp: {e.KeyCode}");
            _keyDownState[e.KeyCode] = false;
            _recorder?.KeyUp(e, time);
        }
    }

    public void GlobalHookMouseDown(MouseEventExtArgs e)
    {
        // Debug.WriteLine($"MouseDown: {e.Button}");
        _recorder?.MouseDown(e);
    }

    public void GlobalHookMouseUp(MouseEventExtArgs e)
    {
        // Debug.WriteLine($"MouseUp: {e.Button}");
        _recorder?.MouseUp(e);
    }

    public void GlobalHookMouseMoveTo(MouseEventExtArgs e)
    {
        if (_isInMainUi)
        {
            return;
        }
        // Debug.WriteLine($"MouseMove: {e.X}, {e.Y}");
        _recorder?.MouseMoveTo(e);
    }
    
    public void GlobalHookMouseWheel(MouseEventExtArgs e)
    {
        // Debug.WriteLine($"MouseWheel: {e.Delta}");
        _recorder?.MouseWheel(e);
    }

    public void GlobalHookMouseMoveBy(MouseState state, uint time)
    {
        if (state is { X: 0, Y: 0 } || !_isInMainUi)
        {
            return;
        }
        // Debug.WriteLine($"MouseMoveBy: {state.X}, {state.Y}");
        _recorder?.MouseMoveBy(state, time);
    }
}

public enum KeyMouseRecorderStatus
{
    Start,
    Recording,
    Stop
}
