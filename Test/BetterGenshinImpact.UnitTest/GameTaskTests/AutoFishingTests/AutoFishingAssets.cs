using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    internal class AutoFishingAssets : GameTask.AutoFishing.Assets.AutoFishingAssets
    {
        internal AutoFishingAssets(ISystemInfo systemInfo): base(systemInfo)
        {
        }
    }
}
