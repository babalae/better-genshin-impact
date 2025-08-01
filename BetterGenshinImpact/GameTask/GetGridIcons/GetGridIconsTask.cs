using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using Microsoft.Extensions.Localization;
using BetterGenshinImpact.GameTask.Model.Area;
using System.Collections.Generic;
using OpenCvSharp;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR;
using System.IO;
using OpenCvSharp.Extensions;
using BetterGenshinImpact.GameTask.Model.GameUI;

namespace BetterGenshinImpact.GameTask.GetGridIcons;

/// <summary>
/// 获取Grid界面的物品图标
/// </summary>
public class GetGridIconsTask : ISoloTask
{
    private readonly ILogger logger = App.GetLogger<GetGridIconsTask>();

    private CancellationToken ct;

    public string Name => "获取Grid界面物品图标独立任务";

    private readonly int? maxNumToGet;

    private readonly GridScreenName gridScreenName;

    private readonly bool starAsSuffix;

    public GetGridIconsTask(GridScreenName gridScreenName, bool starAsSuffix, int? maxNumToGet = null)
    {
        this.gridScreenName = gridScreenName;
        this.starAsSuffix = starAsSuffix;
        this.maxNumToGet = maxNumToGet;
        IStringLocalizer<GetGridIconsTask> stringLocalizer = App.GetService<IStringLocalizer<GetGridIconsTask>>() ?? throw new NullReferenceException();
    }

    public async Task Start(CancellationToken ct)
    {
        this.ct = ct;

        using var ra0 = CaptureToRectArea();
        GridScreenParams gridParams = GridScreenParams.Templates[this.gridScreenName];
        Rect gridRoi = gridParams.GetRect(ra0);

        int count = this.maxNumToGet ?? int.MaxValue;

        string directory = Path.Combine(AppContext.BaseDirectory, "log/gridIcons", DateTime.Now.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(directory);

        GridScreen gridScreen = new GridScreen(gridRoi, gridParams, this.logger, this.ct);
        HashSet<string> fileNames = new HashSet<string>();
        await foreach (ImageRegion itemRegion in gridScreen)
        {
            itemRegion.Click();
            await Delay(300, ct);

            using var ra1 = CaptureToRectArea();
            using ImageRegion nameRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.682), (int)(ra1.Width * 0.0625), (int)(ra1.Width * 0.256), (int)(ra1.Width * 0.03125)));
            var ocrResult = OcrFactory.Paddle.OcrResult(nameRegion.SrcMat);
            string itemName = ocrResult.Text;
            string itemStar = "";
            if (this.starAsSuffix)
            {
                using ImageRegion starRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.682), (int)(ra1.Width * 0.1823), (int)(ra1.Width * 0.105), (int)(ra1.Width * 0.02345)));
                itemStar = String.Join(string.Empty, Enumerable.Repeat("★", GetStars(starRegion.SrcMat)));
            }

            string fileName = itemName + itemStar;
            if (fileNames.Add(fileName))
            {
                string filePath = Path.Combine(directory, $"{fileName}.png");
                Thread saveThread = new Thread(() =>
                {
                    try
                    {
                        using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            itemRegion.SrcMat.ToBitmap().Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        logger.LogInformation("图片保存成功：{Text}", fileName);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "图片保存失败：{Text}", fileName);
                    }
                });
                saveThread.IsBackground = true; // 设置为后台线程
                saveThread.Start();
            }
            else
            {
                logger.LogInformation("重复的物品：{Text}", fileName);
            }

            count--;
            if (count <= 0)
            {
                logger.LogInformation("检查次数已耗尽");
                break;
            }
        }
    }

    /// <summary>
    /// OCR检测★字符很不稳定，因此用cv
    /// 非常简陋的色彩检测，请传入聚焦的图像，勿带入可能的干扰
    /// </summary>
    /// <param name="mat"></param>
    /// <returns></returns>
    public static int GetStars(Mat mat)
    {
        Scalar yellowLower = new Scalar(50 - 5, 204 - 5, 255 - 5);
        Scalar yellowUpper = new Scalar(50 + 5, 204 + 5, 255 + 0);
        using Mat mask = mat.InRange(yellowLower, yellowUpper);
        var contours = mask.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        return contours?.Length ?? 0;
    }
}