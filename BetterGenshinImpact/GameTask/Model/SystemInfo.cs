using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System;
using System.Diagnostics;
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
        /// 捕获内容区，排除全屏黑边后和实际游戏画面一致
        /// </summary>
        public RECT CaptureAreaRect { get; private set; }

        /// <summary>
        /// 捕获内容区，大于1080P则为1920x1080
        /// </summary>
        public Rect ScaleMax1080PCaptureRect { get; private set; }

        public CaptureGeometry CaptureGeometry { get; private set; } = null!;

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

            UpdateCaptureGeometry(SystemControl.GetCaptureRect(hWnd));
            if (GameScreenSize.Width < 800 || GameScreenSize.Height < 600)
            {
                throw new ArgumentException("游戏窗口分辨率不得小于 800x600 ！");
            }
        }

        public void UpdateCaptureGeometry(RECT rawCaptureRect)
        {
            var geometry = BetterGenshinImpact.GameTask.Model.CaptureGeometry.FromRawCaptureRect(rawCaptureRect);
            if (!geometry.HasValidContentSpace)
            {
                return;
            }

            CaptureGeometry = geometry;
            CaptureAreaRect = CaptureGeometry.ContentRect;
            GameScreenSize = new RECT(0, 0, CaptureGeometry.ContentSpace.Width, CaptureGeometry.ContentSpace.Height);

            AssetScale = 1;
            ZoomOutMax1080PRatio = 1;
            if (GameScreenSize.Width < 1920)
            {
                ZoomOutMax1080PRatio = GameScreenSize.Width / 1920d;
                AssetScale = ZoomOutMax1080PRatio;
            }

            ScaleTo1080PRatio = GameScreenSize.Width / 1920d; // 1080P 为标准

            if (GameScreenSize.Width > 1920)
            {
                var scale = GameScreenSize.Width / 1920d;
                ScaleMax1080PCaptureRect = new Rect(0, 0, 1920, (int)(GameScreenSize.Height / scale));
            }
            else
            {
                ScaleMax1080PCaptureRect = new Rect(0, 0, GameScreenSize.Width, GameScreenSize.Height);
            }
        }
    }
}
