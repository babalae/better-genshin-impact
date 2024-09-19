using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using Vanara.PInvoke;
using static System.Net.Mime.MediaTypeNames;

namespace BetterGenshinImpact.GameTask.AutoEat;

public class AutoEatTrigger : ITaskTrigger
{
    public string Name => "自动吃药";

    public bool IsEnabled { get; set; }

    public int Priority => 10;

    public bool IsExclusive { get; set; }

    private int _frameIndex = 0;

    private User32.VK _pickVk = User32.VK.VK_Z;
    private readonly ILogger<AutoPickTrigger> _logger = App.GetLogger<AutoPickTrigger>();

    private DateTime _lastExecutionTime = DateTime.MinValue; // 记录上次执行的时间
    public int IntervalMs = 500;  // 500ms 的执行间隔

    public void Init()
    {
        IsEnabled = TaskContext.Instance().Config.AutoEatConfig.Enabled;
        IntervalMs = TaskContext.Instance().Config.AutoEatConfig.IntervalMs;
        IsExclusive = false;
    }

    public void OnCapture(CaptureContent content)
    {
        var now = DateTime.Now;
        // 判断是否已经超过了 500ms 的间隔
        if ((now - _lastExecutionTime).TotalMilliseconds < IntervalMs)
        {
            return;
        }
        // 获取位图对象
        var bitmap = content.SrcBitmap;

        // 获取 (808, 1010) 位置的像素颜色
        var pixelColor = bitmap.GetPixel(808, 1010);

        // 判断颜色是否是 (255, 90, 90)
        if (pixelColor.R == 255 && pixelColor.G == 90 && pixelColor.B == 90)
        {
            // 模拟按键 "Z"
            Simulation.SendInput.Keyboard.KeyPress(_pickVk);
            _logger.LogInformation("按Z吃药");
            _lastExecutionTime = now;
            // TODO 吃饱了会一直吃
        }
        else
        {
            // _logger.LogInformation("识别的颜色 R:{R} G:{G} B:{B}",pixelColor.R, pixelColor.G, pixelColor.B);
        }
    }

    public void start()
    {
        // 帧序号自增 1分钟后归零(MaxFrameIndexSecond)
        _frameIndex = (_frameIndex + 1) % (int)(CaptureContent.MaxFrameIndexSecond * 1000d / 50);
        var bitmap = TaskTriggerDispatcher.GlobalGameCapture.Capture();
        if (bitmap == null)
        {
            _logger.LogWarning("截图失败!");
            return;
        }
        // 循环执行所有触发器 有独占状态的触发器的时候只执行独占触发器
        var content = new CaptureContent(bitmap, _frameIndex, 50);
        OnCapture(content);
    }
}
