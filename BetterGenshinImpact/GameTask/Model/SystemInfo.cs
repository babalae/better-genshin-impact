using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System;
using Vanara.PInvoke;
using Size = System.Drawing.Size;

namespace BetterGenshinImpact.GameTask.Model
{
    public class SystemInfo : ISystemInfo
    {
        /// <summary>
        /// 显示器分辨率 无缩放
        /// </summary>
        public Size DisplaySize { get; }

        /// <summary>
        /// 游戏窗口内分辨率
        /// </summary>
        public RECT GameScreenSize { get; }

        /// <summary>
        /// 以1080P为标准的素材缩放比例,不会大于1
        /// 与 ZoomOutMax1080PRatio 相等
        /// </summary>
        public double AssetScale { get; } = 1;

        /// <summary>
        /// 游戏区域比1080P缩小的比例
        /// 最大值为1
        /// </summary>
        public double ZoomOutMax1080PRatio { get; } = 1;

        /// <summary>
        /// 捕获游戏区域缩放至1080P的比例
        /// </summary>
        public double ScaleTo1080PRatio { get; }

        /// <summary>
        /// 捕获窗口区域 和实际游戏画面一致
        /// CaptureAreaRect = GameScreenSize or GameWindowRect
        /// </summary>
        public RECT CaptureAreaRect { get; set; }

        /// <summary>
        /// 捕获窗口区域 大于1080P则为1920x1080
        /// </summary>
        public Rect ScaleMax1080PCaptureRect { get; set; }

        public DesktopRegion DesktopRectArea { get; }

        /// <param name="viewport">游戏画面区域，由运行环境提供。最小化检查已在附着窗口时完成</param>
        /// <param name="drawingBoard">运行环境借用的遮罩窗口绘制入口，截图区域树从根开始继承；为空时区域上的绘制不生效</param>
        public SystemInfo(GameViewport viewport, IMaskWindowDrawingBoard? drawingBoard = null)
        {
            DisplaySize = PrimaryScreen.WorkingArea;
            DesktopRectArea = new DesktopRegion(drawingBoard);

            // 注意截图区域要和游戏窗口实际区域一致
            GameScreenSize = new RECT(0, 0, viewport.Width, viewport.Height);
            if (GameScreenSize.Width < 800 || GameScreenSize.Height < 600)
            {
                throw new ArgumentException("游戏窗口分辨率不得小于 800x600 ！");
            }

            // 0.28 改动，素材缩放比例不可以超过 1，也就是图像识别时分辨率大于 1920x1080 的情况下直接进行缩放
            if (GameScreenSize.Width < 1920)
            {
                ZoomOutMax1080PRatio = GameScreenSize.Width / 1920d;
                AssetScale = ZoomOutMax1080PRatio;
            }
            ScaleTo1080PRatio = GameScreenSize.Width / 1920d; // 1080P 为标准

            CaptureAreaRect = viewport.ScreenRect;
            if (CaptureAreaRect.Width > 1920)
            {
                var scale = CaptureAreaRect.Width / 1920d;
                ScaleMax1080PCaptureRect = new Rect(CaptureAreaRect.X, CaptureAreaRect.Y, 1920, (int)(CaptureAreaRect.Height / scale));
            }
            else
            {
                ScaleMax1080PCaptureRect = new Rect(CaptureAreaRect.X, CaptureAreaRect.Y, CaptureAreaRect.Width, CaptureAreaRect.Height);
            }
        }
    }
}
