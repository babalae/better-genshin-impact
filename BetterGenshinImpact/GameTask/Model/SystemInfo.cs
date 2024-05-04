using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using System;
using System.Diagnostics;
using OpenCvSharp;
using Vanara.PInvoke;
using Size = System.Drawing.Size;

namespace BetterGenshinImpact.GameTask.Model
{
    public class SystemInfo
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

        public Process GameProcess { get; }

        public string GameProcessName { get; }

        public int GameProcessId { get; }

        public DesktopRegion DesktopRectArea { get; }

        public SystemInfo(IntPtr hWnd)
        {
            var p = SystemControl.GetProcessByHandle(hWnd);
            GameProcess = p ?? throw new ArgumentException("通过句柄获取游戏进程失败");
            GameProcessName = GameProcess.ProcessName;
            GameProcessId = GameProcess.Id;

            DisplaySize = PrimaryScreen.WorkingArea;
            DesktopRectArea = new DesktopRegion();

            // 判断最小化
            if (User32.IsIconic(hWnd))
            {
                throw new ArgumentException("游戏窗口不能最小化");
            }

            // 注意截图区域要和游戏窗口实际区域一致
            // todo 窗口移动后？
            GameScreenSize = SystemControl.GetGameScreenRect(hWnd);
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

            CaptureAreaRect = SystemControl.GetCaptureRect(hWnd);
            ScaleMax1080PCaptureRect = new Rect(CaptureAreaRect.X, CaptureAreaRect.Y, CaptureAreaRect.Width > 1920 ? 1920 : CaptureAreaRect.Width, CaptureAreaRect.Height > 1080 ? 1080 : CaptureAreaRect.Height);
        }
    }
}
