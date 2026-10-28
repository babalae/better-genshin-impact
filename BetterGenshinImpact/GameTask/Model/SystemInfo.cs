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
        /// 当前捕获窗口区域；设置新区域时同步刷新所有分辨率派生数据
        /// </summary>
        private RECT _captureAreaRect;

        /// <summary>
        /// 显示器分辨率 无缩放
        /// </summary>
        public Size DisplaySize { get; }

        /// <summary>
        /// 游戏窗口内分辨率
        /// </summary>
        public RECT GameScreenSize { get; private set; }

        /// <summary>
        /// 以1080P为标准的素材缩放比例,不会大于1
        /// 与 ZoomOutMax1080PRatio 相等
        /// </summary>
        public double AssetScale { get; private set; } = 1;

        /// <summary>
        /// 游戏区域比1080P缩小的比例
        /// 最大值为1
        /// </summary>
        public double ZoomOutMax1080PRatio { get; private set; } = 1;

        /// <summary>
        /// 捕获游戏区域缩放至1080P的比例
        /// </summary>
        public double ScaleTo1080PRatio { get; private set; }

        /// <summary>
        /// 捕获窗口区域 和实际游戏画面一致
        /// CaptureAreaRect = GameScreenSize or GameWindowRect
        /// </summary>
        public RECT CaptureAreaRect
        {
            get => _captureAreaRect;
            set
            {
                _captureAreaRect = value;
                RefreshResolutionData(value);
            }
        }

        /// <summary>
        /// 捕获窗口区域 大于1080P则为1920x1080
        /// </summary>
        public Rect ScaleMax1080PCaptureRect { get; set; }

        public DesktopRegion DesktopRectArea { get; }

        /// <summary>
        /// 输入坐标缩放比例，绑定时由画面区域记录的快照
        /// </summary>
        public float DpiScale { get; }

        /// <param name="viewport">游戏画面区域，由运行环境提供。最小化检查已在附着窗口时完成</param>
        /// <param name="drawingBoard">运行环境借用的遮罩窗口绘制入口，截图区域树从根开始继承；为空时区域上的绘制不生效</param>
        public SystemInfo(GameViewport viewport, IMaskWindowDrawingBoard? drawingBoard = null)
        {
            DisplaySize = PrimaryScreen.WorkingArea;
            DesktopRectArea = new DesktopRegion(drawingBoard);
            DpiScale = viewport.DpiScale;

            // 注意截图区域要和游戏窗口实际区域一致
            CaptureAreaRect = viewport.ScreenRect;
            if (GameScreenSize.Width < 800 || GameScreenSize.Height < 600)
            {
                throw new ArgumentException("游戏窗口分辨率不得小于 800x600 ！");
            }
        }

        /// <summary>
        /// 根据当前捕获区域统一刷新游戏分辨率、素材缩放比例和归一化捕获区域
        /// </summary>
        /// <param name="captureAreaRect">当前捕获窗口区域</param>
        private void RefreshResolutionData(RECT captureAreaRect)
        {
            GameScreenSize = new RECT(0, 0, captureAreaRect.Width, captureAreaRect.Height);

            // 0.28 改动，素材缩放比例不可以超过 1，也就是图像识别时分辨率大于 1920x1080 的情况下直接进行缩放
            // 取宽高相对于 1920x1080 的缩放比中的较小值，保持非 16:9 分辨率下的宽高比不失真
            ScaleTo1080PRatio = Math.Min(GameScreenSize.Width / 1920d, GameScreenSize.Height / 1080d); // 1080P 为标准
            ZoomOutMax1080PRatio = Math.Min(ScaleTo1080PRatio, 1d);
            AssetScale = ZoomOutMax1080PRatio;

            if (captureAreaRect.Width <= 0 || captureAreaRect.Height <= 0)
            {
                ScaleMax1080PCaptureRect = new Rect(captureAreaRect.X, captureAreaRect.Y, 0, 0);
            }
            else if (captureAreaRect.Width > 1920 || captureAreaRect.Height > 1080)
            {
                var scale = Math.Min(captureAreaRect.Width / 1920d, captureAreaRect.Height / 1080d);
                ScaleMax1080PCaptureRect = new Rect(captureAreaRect.X, captureAreaRect.Y, (int)(captureAreaRect.Width / scale), (int)(captureAreaRect.Height / scale));
            }
            else
            {
                ScaleMax1080PCaptureRect = new Rect(captureAreaRect.X, captureAreaRect.Y, captureAreaRect.Width, captureAreaRect.Height);
            }
        }
    }
}
