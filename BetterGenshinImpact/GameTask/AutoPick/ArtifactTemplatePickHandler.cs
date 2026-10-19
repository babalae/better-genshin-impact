using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 圣遗物名称模板拾取。截图已是 1080P：名称区相对 F 裁剪；当前行不是圣遗物时节流下翻列表。
/// </summary>
public sealed class ArtifactTemplatePickHandler
{
    /// <summary>相对 F 左上角，1080P 物品名起始（对齐 AutoPick ItemTextLeftOffset）</summary>
    private const int NameLeftFromF1080 = 115;

    private const int NameWidth1080 = 260;
    private const int NameYPad1080 = 8;
    private const int DuplicateYThreshold = 20;
    private const int DuplicateDelayMs = 160;
    private const int PickupDelayMs = 80;
    private const int ScrollIntervalMs = 140;

    private IReadOnlyList<ArtifactPickTemplate>? _templates;
    private bool _loadFailed;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _lastScrollMs;
    private long _lastPickupMs;
    private int _lastCenterY;
    private string _lastItemName = string.Empty;

    public void Reset()
    {
        if (_templates != null)
        {
            foreach (var item in _templates)
            {
                item.Recognition.TemplateImageMat?.Dispose();
                item.Recognition.TemplateImageGreyMat?.Dispose();
            }
        }

        _templates = null;
        _loadFailed = false;
        _lastScrollMs = 0;
        _lastPickupMs = 0;
        _lastCenterY = 0;
        _lastItemName = string.Empty;
    }

    public void OnNoPickKey(CaptureContent content, Func<ImageRegion, bool> hasScrollIcon)
    {
        EnsureTemplates();
        if (!hasScrollIcon(content.CaptureRectArea))
        {
            return;
        }

        ScrollDownIfDue();
    }

    public void OnPickKeyFound(
        CaptureContent content,
        AutoPickAssets pickAssets,
        Region foundPickKey,
        Action<CaptureContent, string> logPick)
    {
        EnsureTemplates();
        var now = _clock.ElapsedMilliseconds;
        if (now - _lastPickupMs < PickupDelayMs)
        {
            return;
        }

        try
        {
            if (_templates is { Count: > 0 })
            {
                var nameRect = BuildNameRect(foundPickKey, content.CaptureRectArea);
                if (nameRect is { } rect)
                {
                    using var nameRegion = content.CaptureRectArea.DeriveCrop(rect);
                    var itemName = MatchFirst(nameRegion);
                    if (!string.IsNullOrEmpty(itemName))
                    {
                        var centerY = foundPickKey.Y + foundPickKey.Height / 2;
                        if (Math.Abs(_lastCenterY - centerY) <= DuplicateYThreshold && _lastItemName == itemName)
                        {
                            _lastCenterY = -20;
                            _lastItemName = string.Empty;
                            _lastPickupMs = now + DuplicateDelayMs - PickupDelayMs;
                            ScrollDownIfDue();
                            return;
                        }

                        logPick(content, itemName);
                        InputHub.Foreground.Keyboard.KeyPress(pickAssets.PickVk);
                        _lastCenterY = centerY;
                        _lastItemName = itemName;
                        _lastPickupMs = now;
                        return;
                    }
                }
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "圣遗物模板匹配异常");
        }

        _lastItemName = string.Empty;
        ScrollDownIfDue();
    }

    private void EnsureTemplates()
    {
        if (_templates != null || _loadFailed)
        {
            return;
        }

        try
        {
            _templates = ArtifactPickTemplateLoader.Load();
            TaskControl.Logger.LogInformation("圣遗物拾取已启用，加载模板 {Count} 个（当前行不是圣遗物时会滚动列表）", _templates.Count);
            if (_templates.Count == 0)
            {
                TaskControl.Logger.LogWarning("没有可用的圣遗物名称模板，将只滚动拾取列表");
            }
        }
        catch (Exception e)
        {
            _loadFailed = true;
            TaskControl.Logger.LogError(e, "加载圣遗物拾取模板失败");
            _templates = [];
        }
    }

    private string? MatchFirst(ImageRegion nameRegion)
    {
        if (_templates == null)
        {
            return null;
        }

        foreach (var item in _templates)
        {
            var grey = item.Recognition.TemplateImageGreyMat;
            if (grey is { Width: > 0 } &&
                (grey.Width > nameRegion.SrcMat.Width || grey.Height > nameRegion.SrcMat.Height))
            {
                continue;
            }

            using var hit = nameRegion.Find(item.Recognition);
            if (hit.IsExist())
            {
                return item.ItemName;
            }
        }

        return null;
    }

    private void ScrollDownIfDue()
    {
        var now = _clock.ElapsedMilliseconds;
        if (now - _lastScrollMs < ScrollIntervalMs)
        {
            return;
        }

        _lastScrollMs = now;
        // JS「滚轮下翻」为 mouseY=-120，对应 VerticalScroll(-1)，把 F 移到下一行
        InputHub.Foreground.Mouse.VerticalScroll(-1);
    }

    private static Rect? BuildNameRect(Region fKey, ImageRegion capture)
    {
        var x = fKey.X + NameLeftFromF1080;
        var y = fKey.Y - NameYPad1080;
        var w = NameWidth1080;
        var h = fKey.Height + NameYPad1080 * 2;
        var mat = capture.SrcMat;
        if (x >= mat.Width || y >= mat.Height)
        {
            return null;
        }

        if (x < 0)
        {
            w += x;
            x = 0;
        }

        if (y < 0)
        {
            h += y;
            y = 0;
        }

        w = Math.Min(w, mat.Width - x);
        h = Math.Min(h, mat.Height - y);
        if (w < 24 || h < 12)
        {
            return null;
        }

        return new Rect(x, y, w, h);
    }
}
