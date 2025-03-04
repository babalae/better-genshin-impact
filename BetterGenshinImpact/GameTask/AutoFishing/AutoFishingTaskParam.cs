using BetterGenshinImpact.GameTask.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTaskParam : BaseTaskParam
    {
        public AutoFishingTaskParam(int wholeProcessTimeoutSeconds, int throwRodTimeOutTimeoutSeconds, FishingTimePolicy fishingTimePolicy)
        {
            WholeProcessTimeoutSeconds = wholeProcessTimeoutSeconds;
            ThrowRodTimeOutTimeoutSeconds = throwRodTimeOutTimeoutSeconds;
            FishingTimePolicy = fishingTimePolicy;
        }

        public int WholeProcessTimeoutSeconds { get; set; }
        public int ThrowRodTimeOutTimeoutSeconds { get; set; }
        public FishingTimePolicy FishingTimePolicy { get; set; }
    }
}
