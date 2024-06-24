using System.Collections.Generic;
using System.Diagnostics;
using BetterGenshinImpact.Model;
using Gma.System.MouseKeyHook;
using System.Windows.Forms;

namespace BetterGenshinImpact.Core.Recorder;

public class GlobalKeyMouseRecord : Singleton<GlobalKeyMouseRecord>
{
    private KeyMouseRecorder? _recorder;

    private readonly Dictionary<Keys, bool> _keyDownState = new();

    public KeyMouseRecorder StartRecord()
    {
        _recorder = new KeyMouseRecorder();
        return _recorder;
    }

    public string StopRecord()
    {
        var macro = _recorder?.ToJsonMacro() ?? string.Empty;
        _recorder = null;
        return macro;
    }

    public void GlobalHookKeyDown(KeyEventArgs e)
    {
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
        Debug.WriteLine($"KeyDown: {e.KeyCode}");
        _recorder?.KeyDown(e);
    }

    public void GlobalHookKeyUp(KeyEventArgs e)
    {
        if (_keyDownState.ContainsKey(e.KeyCode) && _keyDownState[e.KeyCode])
        {
            Debug.WriteLine($"KeyUp: {e.KeyCode}");
            _keyDownState[e.KeyCode] = false;
            _recorder?.KeyUp(e);
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

    public void GlobalHookMouseMove(MouseEventExtArgs e)
    {
        // Debug.WriteLine($"MouseMove: {e.X}, {e.Y}");
        _recorder?.MouseMove(e);
    }
}
