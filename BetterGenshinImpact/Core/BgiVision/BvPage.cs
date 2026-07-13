using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
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
}