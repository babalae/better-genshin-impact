using BehaviourTree;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Time.Testing;
using System;
using System.Collections.Generic;
using System.Drawing;
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
        public void Fishing_ShouldBeRunning(string screenshot1080pGetFishBoxArea, string screenshot1080p)
        {
            //
            Bitmap bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080pGetFishBoxArea}");
            FakeDrawContent fakeDrawContent = new FakeDrawContent();
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0, drawContent: fakeDrawContent);

            var blackboard = new Blackboard(null, sleep: i => { });

            GetFishBoxArea getFishBoxArea = new GetFishBoxArea("-", blackboard, new FakeLogger(), false);
            getFishBoxArea.Tick(imageRegion);

            bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            imageRegion = new GameCaptureRegion(bitmap, 0, 0, drawContent: fakeDrawContent);

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            //
            Fishing sut = new Fishing("-", blackboard, new FakeLogger(), false, new FakeInputSimulator(), fakeTimeProvider, drawContent: fakeDrawContent);
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actual);
        }

        [Theory]
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png", @"20250314002439020_Fishing_Succeeded.png")]
        /// <summary>
        /// 测试获取钓鱼拉扯框，由于界面效果，拉扯框无法被识别或消失，结果为成功
        /// </summary>
        public void Fishing_ShouldSuccess(string screenshot1080pGetFishBoxArea, string screenshot1080p)
        {
            //
            Bitmap bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080pGetFishBoxArea}");
            FakeDrawContent fakeDrawContent = new FakeDrawContent();
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0, drawContent: fakeDrawContent);

            var blackboard = new Blackboard(null, sleep: i => { });

            GetFishBoxArea getFishBoxArea = new GetFishBoxArea("-", blackboard, new FakeLogger(), false);
            getFishBoxArea.Tick(imageRegion);

            bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            imageRegion = new GameCaptureRegion(bitmap, 0, 0, drawContent: fakeDrawContent);

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            //
            Fishing sut = new Fishing("-", blackboard, new FakeLogger(), false, new FakeInputSimulator(), fakeTimeProvider, drawContent: fakeDrawContent);
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Succeeded, actual);
        }
    }
}
