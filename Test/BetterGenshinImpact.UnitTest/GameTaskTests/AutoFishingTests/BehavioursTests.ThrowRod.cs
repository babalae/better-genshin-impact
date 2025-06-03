using BehaviourTree;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;
using BehaviourTree.Composites;
using BehaviourTree.FluentBuilder;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", "false worm bait")]
        [InlineData(@"20250226162217468_ThrowRod_Succeeded.png", "fruit paste bait")]
        /// <summary>
        /// 测试各种抛竿，结果为成功
        /// </summary>
        public void ThrowRodTest_VariousFish_ShouldSuccess(string screenshot1080p, string selectedBaitName)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(Predictor, sleep: i => { })
            {
                selectedBaitName = selectedBaitName
            };

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            //
            ThrowRod sut = new ThrowRod("-", blackboard, true, new FakeLogger(), false, new FakeInputSimulator(), fakeTimeProvider, drawContent: new FakeDrawContent());
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.False(blackboard.throwRodNoBaitFish);
            Assert.Equal(BehaviourStatus.Succeeded, actual);
        }

        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", "redrot bait")]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", "fake fly bait")]
        /// <summary>
        /// 测试各种抛竿，未满足HutaoFisher判定，结果为运行中
        /// </summary>
        public void ThrowRodTest_VariousFish_ShouldFail(string screenshot1080p, string selectedBaitName)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(Predictor, sleep: i => { })
            {
                selectedBaitName = selectedBaitName
            };

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            //
            ThrowRod sut = new ThrowRod("-", blackboard, this.useTorch, new FakeLogger(), false, new FakeInputSimulator(), fakeTimeProvider, drawContent: new FakeDrawContent());
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.False(blackboard.throwRodNoBaitFish);
            Assert.Equal(BehaviourStatus.Running, actual);
        }

        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", "flashing maintenance mek bait")]
        /// <summary>
        /// 测试各种抛竿，无鱼饵适用鱼，结果为失败
        /// </summary>
        public void ThrowRodTest_NoBaitFish_ShouldFail(string screenshot1080p, string selectedBaitName)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(Predictor, sleep: i => { })
            {
                selectedBaitName = selectedBaitName
            };

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            //
            ThrowRod sut = new ThrowRod("-", blackboard, this.useTorch, new FakeLogger(), false, new FakeInputSimulator(), fakeTimeProvider, drawContent: new FakeDrawContent());
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.False(blackboard.throwRodNoBaitFish);
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            // Do nothing

            //
            for (int i = 0; i < 10; i++)
            {
                sut.Tick(imageRegion);
            }

            //
            Assert.True(blackboard.throwRodNoBaitFish);
        }

        /// <summary>
        /// 抛竿时，给定三条炮鲀鱼，并且确定算法能将下杆点从鱼的左侧移动到最左侧的炮鲀鱼上，此时希望“当前鱼”能始终锁定在最左侧的鱼上
        /// 由于偶尔观测到“摇摆”行为的出现，故设计此测试
        /// </summary>
        [Fact]
        public void ThrowRodTest_Target_ShouldBeTheLeftOne()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\202503082114541115.png");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(Predictor, sleep: i => { })
            {
                selectedBaitName = "fake fly bait"
            };

            //
            ThrowRod sut = new ThrowRod("-", blackboard, this.useTorch, new FakeLogger(), false, new FakeInputSimulator(), new FakeTimeProvider(), drawContent: new FakeDrawContent());
            sut.Tick(imageRegion);
            var actual = sut.currentFish;

            //
            Assert.True(blackboard.fishpond.TargetRect != null && blackboard.fishpond.TargetRect.Value != default);
            Assert.Equal(3, blackboard.fishpond.Fishes.Count(f => f.FishType.Name == "pufferfish"));
            Assert.Equal(blackboard.fishpond.Fishes.OrderBy(f => f.Rect.X).First(), actual);

            //
            mat = new Mat(@$"..\..\..\Assets\AutoFishing\202503082114560489.png");
            imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            //
            sut.Tick(imageRegion);
            actual = sut.currentFish;

            //
            Assert.Equal(3, blackboard.fishpond.Fishes.Count(f => f.FishType.Name == "pufferfish"));
            Assert.Equal(blackboard.fishpond.Fishes.OrderBy(f => f.Rect.X).First(), actual);
        }

        [Theory]
        [InlineData(@"202502252347412417.png")]
        [InlineData(@"202503012143011486@900p.png")]
        /// <summary>
        /// 测试各种抛竿，超时未找到落点，结果为失败
        /// </summary>
        public void ThrowRodTest_NoTarget_ShouldFail(string screenshot1080p)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(Predictor, sleep: i => { });

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            //
            ThrowRod sut = new ThrowRod("-", blackboard, this.useTorch, new FakeLogger(), false, new FakeInputSimulator(), fakeTimeProvider, drawContent: new FakeDrawContent());
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.False(blackboard.throwRodNoTarget);
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(5.1));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.True(blackboard.throwRodNoTarget);
            Assert.Equal(BehaviourStatus.Failed, actual);
        }

        [Theory]
        [InlineData(@"202502252347412417.png")]
        /// <summary>
        /// 测试抛竿循环，3次超时未找到落点，结果为失败并退出
        /// </summary>
        public void ThrowRodTest_NoTarget_3Times_ShouldFailAndAbort(string screenshot1080p)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");

            FakeInputSimulator input = new FakeInputSimulator();
            FakeDrawContent drawContent = new FakeDrawContent();
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(input.Mouse), converter: new ScaleConverter(1d), drawContent: drawContent);

            var blackboard = new Blackboard(Predictor, sleep: i => { });

            FakeTimeProvider timeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();

            //
            var sut = FluentBuilder.Create<ImageRegion>()
                .MySimpleParallel("抛竿直到成功或出错", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                    .UntilSuccess("重复抛竿")
                        .Sequence("-")
                            .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard, logger, false, input))
                            .PushLeaf(() => new ThrowRod("抛竿", blackboard, this.useTorch, logger, false, input, timeProvider, drawContent))
                        .End()
                    .End()
                    .Do("抛竿检查", _ => (blackboard.abort || blackboard.throwRodNoTarget || blackboard.throwRodNoBaitFish) ? BehaviourStatus.Failed : BehaviourStatus.Running)
                .End()
                .Build();
            sut.Tick(imageRegion);
            sut.Tick(imageRegion);
            timeProvider.Advance(TimeSpan.FromSeconds(5.1));
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.True(blackboard.throwRodNoTarget);
            Assert.Equal(1, blackboard.throwRodNoTargetTimes);
            Assert.Equal(BehaviourStatus.Failed, actual);

            //

            //
            sut.Tick(imageRegion);
            sut.Tick(imageRegion);
            timeProvider.Advance(TimeSpan.FromSeconds(5.1));
            actual = sut.Tick(imageRegion);

            //
            Assert.True(blackboard.throwRodNoTarget);
            Assert.Equal(2, blackboard.throwRodNoTargetTimes);
            Assert.Equal(BehaviourStatus.Failed, actual);

            //

            //
            sut.Tick(imageRegion);
            sut.Tick(imageRegion);
            timeProvider.Advance(TimeSpan.FromSeconds(5.1));
            actual = sut.Tick(imageRegion);

            //
            Assert.True(blackboard.throwRodNoTarget);
            Assert.Equal(3, blackboard.throwRodNoTargetTimes);
            Assert.Equal(BehaviourStatus.Failed, actual);
            Assert.True(blackboard.abort);
        }
    }
}
