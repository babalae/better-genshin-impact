using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using Microsoft.Extensions.Localization;
using System.Globalization;
using BetterGenshinImpact.GameTask.Model.Area;
using System.Collections.Generic;
using Fischless.WindowsInput;
using OpenCvSharp;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Core.Recognition.OCR;
using System.IO;
using System.Drawing;
using static Vanara.PInvoke.Gdi32;
using OpenCvSharp.Extensions;
using BetterGenshinImpact.GameTask.Model.GameUI;

namespace BetterGenshinImpact.GameTask.GetGridIcons;

/// <summary>
/// 获取Grid界面的物品图标
/// </summary>
public class GetGridIconsTask : ISoloTask
{
    private readonly ILogger logger = App.GetLogger<GetGridIconsTask>();
    private readonly InputSimulator input = Simulation.SendInput;
    private readonly ReturnMainUiTask _returnMainUiTask = new();

    private CancellationToken ct;

    public string Name => "获取Grid界面物品图标独立任务";

    private readonly int? maxNumToGet;

    private readonly GridScreenName gridScreenName;

    public GetGridIconsTask(GridScreenName gridScreenName, int? maxNumToGet = null)
    {
        this.gridScreenName = gridScreenName;
        this.maxNumToGet = maxNumToGet;
        IStringLocalizer<GetGridIconsTask> stringLocalizer = App.GetService<IStringLocalizer<GetGridIconsTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
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
        HashSet<string> itemNames = new HashSet<string>();
        await foreach (ImageRegion itemRegion in gridScreen)
        {
            itemRegion.Click();
            await Delay(300, ct);

            using var ra1 = CaptureToRectArea();
            using ImageRegion nameRegion = ra1.DeriveCrop(new Rect((int)(ra1.Width * 0.682), (int)(ra1.Width * 0.0625), (int)(ra1.Width * 0.256), (int)(ra1.Width * 0.03125)));
            var ocrResult = OcrFactory.Paddle.OcrResult(nameRegion.SrcMat);
            string itemName = ocrResult.Text;
            if (itemNames.Add(itemName))
            {
                string filePath = Path.Combine(directory, $"{itemName}.png");
                Thread saveThread = new Thread(() =>
                {
                    try
                    {
                        using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            itemRegion.SrcMat.ToBitmap().Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        logger.LogInformation("图片保存成功：{Text}", itemName);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "图片保存失败：{Text}", itemName);
                    }
                });
                saveThread.IsBackground = true; // 设置为后台线程
                saveThread.Start();
            }
            else
            {
                logger.LogInformation("重复的物品：{Text}", itemName);
            }

            count--;
            if (count <= 0)
            {
                logger.LogInformation("检查次数已耗尽");
                break;
            }
        }
    }
}