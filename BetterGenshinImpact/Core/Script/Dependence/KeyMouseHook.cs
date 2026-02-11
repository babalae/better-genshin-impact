using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using Gma.System.MouseKeyHook;
using Microsoft.ClearScript;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class KeyMouseHook: IDisposable
{
    private IKeyboardMouseEvents? _appEvents;
    private IKeyboardMouseEvents AppHook => _appEvents ??= MouseKeyMonitor.GlobalHook;
    
    private readonly List<ScriptObject> _keyDownDataCallbacks = new();
    private readonly List<ScriptObject> _keyUpDataCallbacks = new();
    private readonly List<ScriptObject> _keyDownCodeCallbacks = new();
    private readonly List<ScriptObject> _keyUpCodeCallbacks = new();
    private readonly List<ScriptObject> _mouseDownCallbacks = new();
    private readonly List<ScriptObject> _mouseUpCallbacks = new();
    private readonly List<ScriptObject> _mouseMoveCallbacks = new();
    private readonly List<ScriptObject> _mouseWheelCallbacks = new();
    
    /// <summary>
    /// 存储每个鼠标移动回调的间隔时间（毫秒）
    /// </summary>
    private readonly Dictionary<ScriptObject, int> _mouseMoveCallbackIntervals = new();
    
    /// <summary>
    /// 存储每个鼠标移动回调的上次调用时间
    /// </summary>
    private readonly Dictionary<ScriptObject, DateTime> _lastMouseMoveCallbackTimes = new();
    
    private KeyEventHandler? _keyDownHandler;
    private KeyEventHandler? _keyUpHandler;
    private EventHandler<MouseEventExtArgs>? _mouseDownExtHandler;
    private EventHandler<MouseEventExtArgs>? _mouseUpExtHandler;
    private EventHandler<MouseEventExtArgs>? _mouseMoveExtHandler;
    private EventHandler<MouseEventExtArgs>? _mouseWheelExtHandler;
    
    private readonly object _mouseMoveLock = new();
    private DateTime _lastProcessMouseMoveTime = DateTime.MinValue;
    
    private readonly ILogger<KeyMouseHook> _logger = App.GetLogger<KeyMouseHook>();
    
    /// <summary>
    /// 统一处理回调函数执行过程中的异常
    /// </summary>
    /// <param name="ex">捕获到的异常</param>
    /// <param name="eventType">事件类型描述</param>
    private void HandleCallbackException(Exception ex, string eventType)
    {
        if (ex is ScriptEngineException scriptEx)
        {
            _logger.LogError("执行{eventType}JS回调时发生错误：{scriptEx.Message}，清除所有回调",eventType, scriptEx.Message); 
            _logger.LogDebug("{scriptEx}",scriptEx);
        }
        else
        {
            _logger.LogError("执行{eventType}回调时发生错误:{ex.Message}，清除所有回调,如果此异常出现在JS脚本结束时,请在JS脚本结束前手动调用Dispose()方法",eventType,ex.Message);
            _logger.LogDebug("{ex}",ex);
        }

        Dispose();
    }
    
    /// <summary>
    /// 将全局屏幕坐标转换为游戏窗口局部坐标
    /// </summary>
    /// <param name="globalX">全局X坐标</param>
    /// <param name="globalY">全局Y坐标</param>
    /// <returns>游戏窗口局部坐标(X, Y)，转换失败时返回(-1, -1)</returns>
    private (int X, int Y) ConvertToLocalCoordinates(int globalX, int globalY)
    {
        try
        {
            // 检查TaskContext是否已初始化
            if (!TaskContext.Instance().IsInitialized)
            {
                _logger.LogError("TaskContext未初始化，无法获取游戏窗口信息");
                return (-1, -1); 
            }
            
            var gameHandle = TaskContext.Instance().GameHandle;
            if (gameHandle == IntPtr.Zero)
            {
                _logger.LogError("游戏窗口句柄无效，无法获取游戏窗口位置");
                return (-1, -1); 
            }
            
            // 获取游戏窗口捕获区域
            var captureRect = SystemControl.GetCaptureRect(gameHandle);
            
            // 检查捕获区域是否有效
            if (captureRect.Width <= 0 || captureRect.Height <= 0)
            {
                _logger.LogError("获取的游戏窗口捕获区域无效，宽度或高度为0");
                return (-1, -1); 
            }
            
            // 计算局部坐标
            int localX = globalX - captureRect.Left;
            int localY = globalY - captureRect.Top;
            
            return (localX, localY);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转换鼠标坐标时发生错误");
            return (-1, -1); 
        }
    }
    
    private readonly System.Threading.Channels.Channel<Action> _eventChannel =
        System.Threading.Channels.Channel.CreateBounded<Action>(
            new System.Threading.Channels.BoundedChannelOptions(2048)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    private readonly CancellationTokenSource _cts = new();

    public KeyMouseHook()
    {
        // 启动后台事件处理器，确保不阻塞钩子线程
        _ = Task.Run(async () =>
        {
            try
            {
                var reader = _eventChannel.Reader;
                while (await reader.WaitToReadAsync(_cts.Token))
                {
                    while (reader.TryRead(out var action))
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                             // 内部错误已在 action 闭包中通过 HandleCallbackException 处理
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "键鼠钩子后台处理器发生异常");
            }
        });

        // 初始化事件处理程序
        _keyDownHandler = (_, args) =>
        {
            if (_keyDownDataCallbacks.Count == 0 && _keyDownCodeCallbacks.Count == 0) return;

            var keyDownDataCallbacksCopy = new List<ScriptObject>(_keyDownDataCallbacks);
            var keyDownCodeCallbacksCopy = new List<ScriptObject>(_keyDownCodeCallbacks);
            var keyDataStr = args.KeyData.ToString();
            var keyCodeStr = args.KeyCode.ToString();

            if (!_eventChannel.Writer.TryWrite(() =>
                {
                    // 调用KeyData回调
                    foreach (var callback in keyDownDataCallbacksCopy)
                    {
                        try
                        {
                            callback.InvokeAsFunction(keyDataStr);
                        }
                        catch (Exception ex)
                        {
                            HandleCallbackException(ex, "键盘按下事件");
                            return;
                        }
                    }

                    // 调用KeyCode回调
                    foreach (var callback in keyDownCodeCallbacksCopy)
                    {
                        try
                        {
                            callback.InvokeAsFunction(keyCodeStr);
                        }
                        catch (Exception ex)
                        {
                            HandleCallbackException(ex, "键盘按下事件");
                            return;
                        }
                    } 
                }))
            { 
                _logger.LogWarning("事件队列已满或已关闭，忽略键盘按下回调");
            }
        };
        
        _keyUpHandler = (_, args) =>
        {
            if (_keyUpDataCallbacks.Count == 0 && _keyUpCodeCallbacks.Count == 0) return;

            var keyUpDataCallbacksCopy = new List<ScriptObject>(_keyUpDataCallbacks);
            var keyUpCodeCallbacksCopy = new List<ScriptObject>(_keyUpCodeCallbacks);
            var keyDataStr = args.KeyData.ToString();
            var keyCodeStr = args.KeyCode.ToString();
            if (!_eventChannel.Writer.TryWrite(() =>
                {
                    // 调用KeyData回调
                    foreach (var callback in keyUpDataCallbacksCopy)
                    {
                        try
                        {
                            callback.InvokeAsFunction(keyDataStr);
                        }
                        catch (Exception ex)
                        {
                            HandleCallbackException(ex, "键盘释放事件");
                            return;
                        }
                    }

                    // 调用KeyCode回调
                    foreach (var callback in keyUpCodeCallbacksCopy)
                    {
                        try
                        {
                            callback.InvokeAsFunction(keyCodeStr);
                        }
                        catch (Exception ex)
                        {
                            HandleCallbackException(ex, "键盘释放事件");
                            return;
                        }
                    }
                }))
            { 
                _logger.LogWarning("事件队列已满或已关闭，忽略键盘释放回调");
            }
        };
        
        _mouseDownExtHandler = (_, args) =>
        {
            if (_mouseDownCallbacks.Count == 0) return;

            var mouseDownCallbacksCopy = new List<ScriptObject>(_mouseDownCallbacks);
            var (localX, localY) = ConvertToLocalCoordinates(args.X, args.Y);
            var buttonStr = args.Button.ToString();

            if (!_eventChannel.Writer.TryWrite(() =>
                {
                    foreach (var callback in mouseDownCallbacksCopy)
                    {
                        try
                        {
                            callback.InvokeAsFunction(buttonStr, localX, localY);
                        }
                        catch (Exception ex)
                        {
                            HandleCallbackException(ex, "鼠标按下事件");
                            return;
                        }
                    }
                }))
            { 
                _logger.LogWarning("事件队列已满或已关闭，忽略鼠标按下回调");
            }
        };
        
        _mouseUpExtHandler = (_, args) =>
        {
            if (_mouseUpCallbacks.Count == 0) return;

            var mouseUpCallbacksCopy = new List<ScriptObject>(_mouseUpCallbacks);
            var (localX, localY) = ConvertToLocalCoordinates(args.X, args.Y);
            var buttonStr = args.Button.ToString();

            if (!_eventChannel.Writer.TryWrite(() =>
                {
                    foreach (var callback in mouseUpCallbacksCopy)
                    {
                        try
                        {
                            callback.InvokeAsFunction(buttonStr, localX, localY);
                        }
                        catch (Exception ex)
                        {
                            HandleCallbackException(ex, "鼠标释放事件");
                            return;
                        }
                    }
                }))
            {
                _logger.LogWarning("事件队列已满或已关闭，忽略鼠标释放回调");
            }
        };
        
        _mouseMoveExtHandler = (_, args) =>
        {
            if (_mouseMoveCallbacks.Count == 0) return;

            var now = DateTime.Now;
            if ((now - _lastProcessMouseMoveTime).TotalMilliseconds < 10) return;
            _lastProcessMouseMoveTime = now;

            var mouseMoveCallbacksCopy = new List<ScriptObject>(_mouseMoveCallbacks);
            var (localX, localY) = ConvertToLocalCoordinates(args.X, args.Y);

            if (!_eventChannel.Writer.TryWrite(() =>
                {
                    foreach (var callback in mouseMoveCallbacksCopy)
                    {
                        
                        try
                        {
                            lock (_mouseMoveLock)
                            {
                                if (_mouseMoveCallbackIntervals.TryGetValue(callback, out var interval) &&
                                    _lastMouseMoveCallbackTimes.TryGetValue(callback, out var lastTime))
                                {
                                    var timeSpan = now - lastTime;
                                    if (timeSpan.TotalMilliseconds >= interval)
                                    {
                                        callback.InvokeAsFunction(localX, localY);
                                        _lastMouseMoveCallbackTimes[callback] = now;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            HandleCallbackException(ex, "鼠标移动事件");
                            return;
                        }
                    }
                }))
            {
                _logger.LogWarning("事件队列已满或已关闭，忽略鼠标移动回调");
            }
        };
        
        _mouseWheelExtHandler = (_, args) =>
        {
            if (_mouseWheelCallbacks.Count == 0) return;

            var mouseWheelCallbacksCopy = new List<ScriptObject>(_mouseWheelCallbacks);
            var (localX, localY) = ConvertToLocalCoordinates(args.X, args.Y);
            var delta = args.Delta;

            if (!_eventChannel.Writer.TryWrite(() =>
                {
                    foreach (var callback in mouseWheelCallbacksCopy)
                    {
                        try
                        {
                            callback.InvokeAsFunction(delta, localX, localY);
                        }
                        catch (Exception ex)
                        {
                            HandleCallbackException(ex, "鼠标滚轮事件");
                            return;
                        }
                    }
                }))
            {
                _logger.LogWarning("事件队列已满或已关闭，忽略鼠标滚轮回调");
            }
        };
        
        // 添加事件监听器
        AppHook.KeyDown += _keyDownHandler;
        AppHook.KeyUp += _keyUpHandler;
        AppHook.MouseDownExt += _mouseDownExtHandler;
        AppHook.MouseUpExt += _mouseUpExtHandler;
        AppHook.MouseMoveExt += _mouseMoveExtHandler;
        AppHook.MouseWheelExt += _mouseWheelExtHandler;
    }
    
    /// <summary>
    /// 注册键盘按下事件回调
    /// </summary>
    /// <param name="callback">回调函数</param>
    /// <param name="useCodeOnly">是否仅返回KeyCode，默认为true（仅返回KeyCode）</param>
    public void OnKeyDown(ScriptObject callback, bool useCodeOnly = true)
    {
        if (useCodeOnly)
            _keyDownCodeCallbacks.Add(callback);
        else
            _keyDownDataCallbacks.Add(callback);
    }
    
    /// <summary>
    /// 注册键盘释放事件回调
    /// </summary>
    /// <param name="callback">回调函数</param>
    /// <param name="useCodeOnly">是否仅返回KeyCode，默认为true（仅返回KeyCode）</param>
    public void OnKeyUp(ScriptObject callback, bool useCodeOnly = true)
    {
        if (useCodeOnly)
            _keyUpCodeCallbacks.Add(callback);
        else
            _keyUpDataCallbacks.Add(callback);
    }

    public void OnMouseDown(ScriptObject callback)
    {
        _mouseDownCallbacks.Add(callback);
    }

    public void OnMouseUp(ScriptObject callback)
    {
        _mouseUpCallbacks.Add(callback);
    }
    
    /// <summary>
    /// 注册鼠标移动事件回调
    /// </summary>
    /// <param name="callback">回调函数</param>
    /// <param name="interval">回调间隔时间（毫秒），默认200ms</param>
    public void OnMouseMove(ScriptObject callback, int interval = 200)
    {
        _mouseMoveCallbacks.Add(callback);
        _mouseMoveCallbackIntervals[callback] = interval;
        _lastMouseMoveCallbackTimes[callback] = DateTime.MinValue;
    }
    
    public void OnMouseWheel(ScriptObject callback)
    {
        _mouseWheelCallbacks.Add(callback);
    }
    
    public void RemoveAllListeners()
    {
        _keyDownDataCallbacks.Clear();
        _keyUpDataCallbacks.Clear();
        _keyDownCodeCallbacks.Clear();
        _keyUpCodeCallbacks.Clear();
        _mouseDownCallbacks.Clear();
        _mouseUpCallbacks.Clear();
        _mouseMoveCallbacks.Clear();
        _mouseWheelCallbacks.Clear();
        
        _mouseMoveCallbackIntervals.Clear();
        _lastMouseMoveCallbackTimes.Clear();
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _eventChannel.Writer.TryComplete();
            // 移除所有事件监听器
            if (_keyDownHandler != null)
            {
                AppHook.KeyDown -= _keyDownHandler;
                _keyDownHandler = null;
            }
            
            if (_keyUpHandler != null)
            {
                AppHook.KeyUp -= _keyUpHandler;
                _keyUpHandler = null;
            }
            
            if (_mouseDownExtHandler != null)
            {
                AppHook.MouseDownExt -= _mouseDownExtHandler;
                _mouseDownExtHandler = null;
            }
            
            if (_mouseUpExtHandler != null)
            {
                AppHook.MouseUpExt -= _mouseUpExtHandler;
                _mouseUpExtHandler = null;
            }
            
            if (_mouseMoveExtHandler != null)
            {
                AppHook.MouseMoveExt -= _mouseMoveExtHandler;
                _mouseMoveExtHandler = null;
            }
            
            if (_mouseWheelExtHandler != null)
            {
                AppHook.MouseWheelExt -= _mouseWheelExtHandler;
                _mouseWheelExtHandler = null;
            }
            
            // 清空回调列表
            RemoveAllListeners();
        }
    }
}