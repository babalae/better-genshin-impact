using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System.Diagnostics;
using Vanara.PInvoke;
using Size = System.Drawing.Size;

namespace BetterGenshinImpact.GameTask.Model
{
    public interface ISystemInfo
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
        public double AssetScale { get; }

        /// <summary>
        /// 游戏区域比1080P缩小的比例
        /// 最大值为1
        /// </summary>
        public double ZoomOutMax1080PRatio { get; }

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

        public Process GameProcess { get; }

        public string GameProcessName { get; }

        public int GameProcessId { get; }

        public DesktopRegion DesktopRectArea { get; }
    }
}
