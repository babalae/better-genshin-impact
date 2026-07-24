using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.View.Windows;
using Gma.System.MouseKeyHook;
using Microsoft.Extensions.Logging;
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

    private IDisposable? _relativeMouseSubscription;

    private readonly System.Timers.Timer _timer = new();

    private bool _isInMainUi = false; // 是否在主界面

    public KeyMouseRecorderStatus Status { get; set; } = KeyMouseRecorderStatus.Stop;

    public GlobalKeyMouseRecord()
    {
        _timer.Elapsed += Tick;
        _timer.Interval = 50; // ms
    }

    public async Task<bool> StartRecord(
        RelativeMouseInputType inputType = RelativeMouseInputType.DirectInput)
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            Toast.Warning("请先在启动页，启动截图器再使用本功能");
            return false;
        }

        if (Status != KeyMouseRecorderStatus.Stop)
        {
            Toast.Warning("已经在录制状态，请不要重复启动录制功能");
            return false;
        }

        Status = KeyMouseRecorderStatus.Start;
        var taskTriggerStopped = false;
        try
        {
            SystemControl.ActivateWindow();

            _logger.LogInformation("录制：{Text}", "实时任务已暂停");
            _logger.LogInformation("注意：录制时遇到主界面（鼠标永远在界面中心）和其他界面（鼠标可自由移动，比如地图等）的切换，请把手离开鼠标等待录制模式切换日志");

            for (var i = 3; i >= 1; i--)
            {
                _logger.LogInformation("{Sec}秒后启动录制...", i);
                await Task.Delay(1000);
            }

            TaskTriggerDispatcher.Instance().StopTimer();
            taskTriggerStopped = true;

            _recorder = new KeyMouseRecorder();
            var monitorFactory = App.GetService<IRelativeMouseInputMonitorFactory>()
                                 ?? throw new InvalidOperationException("相对鼠标捕获工厂未注册");
            _relativeMouseSubscription = monitorFactory
                .Get(inputType)
                .Subscribe(RelativeMouseMoved);

            _timer.Start();
            Status = KeyMouseRecorderStatus.Recording;

            _logger.LogInformation("录制：已启动，相对鼠标捕获方式：{InputType}", inputType);
            return true;
        }
        catch (Exception ex)
        {
            _timer.Stop();
            try
            {
                _relativeMouseSubscription?.Dispose();
            }
            catch (Exception disposeException)
            {
                _logger.LogWarning(disposeException, "清理相对鼠标捕获订阅失败");
            }
            _relativeMouseSubscription = null;
            _recorder = null;

            if (taskTriggerStopped)
            {
                TaskTriggerDispatcher.Instance().StartTimer();
            }

            Status = KeyMouseRecorderStatus.Stop;
            _logger.LogError(ex, "启动键鼠录制失败，相对鼠标捕获方式：{InputType}", inputType);
            await ThemedMessageBox.ErrorAsync(
                $"启动键鼠录制失败：{ex.Message}",
                "键鼠录制启动失败");
            return false;
        }
    }

    public string StopRecord()
    {
        if (Status != KeyMouseRecorderStatus.Recording)
        {
            throw new InvalidOperationException("未处于录制中状态，无法停止");
        }

        _timer.Stop();

        var recorder = _recorder;
        _recorder = null;
        var relativeMouseSubscription = _relativeMouseSubscription;
        _relativeMouseSubscription = null;

        try
        {
            relativeMouseSubscription?.Dispose();
            return recorder?.ToJsonMacro() ?? string.Empty;
        }
        finally
        {
            _logger.LogInformation("录制：{Text}", "结束录制");
            TaskTriggerDispatcher.Instance().StartTimer();
            Status = KeyMouseRecorderStatus.Stop;
        }
    }

    public void Tick(object? sender, EventArgs e)
    {
        var ra = TaskControl.CaptureToRectArea();
        var iconRa = ra.Find(ElementRecognition.Get("FriendChat", ra));
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

    private void RelativeMouseMoved(object? sender, RelativeMouseMoveEventArgs e)
    {
        if (e is { DeltaX: 0, DeltaY: 0 } || !_isInMainUi)
        {
            return;
        }
        // Debug.WriteLine($"MouseMoveBy: {e.DeltaX}, {e.DeltaY}");
        _recorder?.MouseMoveBy(e);
    }
}

public enum KeyMouseRecorderStatus
{
    Start,
    Recording,
    Stop
}
