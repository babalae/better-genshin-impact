using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Diagnostics;
using Vanara.PInvoke;

namespace BetterGenshinImpact.UnitTest.GameTaskTests
{
    internal class FakeSystemInfo : ISystemInfo
    {
        public FakeSystemInfo(RECT gameScreenSize, double assetScale)
        {
            DesktopRectArea = new(gameScreenSize.Width, gameScreenSize.Height);
            UpdateCaptureGeometry(new RECT(0, 0, gameScreenSize.Width, gameScreenSize.Height));
        }

        public System.Drawing.Size DisplaySize => throw new NotImplementedException();

        public RECT GameScreenSize { get; private set; }

        public double AssetScale { get; private set; } = 1;

        public double ZoomOutMax1080PRatio { get; private set; } = 1;

        public double ScaleTo1080PRatio { get; private set; }

        public RECT CaptureAreaRect { get; set; }

        public Rect ScaleMax1080PCaptureRect { get; set; } = new Rect(0, 0, 1920, 1080);

        public CaptureGeometry CaptureGeometry { get; private set; } = null!;

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

            ScaleTo1080PRatio = GameScreenSize.Width / 1920d;

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

        public Process GameProcess => throw new NotImplementedException();

        public string GameProcessName => throw new NotImplementedException();

        public int GameProcessId => throw new NotImplementedException();

        public DesktopRegion DesktopRectArea { get; set; }
    }
}
