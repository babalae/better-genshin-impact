using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 游戏截图的保存：取帧、按配置遮挡 UID、在后台写入 log\screenshot。
/// <para>
/// 不依赖 GameRuntimeService：截哪个运行环境、保存哪一帧都由调用方传入，因此任何层都可以注入它，不会形成 DI 环
/// </para>
/// </summary>
public sealed class GameScreenshotService(AllConfig config, ILogger<GameScreenshotService> logger)
{
    /// <summary>
    /// 截图保存目录（相对程序目录）
    /// </summary>
    private const string ScreenshotFolder = @"log\screenshot\";

    /// <summary>
    /// 截取指定运行环境的当前画面（原始分辨率）并保存。截图热键使用
    /// </summary>
    /// <param name="runtime">要截图的运行环境</param>
    public void SaveCurrentFrame(GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Mat frame;
        try
        {
            frame = TaskControl.CaptureGameImage(runtime.Capture);
        }
        catch (Exception)
        {
            logger.LogInformation("截图失败，未获取到图像");
            return;
        }

        using (frame)
        {
            Save(frame);
        }
    }

    /// <summary>
    /// 保存一帧已有画面。在调用线程上复制后立即返回，写盘在后台进行；
    /// 调用方保留 <paramref name="frame"/> 的所有权，返回后即可释放
    /// </summary>
    /// <param name="frame">要保存的画面，原始分辨率或归一到 1080P 的帧都可以</param>
    /// <param name="fileName">文件名，为空时按时间生成</param>
    public void Save(Mat frame, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var name = string.IsNullOrWhiteSpace(fileName) ? $"{DateTime.Now:yyyyMMddHHmmssffff}.png" : fileName;

        // 复制在调用线程上同步完成：调用方返回后可能立即释放原帧
        Mat copy;
        try
        {
            copy = frame.Clone();
        }
        catch (Exception e)
        {
            logger.LogError("截图保存失败: {Message}", e.Message);
            return;
        }

        var coverUid = config.CommonConfig.ScreenshotUidCoverEnabled;
        _ = Task.Run(() => Write(copy, name, coverUid));
    }

    /// <summary>
    /// 在后台遮挡 UID 并写盘，负责释放 <paramref name="copy"/>
    /// </summary>
    private void Write(Mat copy, string name, bool coverUid)
    {
        using (copy)
        {
            try
            {
                var folder = Global.Absolute(ScreenshotFolder);
                Directory.CreateDirectory(folder);
                if (coverUid)
                {
                    CoverUid(copy);
                }

                Cv2.ImWrite(Path.Combine(folder, name), copy);
                logger.LogInformation("截图已保存: {Name}", name);
            }
            catch (Exception e)
            {
                logger.LogError("截图保存失败: {Message}", e.Message);
                logger.LogDebug("截图保存失败: {StackTrace}", e.StackTrace);
            }
        }
    }

    /// <summary>
    /// 用白色矩形遮住右下角的 UID。比例按帧自身尺寸计算，原始帧和归一到 1080P 的帧都适用
    /// </summary>
    private static void CoverUid(Mat frame)
    {
        var scale = Math.Min(frame.Width / 1920d, frame.Height / 1080d);
        var cover = MaskWindowConfig.UidCoverRightBottomRect;
        var rect = new Rect((int)(frame.Width - cover.X * scale),
            (int)(frame.Height - cover.Y * scale),
            (int)(cover.Width * scale),
            (int)(cover.Height * scale));
        frame.Rectangle(rect, Scalar.White, -1);
    }
}
