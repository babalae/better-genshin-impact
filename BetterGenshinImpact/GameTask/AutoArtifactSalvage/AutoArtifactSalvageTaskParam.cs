using BetterGenshinImpact.GameTask.Model;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage
{
    public class AutoArtifactSalvageTaskParam : BaseTaskParam<AutoArtifactSalvageTask>
    {
        public AutoArtifactSalvageTaskParam(int star, string? javaScript, int? maxNumToCheck, CultureInfo? cultureInfo = null, IStringLocalizer<AutoArtifactSalvageTask>? stringLocalizer = null) : base(cultureInfo, stringLocalizer)
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
