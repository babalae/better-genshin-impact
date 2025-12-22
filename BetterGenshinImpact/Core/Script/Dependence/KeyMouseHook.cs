using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BetterGenshinImpact.Core.Monitor;
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
    
    private readonly ILogger<KeyMouseHook> _logger = App.GetLogger<KeyMouseHook>();
    
    public KeyMouseHook()
    {
        // 初始化事件处理程序
        _keyDownHandler = (_, args) =>
        {
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var keyDownDataCallbacksCopy = new List<ScriptObject>(_keyDownDataCallbacks);
            var keyDownCodeCallbacksCopy = new List<ScriptObject>(_keyDownCodeCallbacks);
            
            // 调用KeyData回调
            foreach (var callback in keyDownDataCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.KeyData.ToString());
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("V8 object has been released"))
                {
                    _logger.LogDebug("V8对象已释放，清除所有回调");
                    RemoveAllListeners();
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行键盘按下事件回调时发生错误");
                    // 忽略单个回调执行异常，不影响其他回调
                }
            }
            
            // 调用KeyCode回调
            foreach (var callback in keyDownCodeCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.KeyCode.ToString());
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("V8 object has been released"))
                {
                    _logger.LogDebug("V8对象已释放，清除所有回调");
                    RemoveAllListeners();
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行键盘按下事件回调时发生错误");
                    // 忽略单个回调执行异常，不影响其他回调
                }
            }
        };
        
        _keyUpHandler = (_, args) =>
        {
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var keyUpDataCallbacksCopy = new List<ScriptObject>(_keyUpDataCallbacks);
            var keyUpCodeCallbacksCopy = new List<ScriptObject>(_keyUpCodeCallbacks);
            
            // 调用KeyData回调
            foreach (var callback in keyUpDataCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.KeyData.ToString());
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("V8 object has been released"))
                {
                    _logger.LogDebug("V8对象已释放，清除所有回调");
                    RemoveAllListeners();
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行键盘释放事件回调时发生错误");
                    // 忽略单个回调执行异常，不影响其他回调
                }
            }
            
            // 调用KeyCode回调
            foreach (var callback in keyUpCodeCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.KeyCode.ToString());
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("V8 object has been released"))
                {
                    _logger.LogDebug("V8对象已释放，清除所有回调");
                    RemoveAllListeners();
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行键盘释放事件回调时发生错误");
                    // 忽略单个回调执行异常，不影响其他回调
                }
            }
        };
        
        _mouseDownExtHandler = (_, args) =>
        {
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var mouseDownCallbacksCopy = new List<ScriptObject>(_mouseDownCallbacks);
            
            foreach (var callback in mouseDownCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.Button.ToString(), args.X, args.Y);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("V8 object has been released"))
                {
                    _logger.LogDebug("V8对象已释放，清除所有回调");
                    RemoveAllListeners();
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行鼠标按下事件回调时发生错误");
                    // 忽略单个回调执行异常，不影响其他回调
                }
            }
        };
        
        _mouseUpExtHandler = (_, args) =>
        {
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var mouseUpCallbacksCopy = new List<ScriptObject>(_mouseUpCallbacks);
            
            foreach (var callback in mouseUpCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.Button.ToString(), args.X, args.Y);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("V8 object has been released"))
                {
                    _logger.LogDebug("V8对象已释放，清除所有回调");
                    RemoveAllListeners();
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行鼠标释放事件回调时发生错误");
                    // 忽略单个回调执行异常，不影响其他回调
                }
            }
        };
        
        _mouseMoveExtHandler = (_, args) =>
        {
            var now = DateTime.Now;
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var mouseMoveCallbacksCopy = new List<ScriptObject>(_mouseMoveCallbacks);
            
            foreach (var callback in mouseMoveCallbacksCopy)
            {
                try
                {
                    // 获取回调的间隔时间
                    if (_mouseMoveCallbackIntervals.TryGetValue(callback, out var interval))
                    {
                        // 获取上次调用时间
                        if (_lastMouseMoveCallbackTimes.TryGetValue(callback, out var lastTime))
                        {
                            // 计算时间差
                            var timeSpan = now - lastTime;
                            // 如果时间差大于等于间隔时间，则执行回调
                            if (timeSpan.TotalMilliseconds >= interval)
                            {
                                callback.InvokeAsFunction(args.X, args.Y);
                                // 更新上次调用时间
                                _lastMouseMoveCallbackTimes[callback] = now;
                            }
                        }
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("V8 object has been released"))
                {
                    _logger.LogDebug("V8对象已释放，清除所有回调");
                    RemoveAllListeners();
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行鼠标移动事件回调时发生错误");
                    // 忽略单个回调执行异常，不影响其他回调
                }
            }
        };
        
        _mouseWheelExtHandler = (_, args) =>
        {
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var mouseWheelCallbacksCopy = new List<ScriptObject>(_mouseWheelCallbacks);
            
            foreach (var callback in mouseWheelCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.Delta, args.X, args.Y);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("V8 object has been released"))
                {
                    _logger.LogDebug("V8对象已释放，清除所有回调");
                    RemoveAllListeners();
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行鼠标滚轮事件回调时发生错误");
                    // 忽略单个回调执行异常，不影响其他回调
                }
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