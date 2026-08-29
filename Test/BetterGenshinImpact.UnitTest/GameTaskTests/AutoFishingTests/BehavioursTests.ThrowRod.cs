using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using CsTrees;
using CsTrees.Composites;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", BaitType.FalseWormBait)]
        /// <summary>
        /// 测试各种抛竿，结果为成功
        /// </summary>
        public async Task ThrowRodTest_VariousFish_ShouldSuccess(string screenshot1080p, BaitType selectedBait)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var selectedBaitAccess = blackboard.GrantWrite<BaitType?>(null!, "SelectedBait");
            selectedBaitAccess.Set(selectedBait);
            var throwRodNoBaitFishAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoBaitFish");

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion], bb!))
                        .LiftRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, fakeTimeProvider, drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();

            //
            Status actual = await sut.TickOnce();

            //
            Assert.False(throwRodNoBaitFishAccess.Get());
            Assert.Equal(Status.Success, actual);
        }

        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", BaitType.RedrotBait)]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", BaitType.FakeFlyBait)]
        [InlineData(@"20250226162217468_ThrowRod_Succeeded.png", BaitType.FruitPasteBait)]
        /// <summary>
        /// 测试各种抛竿，未满足HutaoFisher判定，结果为运行中
        /// </summary>
        public async Task ThrowRodTest_VariousFish_ShouldFail(string screenshot1080p, BaitType selectedBait)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var selectedBaitAccess = blackboard.GrantWrite<BaitType?>(null!, "SelectedBait");
            selectedBaitAccess.Set(selectedBait);
            var throwRodNoBaitFishAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoBaitFish");

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion], bb!))
                        .LiftRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, fakeTimeProvider, drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();

            //
            Status actual = await sut.TickOnce();

            //
            Assert.False(throwRodNoBaitFishAccess.Get());
            Assert.Equal(Status.Running, actual);
        }

        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", BaitType.FlashingMaintenanceMekBait)]
        /// <summary>
        /// 测试各种抛竿，无鱼饵适用鱼，结果为失败
        /// </summary>
        public async Task ThrowRodTest_NoBaitFish_ShouldFail(string screenshot1080p, BaitType selectedBait)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var selectedBaitAccess = blackboard.GrantWrite<BaitType?>(null!, "SelectedBait");
            selectedBaitAccess.Set(selectedBait);
            var throwRodNoBaitFishAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoBaitFish");

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(imageRegion, 11), bb!))
                        .LiftRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, fakeTimeProvider, drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();
            //
            Status actual = await sut.TickOnce();

            //
            Assert.False(throwRodNoBaitFishAccess.Get());
            Assert.Equal(Status.Running, actual);

            //
            // Do nothing

            //
            for (int i = 0; i < 10; i++)
            {
                await sut.TickOnce();
            }

            //
            Assert.True(throwRodNoBaitFishAccess.Get());
        }

        /// <summary>
        /// 抛竿时，给定三条炮鲀鱼，并且确定算法能将下杆点从鱼的左侧移动到最左侧的炮鲀鱼上，此时希望“当前鱼”能始终锁定在最左侧的鱼上
        /// 由于偶尔观测到“摇摆”行为的出现，故设计此测试
        /// </summary>
        [Fact]
        public async Task ThrowRodTest_Target_ShouldBeTheLeftOne()
        {
            //
            Mat mat1 = new Mat(@$"..\..\..\Assets\AutoFishing\202503082114541115.png");
            var imageRegion1 = new GameCaptureRegion(mat1, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            Mat mat2 = new Mat(@$"..\..\..\Assets\AutoFishing\202503082114560489.png");
            var imageRegion2 = new GameCaptureRegion(mat2, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var selectedBaitAccess = blackboard.GrantWrite<BaitType?>(null!, "SelectedBait");
            selectedBaitAccess.Set(BaitType.FakeFlyBait);
            var fishpondAccess = blackboard.GrantWrite<Fishpond>(null!, "Fishpond");

            var sut = new LiftRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, blackboard, new FakeTimeProvider(), drawContent: new FakeDrawContent());
            var tree = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion1, imageRegion2], bb!))
                        .Leaf(() => sut)
                    .End()
                .End()
                .Build();

            //
            await tree.TickOnce();
            var actual = sut.currentFish;

            //
            var fishpond = fishpondAccess.Get();
            Assert.True(fishpond.TargetRect != null && fishpond.TargetRect.Value != default);
            Assert.Equal(3, fishpond.Fishes.Count(f => f.FishType.Name == "pufferfish"));
            Assert.Equal(fishpond.Fishes.OrderBy(f => f.Rect.X).First(), actual);

            //

            //
            await sut.TickOnce();
            actual = sut.currentFish;

            //
            fishpond = fishpondAccess.Get();
            Assert.Equal(3, fishpond.Fishes.Count(f => f.FishType.Name == "pufferfish"));
            Assert.Equal(fishpond.Fishes.OrderBy(f => f.Rect.X).First(), actual);
        }

        [Theory]
        [InlineData(@"202502252347412417.png")]
        [InlineData(@"202503012143011486@900p.png")]
        /// <summary>
        /// 测试各种抛竿，超时未找到落点，结果为失败
        /// </summary>
        public async Task ThrowRodTest_NoTarget_ShouldFail(string screenshot1080p)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var throwRodNoTargetAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoTarget");

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion, imageRegion], bb!))
                        .LiftRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, fakeTimeProvider, drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();

            //
            Status actual = await sut.TickOnce();

            //
            Assert.False(throwRodNoTargetAccess.Get());
            Assert.Equal(Status.Running, actual);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(5.1));

            //
            actual = await sut.TickOnce();

            //
            Assert.True(throwRodNoTargetAccess.Get());
            Assert.Equal(Status.Failure, actual);
        }

        [Theory]
        [InlineData(@"202502252347412417.png")]
        /// <summary>
        /// 测试抛竿循环，3次超时未找到落点，结果为失败并退出
        /// </summary>
        public async Task ThrowRodTest_NoTarget_3Times_ShouldFailAndAbort(string screenshot1080p)
        {
            //
            FakeInputSimulator input = new FakeInputSimulator();
            FakeDrawContent drawContent = new FakeDrawContent();
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(input.Mouse), converter: new ScaleConverter(1d), drawContent: drawContent);
            FakeTimeProvider timeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var throwRodNoTargetAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoTarget");
            var throwRodNoTargetTimesAccess = blackboard.GrantWrite<int>(null!, "ThrowRodNoTargetTimes");
            var abortAccess = blackboard.GrantWrite<bool>(null!, "Abort");

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(imageRegion, 9), bb!))
                            .Parallel("抛竿直到成功或出错", policy: new ParallelPolicy.SuccessOnOne())
                                .FailureIsSuccess("重复抛竿")
                                    .Sequence("-", true)
                                        .MoveViewpointDown("调整视角至俯视", logger, input)
                                        .LiftRod("抛竿", logger, input, Predictor, timeProvider, drawContent)
                                    .End()
                                .End()
                                .CheckThrowRodResult("抛竿检查")
                            .End()
                    .End()
                .End()
                .Build();

            //
            await sut.TickOnce();
            await sut.TickOnce();
            timeProvider.Advance(TimeSpan.FromSeconds(5.1));
            Status actual = await sut.TickOnce();

            //
            Assert.True(throwRodNoTargetAccess.Get());
            Assert.Equal(1, throwRodNoTargetTimesAccess.Get());
            Assert.Equal(Status.Failure, actual);

            //

            //
            await sut.TickOnce();
            await sut.TickOnce();
            timeProvider.Advance(TimeSpan.FromSeconds(5.1));
            actual = await sut.TickOnce();

            //
            Assert.True(throwRodNoTargetAccess.Get());
            Assert.Equal(2, throwRodNoTargetTimesAccess.Get());
            Assert.Equal(Status.Failure, actual);

            //

            //
            await sut.TickOnce();
            await sut.TickOnce();
            timeProvider.Advance(TimeSpan.FromSeconds(5.1));
            actual = await sut.TickOnce();

            //
            Assert.True(throwRodNoTargetAccess.Get());
            Assert.Equal(3, throwRodNoTargetTimesAccess.Get());
            Assert.Equal(Status.Failure, actual);
            Assert.True(abortAccess.Get());
        }
    }
}
