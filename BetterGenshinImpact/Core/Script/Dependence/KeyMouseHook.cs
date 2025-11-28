using System;
using System.Windows.Forms;
using BetterGenshinImpact.Core.Monitor;
using Gma.System.MouseKeyHook;
using Microsoft.ClearScript;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class KeyMouseHook: IDisposable
{
    private IKeyboardMouseEvents? _appEvents;
    private IKeyboardMouseEvents AppHook => _appEvents ??= MouseKeyMonitor.GlobalHook;
    
    private KeyEventHandler? _keyDownHandler;
    private KeyEventHandler? _keyUpHandler;
    private MouseEventHandler? _mouseDownHandler;
    private MouseEventHandler? _mouseUpHandler;
    private MouseEventHandler? _mouseMoveHandler;
    private MouseEventHandler? _mouseWheelHandler;
    
    public void OnKeyDown(ScriptObject callback)
    {
        if (_keyDownHandler != null)
            AppHook.KeyDown -= _keyDownHandler;
        _keyDownHandler = (_, args) =>
            callback.InvokeAsFunction(args.KeyData.ToString());
        AppHook.KeyDown += _keyDownHandler;
    }

    public void OnKeyUp(ScriptObject callback)
    {
        callback.InvokeAsFunction("Test");
        if (_keyUpHandler != null)
            AppHook.KeyUp -= _keyUpHandler;
        _keyUpHandler = (_, args) =>
            callback.InvokeAsFunction(args.KeyCode.ToString());
        AppHook.KeyUp += _keyUpHandler;
    }

    public void OnMouseDown(ScriptObject callback)
    {
        if (_mouseDownHandler != null)
            AppHook.MouseDown -= _mouseDownHandler;
        _mouseDownHandler = (_, args) =>
            callback.InvokeAsFunction(args.Button.ToString(), args.X, args.Y);
        AppHook.MouseDown += _mouseDownHandler;
    }

    public void OnMouseUp(ScriptObject callback)
    {
        if (_mouseUpHandler != null)
            AppHook.MouseUp -= _mouseUpHandler;
        _mouseUpHandler = (_, args) =>
            callback.InvokeAsFunction(args.Button.ToString(), args.X, args.Y);
        AppHook.MouseUp += _mouseUpHandler;
    }
    
    public void OnMouseMove(ScriptObject callback)
    {
        if (_mouseMoveHandler != null)
            AppHook.MouseMove -= _mouseMoveHandler;
        _mouseMoveHandler = (_, args) =>
            callback.InvokeAsFunction(args.X, args.Y);
        AppHook.MouseMove += _mouseMoveHandler;
    }
    
    public void OnMouseWheel(ScriptObject callback)
    {
        if (_mouseWheelHandler != null)
            AppHook.MouseWheel -= _mouseWheelHandler;
        _mouseWheelHandler = (_, args) =>
            callback.InvokeAsFunction(args.Delta, args.X, args.Y);
        AppHook.MouseWheel += _mouseWheelHandler;
    }
    
    public void Dispose()
    {
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
        
        if (_mouseDownHandler != null)
        {
            AppHook.MouseDown -= _mouseDownHandler;
            _mouseDownHandler = null;
        }
        
        if (_mouseUpHandler != null)
        {
            AppHook.MouseUp -= _mouseUpHandler;
            _mouseUpHandler = null;
        }
        
        if (_mouseMoveHandler != null)
        {
            AppHook.MouseMove -= _mouseMoveHandler;
            _mouseMoveHandler = null;
        }
        
        if (_mouseWheelHandler != null)
        {
            AppHook.MouseWheel -= _mouseWheelHandler;
            _mouseWheelHandler = null;
        }
    }
}