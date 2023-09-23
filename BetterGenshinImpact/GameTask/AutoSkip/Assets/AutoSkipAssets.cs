using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoSkip.Assets
{
    public class AutoSkipAssets
    {
        public static Mat StopAutoButtonMat  = new(Global.Absolute(@"GameTask\AutoSkip\Assets\1920x1080\stop_auto.png"), ImreadModes.Grayscale);
        public static Mat OptionMat = new(Global.Absolute(@"GameTask\AutoSkip\Assets\1920x1080\option.png"), ImreadModes.Grayscale);
        public static Mat MenuMat = new(Global.Absolute(@"GameTask\AutoSkip\Assets\1920x1080\menu.png"), ImreadModes.Grayscale);
    }
}
