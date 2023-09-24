using BetterGenshinImpact.Core.Config;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoFishing.Assets
{
    public class AutoFishingAssets
    {
        public static Mat SpaceButtonMat = new(Global.Absolute(@"GameTask\AutoFishing\Assets\1920x1080\Space.png"), ImreadModes.Grayscale);
    }
}
