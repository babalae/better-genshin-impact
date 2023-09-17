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
        public static Mat StopAutoButtonMat  = new(Global.Absolute("GameTask/AutoSkip/Assets/stop_auto.png"), ImreadModes.Grayscale);
        public static Mat OptionMat = new(Global.Absolute("GameTask/AutoSkip/Assets/option.png"), ImreadModes.Grayscale);
    }
}
