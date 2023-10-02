using BetterGenshinImpact.Helpers;
using System;
using System.Diagnostics;
using Vanara.PInvoke;
using Size = System.Drawing.Size;

namespace BetterGenshinImpact.GameTask.Model
{
    public class SystemInfo
    {

        /// <summary>
        /// 显示器分辨率 无缩放
        /// </summary>
        public Size DisplaySize { get;}

        /// <summary>
        /// 游戏窗口内分辨率
        /// </summary>
        public RECT GameScreenSize { get;  }

        /// <summary>
        /// 素材缩放比例
        /// </summary>
        public double AssetScale { get;  }

        /// <summary>
        /// 游戏窗口内分辨率
        /// </summary>
        public RECT GameWindowRect { get; }

        public Process GameProcess { get;  }

        public string GameProcessName { get; }

        public int GameProcessId { get;}

        public RectArea DesktopRectArea { get; }

        public SystemInfo(IntPtr hWnd)
        {
            var p = SystemControl.GetProcessByHandle(hWnd);
            GameProcess = p ?? throw new ArgumentException("通过句柄获取游戏进程失败");
            GameProcessName = GameProcess.ProcessName;
            GameProcessId = GameProcess.Id;

            DisplaySize = PrimaryScreen.WorkingArea;
            DesktopRectArea = new RectArea( 0, 0, DisplaySize.Width, DisplaySize.Height);
            // 注意截图区域要和游戏窗口实际区域一致
            // todo 窗口移动后？
            GameScreenSize = SystemControl.GetGameScreenRect(hWnd);
            AssetScale = GameScreenSize.Width / 1920d;
            GameWindowRect = SystemControl.GetWindowRect(hWnd);

        }
    }
}
