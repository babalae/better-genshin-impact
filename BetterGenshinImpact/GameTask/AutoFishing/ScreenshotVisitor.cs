using System;
using System.IO;
using System.Threading.Tasks;
using CsTrees;
using CsTrees.Visitors;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoFishing;

/// <summary>
/// CsTrees Visitor：在行为结束时自动截图。
/// 通过 Blackboard 获取当前帧，在行为的终态（Success/Failure）时保存截图。
/// </summary>
public class ScreenshotVisitor : VisitorBase
{
    private readonly ILogger _logger;

    public ScreenshotVisitor(ILogger logger) : base(full: false)
    {
        _logger = logger;
    }

    public override void Run(Behaviour behaviour)
    {
        if (behaviour.Status == Status.Running)
            return;

        if (behaviour is IScreenshotBehaviour screenshotBehaviour)
        {
            var currentFrame = screenshotBehaviour.Screenshot.Get();
            if (currentFrame == null)
                return;

            var fileName = $"{DateTime.Now:yyyyMMddHHmmssfff}_{behaviour.GetType().Name}_{behaviour.Status}.png";
            _logger.LogInformation("保存截图: {Name}", fileName);

            SaveScreenshot(currentFrame, fileName);
        }
    }

    public static void SaveScreenshot(ImageRegion imageRegion, string name)
    {
        var path = Global.Absolute($@"log\screenshot\");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        if (String.IsNullOrWhiteSpace(name))
        {
            name = $@"{DateTime.Now:yyyyMMddHHmmssffff}.png";
        }
        var savePath = Global.Absolute($@"log\screenshot\{name}");

        var mat = imageRegion.SrcMat;
        if (TaskContext.Instance().Config.CommonConfig.ScreenshotUidCoverEnabled)
        {
            new Task(() =>
            {
                using var mat2 = mat.Clone();
                var assetScale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
                var rect = new Rect((int)(mat2.Width - MaskWindowConfig.UidCoverRightBottomRect.X * assetScale),
                    (int)(mat2.Height - MaskWindowConfig.UidCoverRightBottomRect.Y * assetScale),
                    (int)(MaskWindowConfig.UidCoverRightBottomRect.Width * assetScale),
                    (int)(MaskWindowConfig.UidCoverRightBottomRect.Height * assetScale));
                mat2.Rectangle(rect, Scalar.White, -1);
                Cv2.ImWrite(savePath, mat2);
            }).Start();
        }
        else
        {
            new Task(() =>
            {
                Cv2.ImWrite(savePath, mat);
            }).Start();
        }
    }
}
