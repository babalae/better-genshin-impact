using BetterGenshinImpact.Core.Config;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPick.Assets
{
    public class AutoPickAssets
    {
        public static Mat StopAutoButtonMat = new(Global.Absolute(@"GameTask\AutoPick\Assets\1920x1080\F.png"), ImreadModes.Grayscale);
    }
}
