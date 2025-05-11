using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Linq;
using System.Windows.Forms;

namespace BetterGenshinImpact.GameTask.QuickTeleport;

internal class QuickTeleportTrigger : ITaskTrigger
{
    public string Name => "快速传送";
    public bool IsEnabled { get; set; }
    public int Priority => 21;
    public bool IsExclusive { get; set; }

    private readonly QuickTeleportAssets _assets;

    private DateTime _prevClickOptionButtonTime = DateTime.MinValue;

    // private DateTime _prevClickTeleportButtonTime = DateTime.MinValue;
    private DateTime _prevExecute = DateTime.MinValue;

    private readonly QuickTeleportConfig _config;
    private readonly HotKeyConfig _hotkeyConfig;

    public QuickTeleportTrigger()
    {
        _assets = QuickTeleportAssets.Instance;
        _config = TaskContext.Instance().Config.QuickTeleportConfig;
        _hotkeyConfig = TaskContext.Instance().Config.HotKeyConfig;
    }

    public void Init()
    {
        IsEnabled = _config.Enabled;
        IsExclusive = false;
    }

    public void OnCapture(CaptureContent content)
    {
        if ((DateTime.Now - _prevExecute).TotalMilliseconds <= 300)
        {
            return;
        }

        IsExclusive = false;

        // 快捷键传送配置启用的情况下，且快捷键按下的时候激活
        if (_config.HotkeyTpEnabled && !string.IsNullOrEmpty(_hotkeyConfig.QuickTeleportTickHotkey))
        {
            if (!IsHotkeyPressed())
            {
                return;
            }
        }

        _prevExecute = DateTime.Now;

        // 1.判断是否在地图界面
        content.CaptureRectArea.Find(_assets.MapScaleButtonRo, _ =>
        {
            IsExclusive = true;

            // 2. 判断是否有传送按钮
            var hasTeleportButton = CheckTeleportButton(content.CaptureRectArea);

            if (!hasTeleportButton)
            {
                // 存在地图关闭按钮，说明未选中传送点，直接返回
                var mapCloseRa = content.CaptureRectArea.Find(_assets.MapCloseButtonRo);
                if (!mapCloseRa.IsEmpty())
                {
                    return;
                }

                // 存在地图选择按钮，说明未选中传送点，直接返回
                var mapChooseRa = content.CaptureRectArea.Find(_assets.MapChooseRo);
                if (!mapChooseRa.IsEmpty())
                {
                    return;
                }

                // 3. 循环判断选项列表是否有传送点
                var hasMapChooseIcon = CheckMapChooseIcon(content);
                if (hasMapChooseIcon)
                {
                    TaskControl.Sleep(_config.WaitTeleportPanelDelay);
                    CheckTeleportButton(TaskControl.CaptureToRectArea(forceNew: true));
                }
            }
        });
    }

    private bool CheckTeleportButton(ImageRegion imageRegion)
    {
        var hasTeleportButton = false;
        imageRegion.Find(_assets.TeleportButtonRo, ra =>
        {
            ra.Click();
            hasTeleportButton = true;
            // if ((DateTime.Now - _prevClickTeleportButtonTime).TotalSeconds > 1)
            // {
            //     TaskControl.Logger.LogInformation("快速传送：传送");
            // }
            // _prevClickTeleportButtonTime = DateTime.Now;
        });
        return hasTeleportButton;
    }

    /// <summary>
    /// 全匹配一遍并进行文字识别
    /// 60ms ~200ms
    /// </summary>
    /// <param name="content"></param>
    /// <returns></returns>
    private bool CheckMapChooseIcon(CaptureContent content)
    {
        var hasMapChooseIcon = false;

        // 全匹配一遍
        var rResultList = MatchTemplateHelper.MatchMultiPicForOnePic(content.CaptureRectArea.CacheGreyMat[_assets.MapChooseIconRoi], _assets.MapChooseIconGreyMatList);
        // 按高度排序
        if (rResultList.Count > 0)
        {
            rResultList = [.. rResultList.OrderBy(x => x.Y)];
            // 点击最高的
            foreach (var iconRect in rResultList)
            {
                // 200宽度的文字区域
                using var ra = content.CaptureRectArea.DeriveCrop(_assets.MapChooseIconRoi.X + iconRect.X + iconRect.Width, _assets.MapChooseIconRoi.Y + iconRect.Y - 8, 200, iconRect.Height + 16);
                using var textRegion = ra.Find(new RecognitionObject
                {
                    // RecognitionType = RecognitionTypes.Ocr,
                    RecognitionType = RecognitionTypes.ColorRangeAndOcr,
                    LowerColor = new Scalar(249, 249, 249),  // 只取白色文字
                    UpperColor = new Scalar(255, 255, 255),
                });
                if (string.IsNullOrEmpty(textRegion.Text) || textRegion.Text.Length == 1)
                {
                    continue;
                }

                if ((DateTime.Now - _prevClickOptionButtonTime).TotalMilliseconds > 500)
                {
                    TaskControl.Logger.LogInformation("快速传送：点击 {Option}", textRegion.Text.Replace(">", ""));
                }

                _prevClickOptionButtonTime = DateTime.Now;
                TaskControl.Sleep(_config.TeleportListClickDelay);
                ra.Click();
                hasMapChooseIcon = true;
                break;
            }
        }

        // List<RectArea> raResultList = new();
        // foreach (var ro in _assets.MapChooseIconRoList)
        // {
        //     var ra = content.CaptureRectArea.Find(ro);
        //     if (!ra.IsEmpty())
        //     {
        //         var text = GetOptionText(content.CaptureRectArea.SrcGreyMat, ra, 200);
        //         if (string.IsNullOrEmpty(text) || text.Length == 1)
        //         {
        //             continue;
        //         }
        //
        //         if ((DateTime.Now - _prevClickOptionButtonTime).TotalMilliseconds > 500)
        //         {
        //             TaskControl.Logger.LogInformation("快速传送：点击 {Option}", text);
        //         }
        //
        //         _prevClickOptionButtonTime = DateTime.Now;
        //         TaskControl.Sleep(_config.TeleportListClickDelay);
        //         raResultList.Add(ra);
        //     }
        // }

        // if (raResultList.Count > 0)
        // {
        //     var highest = raResultList[0];
        //     foreach (var ra in raResultList)
        //     {
        //         if (ra.Y < highest.Y)
        //         {
        //             highest = ra;
        //         }
        //     }
        //
        //     highest.ClickCenter();
        //     hasMapChooseIcon = true;
        //
        //     foreach (var ra in raResultList)
        //     {
        //         ra.Dispose();
        //     }
        // }

        return hasMapChooseIcon;
    }

    // /// <summary>
    // /// 获取选项的文字
    // /// </summary>
    // /// <param name="captureMat"></param>
    // /// <param name="foundIconRect"></param>
    // /// <param name="chatOptionTextWidth"></param>
    // /// <returns></returns>
    // [Obsolete]
    // private string GetOptionText(Mat captureMat, Rect foundIconRect, int chatOptionTextWidth)
    // {
    //     var textRect = new Rect(foundIconRect.X + foundIconRect.Width, foundIconRect.Y, chatOptionTextWidth, foundIconRect.Height);
    //     using var mat = new Mat(captureMat, textRect);
    //     var text = OcrFactory.Paddle.Ocr(mat);
    //     return text;
    // }

    private bool IsHotkeyPressed()
    {
        if (HotKey.IsMouseButton(_hotkeyConfig.QuickTeleportTickHotkey))
        {
            if (MouseHook.AllMouseHooks.TryGetValue((MouseButtons)Enum.Parse(typeof(MouseButtons), _hotkeyConfig.QuickTeleportTickHotkey), out var mouseHook))
            {
                if (mouseHook.IsPressed)
                {
                    return true;
                }
            }
        }
        else
        {
            if (KeyboardHook.AllKeyboardHooks.TryGetValue((Keys)Enum.Parse(typeof(Keys), _hotkeyConfig.QuickTeleportTickHotkey), out var keyboardHook))
            {
                if (keyboardHook.IsPressed)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
