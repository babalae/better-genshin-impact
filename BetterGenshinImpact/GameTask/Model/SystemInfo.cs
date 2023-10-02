using BetterGenshinImpact.Helpers;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32.Foundation;
using OpenCvSharp;
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

        public Process GameProcess { get;  }

        public string GameProcessName { get; }

        public int GameProcessId { get;}

        public RectArea DesktopRectArea { get; }

        public SystemInfo(IntPtr hWnd)
        {
            DisplaySize = PrimaryScreen.WorkingArea;
            DesktopRectArea = new RectArea( 0, 0, DisplaySize.Width, DisplaySize.Height);
            // 注意截图区域要和游戏窗口实际区域一致
            // todo 窗口移动后？
            GameScreenSize = SystemControl.GetGameScreenRect(hWnd);
            AssetScale = GameScreenSize.Width / 1920d;
            GameProcess = SystemControl.GetProcessByHandle(hWnd);
            GameProcessName = GameProcess.ProcessName;
            GameProcessId = GameProcess.Id;
        }
    }
}
