using BehaviourTree;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using Microsoft.Extensions.Time.Testing;
using BehaviourTree.Composites;
using BehaviourTree.FluentBuilder;

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
        public void GetFishBoxArea_ShouldSuccess(string screenshot1080p)
        {
            //
            Bitmap bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0, drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(null, sleep: i => { });

            //
            GetFishBoxArea sut = new GetFishBoxArea("-", blackboard, new FakeLogger(), false);
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Succeeded, actual);
        }

        [Fact]
        /// <summary>
        /// 测试获取钓鱼拉扯框，超时后，结果为失败
        /// </summary>
        public void GetFishBoxArea_ShouldFail()
        {
            //
            Bitmap bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\202503012143011486@900p.png");
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0, drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(null, sleep: i => { });

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();

            //
            var sut = FluentBuilder.Create<ImageRegion>()
                .MySimpleParallel("-", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                    //.PushLeaf(() => new CheckRaiseHook("-", logger, false, fakeTimeProvider)) // todo
                    .Sequence("-")
                        .PushLeaf(() => new GetFishBoxArea("-", blackboard, logger, false, fakeTimeProvider))
                        .PushLeaf(() => new Fishing("-", blackboard, logger, false, new FakeInputSimulator(), fakeTimeProvider, new FakeDrawContent()))
                    .End()
                .End()
                .Build();
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\20250306111752769_GetFishBoxArea_Succeeded.png");
            imageRegion = new GameCaptureRegion(bitmap, 0, 0, drawContent: new FakeDrawContent());
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(6));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Failed, actual);
        }
    }
}
