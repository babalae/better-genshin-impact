using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Recorder.Model;

[Serializable]
public class KeyMouseScript
{
    public List<MacroEvent> MacroEvents { get; set; } = [];
    public KeyMouseScriptInfo? Info { get; set; }

    /// <summary>
    /// 转换原始脚本为适应当前分辨率的脚本
    /// </summary>
    public void Adapt(RECT captureRect, double dpiScale)
    {
        foreach (var macroEvent in MacroEvents)
        {
            if (macroEvent.Type == MacroEventType.MouseMoveTo
                || macroEvent.Type == MacroEventType.MouseDown
                || macroEvent.Type == MacroEventType.MouseUp)
            {
                if (Info == null || Info.Width == 0 || Info.Height == 0)
                {
                    Debug.WriteLine("错误的脚本数据 Info.Width == 0 || Info.Height == 0");
                    break;
                }

                macroEvent.MouseX = (int)(captureRect.X + (macroEvent.MouseX - Info.X) * captureRect.Width * 1d / Info.Width);
                macroEvent.MouseY = (int)(captureRect.Y + (macroEvent.MouseY - Info.Y) * captureRect.Height * 1d / Info.Height);
            }
            else if (macroEvent.Type == MacroEventType.MouseMoveBy)
            {
                macroEvent.MouseX = (int)Math.Round(macroEvent.MouseX * dpiScale, 0);
                macroEvent.MouseY = (int)Math.Round(macroEvent.MouseY * dpiScale, 0);
            }
        }
    }
}
