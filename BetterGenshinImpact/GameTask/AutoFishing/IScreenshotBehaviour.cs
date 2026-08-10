using BetterGenshinImpact.GameTask.Model.Area;
using CsTrees.Blackboard;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public interface IScreenshotBehaviour
    {
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; }
    }
}
