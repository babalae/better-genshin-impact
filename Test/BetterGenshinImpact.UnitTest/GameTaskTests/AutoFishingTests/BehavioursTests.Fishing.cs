using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area;
using CsTrees;
using CsTrees.FluentBuilder;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Theory]
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png", @"20250306111752769_GetFishBoxArea_Succeeded.png")]
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png", @"202503140802528967.png")]
        [InlineData(@"202503140845524752.png", @"202503140802528967.png")]
        [InlineData(@"202503140845572301.png", @"202503140802528967.png")]
        /// <summary>
        /// 测试获取钓鱼拉扯框，结果为运行中
        /// </summary>
        public async Task Fishing_ShouldBeRunning(string screenshot1080pGetFishBoxArea, string screenshot1080p)
        {
            //
            FakeDrawContent fakeDrawContent = new FakeDrawContent();
            Mat mat1 = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080pGetFishBoxArea}");
            var imageRegion1 = new GameCaptureRegion(mat1, 0, 0, drawContent: fakeDrawContent);
            var mat2 = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion2 = new GameCaptureRegion(mat2, 0, 0, drawContent: fakeDrawContent);
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            var sut = TreeBuilder.Create()
                .WithBlackboard(blackboard)
                    .Sequence("用例")
                        .ScreenshotQueue("用例", [imageRegion1, imageRegion2, imageRegion2])
                        .SequenceWithMemory("-")
                            .GetFishBoxArea("-", logger, false)
                            .Fishing("-", logger, false, new FakeInputSimulator(), fakeTimeProvider, drawContent: fakeDrawContent)
                        .End()
                    .End()
                .End()
                .Build();

            //
            await sut.TickOnce();
            Status actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Running, actual);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));

            //
            actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Running, actual);
        }

        [Theory]
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png", @"20250314002439020_Fishing_Succeeded.png")]
        /// <summary>
        /// 测试获取钓鱼拉扯框，由于界面效果，拉扯框无法被识别或消失，结果为成功
        /// </summary>
        public async Task Fishing_ShouldSuccess(string screenshot1080pGetFishBoxArea, string screenshot1080p)
        {
            //
            FakeDrawContent fakeDrawContent = new FakeDrawContent();
            Mat mat1 = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080pGetFishBoxArea}");
            var imageRegion1 = new GameCaptureRegion(mat1, 0, 0, drawContent: fakeDrawContent);
            var mat2 = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion2 = new GameCaptureRegion(mat2, 0, 0, drawContent: fakeDrawContent);
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            var sut = TreeBuilder.Create()
                .WithBlackboard(blackboard)
                    .Sequence("用例")
                        .ScreenshotQueue("用例", [imageRegion1, imageRegion2, imageRegion2])
                        .SequenceWithMemory("-")
                            .GetFishBoxArea("-", logger, false)
                            .Fishing("-", logger, false, new FakeInputSimulator(), fakeTimeProvider, drawContent: fakeDrawContent)
                        .End()
                    .End()
                .End()
                .Build();

            //
            await sut.TickOnce();
            Status actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Running, actual);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));

            //
            actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Success, actual);
        }
    }
}
