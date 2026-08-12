using BetterGenshinImpact.GameTask.Model.Area;
using CsTrees;
using CsTrees.Blackboard;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    /// <summary>
    /// 测试专用：从预设的 ImageRegion 队列中依次出队，写入 Blackboard 的 Screenshot 键。
    /// 用于替代主项目的 TakeScreenshot 行为，使行为树测试不依赖真实截图。
    /// </summary>
    public partial class ScreenshotQueue : Behaviour
    {
        private readonly Queue<ImageRegion> _queue;

        [BlackboardKey(Access = Access.ExclusiveWrite)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        public ScreenshotQueue(string name, IEnumerable<ImageRegion> screenshots) : base(name)
        {
            _queue = new Queue<ImageRegion>(screenshots);
        }

        protected override Task<Status> Update()
        {
            if (_queue.Count == 0)
            {
                throw new InvalidOperationException("截图队列已空，无法提供截图");
            }

            var screenshot = _queue.Dequeue();
            Screenshot.Set(screenshot);
            return Task.FromResult(Status.Success);
        }
    }
}
