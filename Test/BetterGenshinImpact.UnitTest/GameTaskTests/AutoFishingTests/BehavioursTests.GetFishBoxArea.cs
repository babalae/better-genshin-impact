using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area;
using CsTrees;
using CsTrees.Composites;
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
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png")]
        [InlineData(@"202503140845524752.png")]
        [InlineData(@"202503140845572301.png")]
        /// <summary>
        /// 测试获取钓鱼拉扯框，结果为成功
        /// </summary>
        public async Task GetFishBoxArea_ShouldSuccess(string screenshot1080p)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0,  drawContent: new FakeDrawContent());

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            var sut = TreeBuilder.Create()
                .WithBlackboard(blackboard)
                    .Sequence("用例")
                        .ScreenshotQueue("用例", [imageRegion])
                        .GetFishBoxArea("-", new FakeLogger(), false)
                    .End()
                .End()
                .Build();

            //
            Status actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Success, actual);
        }

        [Fact]
        /// <summary>
        /// 测试获取钓鱼拉扯框，超时后，结果为失败
        /// </summary>
        public async Task GetFishBoxArea_ShouldFail()
        {
            //
            Mat mat1 = new Mat(@$"..\..\..\Assets\AutoFishing\202503012143011486@900p.png");
            var imageRegion1 = new GameCaptureRegion(mat1, 0, 0, drawContent: new FakeDrawContent());
            Mat mat2 = new Mat(@$"..\..\..\Assets\AutoFishing\20250306111752769_GetFishBoxArea_Succeeded.png");
            var imageRegion2 = new GameCaptureRegion(mat2, 0, 0, drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            var sut = TreeBuilder.Create()
                .WithBlackboard(blackboard)
                    .Sequence("用例")
                        .ScreenshotQueue("用例", [imageRegion1, imageRegion2])
                        .Parallel("-", new ParallelPolicy.SuccessOnOne())
                            .CheckRaiseHook("-", logger, fakeTimeProvider)
                            .SequenceWithMemory("-")
                                .GetFishBoxArea("-", logger, false, fakeTimeProvider)
                                .Fishing("-", logger, false, new FakeInputSimulator(), fakeTimeProvider, drawContent: new FakeDrawContent())
                            .End()
                        .End()
                    .End()
                .End()
                .Build();

            //
            Status actual = await sut.TickOnce();

            //
            string snapshot = CsTrees.Display.Display.AsciiTree(sut, showStatus: true);
            Assert.Equal(Status.Running, actual);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(6));

            //
            actual = await sut.TickOnce();

            //
            snapshot = CsTrees.Display.Display.AsciiTree(sut, showStatus: true);
            Assert.Equal(Status.Failure, actual);
        }
    }
}
