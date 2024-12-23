using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Recorder.Model;

[Serializable]
public class KeyMouseScript
{
    public List<MacroEvent> MacroEvents { get; set; } = [];
    public List<MacroEvent> MouseMoveByMacroEvents { get; set; } = [];
    public List<MacroEvent> MouseMoveToMacroEvents { get; set; } = [];
    public KeyMouseScriptInfo? Info { get; set; }

    /// <summary>
    /// 转换原始脚本为适应当前分辨率的脚本
    /// </summary>
    public void Adapt(RECT captureRect, double dpiScale)
    {
        foreach (var macroEvent in MacroEvents)
        {
            if (Info == null || Info.Width == 0 || Info.Height == 0)
            {
                Debug.WriteLine("错误的脚本数据 Info.Width == 0 || Info.Height == 0");
                break;
            }

            if (macroEvent.Type == MacroEventType.MouseMoveTo
                || macroEvent.Type == MacroEventType.MouseDown
                || macroEvent.Type == MacroEventType.MouseUp)
            {
                macroEvent.MouseX = (int)(captureRect.X + (macroEvent.MouseX - Info.X) * captureRect.Width * 1d / Info.Width);
                macroEvent.MouseY = (int)(captureRect.Y + (macroEvent.MouseY - Info.Y) * captureRect.Height * 1d / Info.Height);
            }
            else if (macroEvent.Type == MacroEventType.MouseMoveBy)
            {
                macroEvent.MouseX = (int)Math.Round(macroEvent.MouseX / Info.RecordDpi * dpiScale, 0);
                macroEvent.MouseY = (int)Math.Round(macroEvent.MouseY / Info.RecordDpi * dpiScale, 0);
            }
        }
    }

    public void Merge()
    {
        // 合并鼠标移动事件
        const int mergedEventTimeMax = 20;
        var mergedMacroEvents = new List<MacroEvent>();
        MacroEvent? currentMerge = null;
        foreach (var macroEvent in MacroEvents)
        {
            if (currentMerge == null)
            {
                currentMerge = macroEvent;
                continue;
            }

            if (currentMerge.Type != macroEvent.Type)
            {
                mergedMacroEvents.Add(currentMerge);
                currentMerge = macroEvent;
                continue;
            }

            switch (macroEvent.Type)
            {
                case MacroEventType.MouseMoveTo:
                    // 控制合并时间片段长度
                    if (macroEvent.Time - currentMerge.Time > mergedEventTimeMax)
                    {
                        mergedMacroEvents.Add(currentMerge);
                        currentMerge = macroEvent;
                        break;
                    }

                    // 合并为最后一个事件的位置，避免丢步
                    currentMerge.MouseX = macroEvent.MouseX;
                    currentMerge.MouseY = macroEvent.MouseY;
                    break;

                case MacroEventType.MouseMoveBy:
                    if (macroEvent.Time - currentMerge.Time > 10)
                    {
                        mergedMacroEvents.Add(currentMerge);
                        currentMerge = macroEvent;
                        break;
                    }

                    // 相对位移量相加
                    currentMerge.MouseX += macroEvent.MouseX;
                    currentMerge.MouseY += macroEvent.MouseY;
                    if (macroEvent.CameraOrientation != null)
                    {
                        currentMerge.CameraOrientation = macroEvent.CameraOrientation;
                    }

                    break;

                default:
                    mergedMacroEvents.Add(currentMerge);
                    mergedMacroEvents.Add(macroEvent);
                    currentMerge = null;
                    break;
            }
        }

        MacroEvents = mergedMacroEvents;
    }
}