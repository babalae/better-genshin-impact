using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using CsTrees;
using CsTrees.Composites;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Theory]
        [InlineData(@"20250306111752053_FishBite_Succeeded.png")]
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png")]
        [InlineData(@"20250314164703100_FishBite_Succeeded_FP.png")]   // 假阳性
        /// <summary>
        /// 测试鱼咬钩，结果为成功
        /// </summary>
        public async Task FishBite_ShouldSuccess(string screenshot1080p)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            //
            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion], bb!))
                        .CheckFishBite("-", new FakeLogger(), OcrService, drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();
            Status actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Success, actual);
        }

        [Theory]
        [InlineData(@"20250306111749714_CheckThrowRod_Succeeded.png", @"20250306111752053_FishBite_Succeeded.png")]
        /// <summary>
        /// 测试鱼咬钩超时，在超时提竿时鱼咬钩了，整体也能成功
        /// 通过先超时提竿，但继续检查咬钩一定时间，来保证咬钩一定能被后续拉条处理
        /// </summary>
        public async Task FishBite_Tree_Timeout_ShouldSuccess(string screenshot1080pCheckThrowRod, string screenshot1080pFishBite)
        {
            //
            Mat mat1 = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080pCheckThrowRod}");
            var imageRegion1 = new GameCaptureRegion(mat1, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            Mat mat2 = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080pFishBite}");
            var imageRegion2 = new GameCaptureRegion(mat2, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();
            FakeInputSimulator input = new FakeInputSimulator();

            //
            FishBiteTimeout fishBiteTimeoutBehaviour = new FishBiteTimeout("-", 15, logger, input, blackboard, fakeTimeProvider);
            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion1, imageRegion1, imageRegion2], bb!))
                        .Parallel("-", new ParallelPolicy.SuccessOnOne())
                            .CheckFishBite("-", logger, OcrService, drawContent: new FakeDrawContent())
                            .Leaf(() => fishBiteTimeoutBehaviour)
                        .End()
                    .End()
                .End()
                .Build();
            Status actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Running, actual);
            Assert.False(fishBiteTimeoutBehaviour.leftButtonClicked);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(15));

            //
            actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Running, actual);
            Assert.True(fishBiteTimeoutBehaviour.leftButtonClicked);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));

            //
            actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Success, actual);
            Assert.True(fishBiteTimeoutBehaviour.leftButtonClicked);
        }

        [Theory]
        [InlineData(@"202503230049406101_en.png", "en")]  // 一张移除了右下角按钮的咬钩截图
        /// <summary>
        /// 测试外语鱼咬钩，结果为成功
        /// </summary>
        public async Task FishBite_English_ShouldSuccess(string screenshot1080p, string cultureName)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            //
            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion], bb!))
                        .CheckFishBite("-", new FakeLogger(), OcrService, drawContent: new FakeDrawContent(), new System.Globalization.CultureInfo(cultureName), stringLocalizer)
                    .End()
                .End()
                .Build();
            Status actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Success, actual);
        }
    }
}
