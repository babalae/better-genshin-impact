using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.UnitTest.GameTaskTests
{
    internal class FakeSystemInfo : ISystemInfo
    {
        public FakeSystemInfo(RECT gameScreenSize, double assetScale)
        {
            GameScreenSize = gameScreenSize;
            // 0.28 改动，素材缩放比例不可以超过 1，也就是图像识别时分辨率大于 1920x1080 的情况下直接进行缩放
            if (GameScreenSize.Width < 1920)
            {
                ZoomOutMax1080PRatio = GameScreenSize.Width / 1920d;
                AssetScale = ZoomOutMax1080PRatio;
            }
            ScaleTo1080PRatio = GameScreenSize.Width / 1920d; // 1080P 为标准
        }

        public System.Drawing.Size DisplaySize => throw new NotImplementedException();

        public RECT GameScreenSize { get; }

        public double AssetScale { get; } = 1;

        public double ZoomOutMax1080PRatio { get; }

        public double ScaleTo1080PRatio { get; }

        public RECT CaptureAreaRect { get ; set ; }
        public Rect ScaleMax1080PCaptureRect { get ; set ; }

        public Process GameProcess => throw new NotImplementedException();

        public string GameProcessName => throw new NotImplementedException();

        public int GameProcessId => throw new NotImplementedException();

        public DesktopRegion DesktopRectArea => throw new NotImplementedException();
    }
}
