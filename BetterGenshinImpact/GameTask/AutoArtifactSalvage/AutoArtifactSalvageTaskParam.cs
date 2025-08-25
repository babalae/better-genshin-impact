using BetterGenshinImpact.GameTask.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage
{
    public class AutoArtifactSalvageTaskParam : BaseTaskParam
    {
        public AutoArtifactSalvageTaskParam(int star, string? javaScript, int? maxNumToCheck, CultureInfo? cultureInfo) : base(cultureInfo)
        {
            Star = star;
            JavaScript = javaScript;
            MaxNumToCheck = maxNumToCheck;
        }

        public int Star { get; set; }
        public string? JavaScript { get; set; }
        public int? MaxNumToCheck { get; set; }
    }
}
