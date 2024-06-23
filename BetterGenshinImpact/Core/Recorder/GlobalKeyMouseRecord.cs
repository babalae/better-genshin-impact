using System.Diagnostics;
using BetterGenshinImpact.Model;
using Gma.System.MouseKeyHook;
using System.Windows.Forms;

namespace BetterGenshinImpact.Core.Recorder;

public class GlobalKeyMouseRecord : Singleton<GlobalKeyMouseRecord>
{
    private KeyMouseRecorder? _recorder;

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
        // Debug.WriteLine($"KeyDown: {e.KeyCode}");
        _recorder?.KeyDown(e);
    }

    public void GlobalHookKeyUp(KeyEventArgs e)
    {
        // Debug.WriteLine($"KeyUp: {e.KeyCode}");
        _recorder?.KeyUp(e);
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
