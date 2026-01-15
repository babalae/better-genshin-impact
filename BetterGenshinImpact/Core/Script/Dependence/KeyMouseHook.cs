using System;
using System.Collections.Generic;
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
                    callback.InvokeAsFunction(args.KeyCode.ToString());
                }
                catch (Exception ex)
                {
                    HandleCallbackException(ex, "键盘按下事件");
                    return;
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
                    callback.InvokeAsFunction(args.KeyCode.ToString());
                }
                catch (Exception ex)
                {
                    HandleCallbackException(ex, "键盘释放事件");
                    return;
                }
            }
        };
        
        _mouseDownExtHandler = (_, args) =>
        {
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var mouseDownCallbacksCopy = new List<ScriptObject>(_mouseDownCallbacks);
            
            // 转换为局部坐标
            var (localX, localY) = ConvertToLocalCoordinates(args.X, args.Y);
            
            foreach (var callback in mouseDownCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.Button.ToString(), localX, localY);
                }
                catch (Exception ex)
                {
                    HandleCallbackException(ex, "鼠标按下事件");
                    return;
                }
            }
        };
        
        _mouseUpExtHandler = (_, args) =>
        {
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var mouseUpCallbacksCopy = new List<ScriptObject>(_mouseUpCallbacks);
            
            // 转换为局部坐标
            var (localX, localY) = ConvertToLocalCoordinates(args.X, args.Y);
            
            foreach (var callback in mouseUpCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.Button.ToString(), localX, localY);
                }
                catch (Exception ex)
                {
                    HandleCallbackException(ex, "鼠标释放事件");
                    return;
                }
            }
        };
        
        _mouseMoveExtHandler = (_, args) =>
        {
            var now = DateTime.Now;
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var mouseMoveCallbacksCopy = new List<ScriptObject>(_mouseMoveCallbacks);
            
            // 转换为局部坐标
            var (localX, localY) = ConvertToLocalCoordinates(args.X, args.Y);
            
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
                                callback.InvokeAsFunction(localX, localY);
                                // 更新上次调用时间
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
        };
        
        _mouseWheelExtHandler = (_, args) =>
        {
            // 创建回调列表的副本，避免迭代期间修改集合导致异常
            var mouseWheelCallbacksCopy = new List<ScriptObject>(_mouseWheelCallbacks);
            
            // 转换为局部坐标
            var (localX, localY) = ConvertToLocalCoordinates(args.X, args.Y);
            
            foreach (var callback in mouseWheelCallbacksCopy)
            {
                try
                {
                    callback.InvokeAsFunction(args.Delta, localX, localY);
                }
                catch (Exception ex)
                {
                    HandleCallbackException(ex, "鼠标滚轮事件");
                    return;
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