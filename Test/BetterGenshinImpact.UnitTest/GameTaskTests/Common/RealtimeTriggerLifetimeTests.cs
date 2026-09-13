using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using System.Collections.Concurrent;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.Common;

[Collection("Realtime trigger lifetime")]
public class RealtimeTriggerLifetimeTests
{
    [Fact]
    public void ClearTriggersPreservesOnlyAlwaysActiveTriggers()
    {
        var previous = GameTaskManager.TriggerDictionary;
        try
        {
            GameTaskManager.TriggerDictionary = new ConcurrentDictionary<string, ITaskTrigger>();
            GameTaskManager.TriggerDictionary["regular"] = new FakeTrigger(false);
            GameTaskManager.TriggerDictionary["resident"] = new FakeTrigger(true);

            GameTaskManager.ClearTriggers();

            Assert.False(GameTaskManager.TriggerDictionary.ContainsKey("regular"));
            Assert.True(GameTaskManager.TriggerDictionary.ContainsKey("resident"));
        }
        finally
        {
            GameTaskManager.TriggerDictionary = previous;
        }
    }

    private sealed class FakeTrigger(bool alwaysActive) : ITaskTrigger
    {
        public string Name => "Fake";
        public bool IsEnabled { get; set; } = true;
        public int Priority => 0;
        public bool IsExclusive => false;
        public bool AlwaysActive => alwaysActive;
        public void Init() { }
        public void OnCapture(CaptureContent content) { }
    }
}

[CollectionDefinition("Realtime trigger lifetime", DisableParallelization = true)]
public sealed class RealtimeTriggerLifetimeCollection;
