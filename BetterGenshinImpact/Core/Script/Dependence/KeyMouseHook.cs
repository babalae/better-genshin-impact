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
            // 调用KeyData回调
            foreach (var callback in _keyDownDataCallbacks)
            {
                try
                {
                    callback.InvokeAsFunction(args.KeyData.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行键盘按下事件回调时发生错误");
                    // 忽略回调执行异常，不影响其他回调
                }
            }
            
            // 调用KeyCode回调
            foreach (var callback in _keyDownCodeCallbacks)
            {
                try
                {
                    callback.InvokeAsFunction(args.KeyCode.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行键盘按下事件回调时发生错误");
                    // 忽略回调执行异常，不影响其他回调
                }
            }
        };
        
        _keyUpHandler = (_, args) =>
        {
            // 调用KeyData回调
            foreach (var callback in _keyUpDataCallbacks)
            {
                try
                {
                    callback.InvokeAsFunction(args.KeyData.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行键盘释放事件回调时发生错误");
                    // 忽略回调执行异常，不影响其他回调
                }
            }
            
            // 调用KeyCode回调
            foreach (var callback in _keyUpCodeCallbacks)
            {
                try
                {
                    callback.InvokeAsFunction(args.KeyCode.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行键盘释放事件回调时发生错误");
                    // 忽略回调执行异常，不影响其他回调
                }
            }
        };
        
        _mouseDownExtHandler = (_, args) =>
        {
            foreach (var callback in _mouseDownCallbacks)
            {
                try
                {
                    callback.InvokeAsFunction(args.Button.ToString(), args.X, args.Y);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行鼠标按下事件回调时发生错误");
                    // 忽略回调执行异常，不影响其他回调
                }
            }
        };
        
        _mouseUpExtHandler = (_, args) =>
        {
            foreach (var callback in _mouseUpCallbacks)
            {
                try
                {
                    callback.InvokeAsFunction(args.Button.ToString(), args.X, args.Y);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行鼠标释放事件回调时发生错误");
                    // 忽略回调执行异常，不影响其他回调
                }
            }
        };
        
        _mouseMoveExtHandler = (_, args) =>
        {
            foreach (var callback in _mouseMoveCallbacks)
            {
                try
                {
                    callback.InvokeAsFunction(args.X, args.Y);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行鼠标移动事件回调时发生错误");
                    // 忽略回调执行异常，不影响其他回调
                }
            }
        };
        
        _mouseWheelExtHandler = (_, args) =>
        {
            foreach (var callback in _mouseWheelCallbacks)
            {
                try
                {
                    callback.InvokeAsFunction(args.Delta, args.X, args.Y);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "执行鼠标滚轮事件回调时发生错误");
                    // 忽略回调执行异常，不影响其他回调
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
    
    public void OnMouseMove(ScriptObject callback)
    {
        _mouseMoveCallbacks.Add(callback);
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