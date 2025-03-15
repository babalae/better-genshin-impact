using BehaviourTree;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.GameTask.Model.Area;
using System.Drawing;
using BehaviourTree.Composites;
using BehaviourTree.FluentBuilder;
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
        public void FishBite_ShouldSuccess(string screenshot1080p)
        {
            //
            Mat bitmap = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            //
            FishBite sut = new FishBite("-", new FakeLogger(), false, new FakeInputSimulator(), drawContent: new FakeDrawContent());
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Succeeded, actual);
        }

        [Theory]
        [InlineData(@"20250306111749714_CheckThrowRod_Succeeded.png", @"20250306111752053_FishBite_Succeeded.png")]
        /// <summary>
        /// 测试鱼咬钩超时，在超时提竿时鱼咬钩了，结果为成功
        /// </summary>
        public void FishBite_Tree_Timeout_ShouldSuccess(string screenshot1080pCheckThrowRod, string screenshot1080pFishBite)
        {
            //
            Mat bitmap = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080pCheckThrowRod}");
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();
            FakeInputSimulator input = new FakeInputSimulator();

            //
            FishBiteTimeout fishBiteTimeoutBehaviour = new FishBiteTimeout("-", 15, logger, false, input, fakeTimeProvider);
            var sut = FluentBuilder.Create<ImageRegion>()
                .MySimpleParallel("-", SimpleParallelPolicy.OnlyOneMustSucceed)
                    //.PushLeaf(() => new CheckThrowRod("-", logger, false, fakeTimeProvider)) // todo
                    .PushLeaf(() => new FishBite("-", logger, false, input, drawContent: new FakeDrawContent()))
                    .PushLeaf(() => fishBiteTimeoutBehaviour)
                .End()
                .Build();
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actual);
            Assert.False(fishBiteTimeoutBehaviour.leftButtonClicked);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(15));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actual);
            Assert.True(fishBiteTimeoutBehaviour.leftButtonClicked);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            bitmap = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080pFishBite}");
            imageRegion = new GameCaptureRegion(bitmap, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Succeeded, actual);
            Assert.True(fishBiteTimeoutBehaviour.leftButtonClicked);
        }
    }
}
