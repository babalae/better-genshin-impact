using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.BgiVision;

public class BvPage
{
    private static readonly ILogger Logger = App.GetLogger<BvPage>();
    private readonly CancellationToken _cancellationToken;

    public IKeyboardSimulator Keyboard => Simulation.SendInput.Keyboard;

    public IMouseSimulator Mouse => Simulation.SendInput.Mouse;

    /// <summary>
    /// Default timeout for operations in milliseconds
    /// </summary>
    public int DefaultTimeout { get; set; } = 10000;

    /// <summary>
    /// Default retry interval in milliseconds
    /// </summary>
    public int DefaultRetryInterval { get; set; } = 1000;

    public BvPage(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// 截图
    /// </summary>
    /// <returns></returns>
    public ImageRegion Screenshot()
    {
        return TaskControl.CaptureToRectArea();
    }

    /// <summary>
    /// 等待
    /// </summary>
    /// <param name="milliseconds"></param>
    /// <returns></returns>
    public async Task<BvPage> Wait(int milliseconds)
    {
        await TaskControl.Delay(milliseconds, _cancellationToken);
        return this;
    }

    /// <summary>
    /// 定位图片位置
    /// </summary>
    /// <param name="image"></param>
    /// <returns></returns>
    public BvLocator Locator(BvImage image)
    {
        return new BvLocator(image.ToRecognitionObject(), _cancellationToken);
    }

    /// <summary>
    /// 定位文本位置
    /// </summary>
    /// <param name="text"></param>
    /// <param name="rect"></param>
    /// <returns></returns>
    public BvLocator Locator(string text, Rect rect = default)
    {
        return Locator(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = rect,
            Text = text
        });
    }


    /// <summary>
    /// 定位 RecognitionObject 代表的位置
    /// </summary>
    /// <param name="ro"></param>
    /// <returns></returns>
    public BvLocator Locator(RecognitionObject ro)
    {
        return new BvLocator(ro, _cancellationToken);
    }

    public BvLocator GetByText(string text = "", Rect rect = default)
    {
        return Locator(text, rect);
    }

    public BvLocator GetByImage(BvImage image)
    {
        return Locator(image);
    }


    public List<Region> Ocr(Rect rect = default)
    {
        return Locator(string.Empty, rect).FindAll();
    }


    /// <summary>
    /// 1080P 分辨率下点击坐标
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public void Click(double x, double y)
    {
        GameCaptureRegion.GameRegion1080PPosClick(x, y);
    }

    /// <summary>
    /// 使用模糊匹配判断截图中是否包含目标文字。
    /// 通过 <see cref="OcrFactory.PaddleMatch"/> 自动选择最佳实现（DP 模糊匹配或普通 OCR + 字符串比较）。
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="rect">感兴趣区域，default 表示全屏</param>
    /// <param name="threshold">匹配阈值 (0~1)，null 使用配置中的默认阈值</param>
    /// <returns>是否匹配成功</returns>
    public bool OcrMatch(string target, Rect rect = default, double? threshold = null)
    {
        var matchService = OcrFactory.PaddleMatch;
        var actualThreshold = threshold
                              ?? TaskContext.Instance().Config.OtherConfig.OcrConfig.OcrMatchDefaultThreshold;

        var screen = TaskControl.CaptureToRectArea();
        try
        {
            var roi = rect == default ? null : screen.DeriveCrop(rect);
            try
            {
                var score = matchService.OcrMatch((roi ?? screen).SrcMat, target);
                return score >= actualThreshold;
            }
            finally
            {
                roi?.Dispose();
            }
        }
        finally
        {
            screen.Dispose();
        }
    }

    /// <summary>
    /// 重复截图并使用模糊匹配，等待目标文字出现。
    /// 超时返回 false 而非抛异常。
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="rect">感兴趣区域，default 表示全屏</param>
    /// <param name="threshold">匹配阈值 (0~1)，null 使用配置中的默认阈值</param>
    /// <param name="timeout">超时时间（毫秒），null 使用 DefaultTimeout</param>
    /// <returns>是否在超时前匹配成功</returns>
    public async Task<bool> WaitForOcrMatch(string target, Rect rect = default, double? threshold = null, int? timeout = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var retryCount = DefaultRetryInterval > 0 ? actualTimeout / DefaultRetryInterval : 1;

        return await NewRetry.WaitForAction(() => OcrMatch(target, rect, threshold),
            _cancellationToken, retryCount, DefaultRetryInterval);
    }
}
