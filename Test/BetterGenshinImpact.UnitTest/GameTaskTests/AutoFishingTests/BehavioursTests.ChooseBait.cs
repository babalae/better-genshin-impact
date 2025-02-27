using BehaviourTree;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
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
        [InlineData(@"20250225101300361_ChooseBait_Succeeded.png", new string[] { "medaka", "butterflyfish", "butterflyfish", "pufferfish" })]
        [InlineData(@"20250226161354285_ChooseBait_Succeeded.png", new string[] { "medaka", "medaka" })]
        /// <summary>
        /// 测试各种选取鱼饵，结果为成功
        /// </summary>
        public void ChooseBaitTest_VariousBait_ShouldSuccess(string screenshot1080p, IEnumerable<string> fishNames)
        {
            //
            Bitmap bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new ImageRegion(bitmap, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            var blackboard = new Blackboard(null, sleep: i => { })
            {
                fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, OpenCvSharp.Rect.Empty, 0)).ToList())
            };

            //
            ChooseBait sut = new ChooseBait("-", blackboard, new FakeLogger(), new FakeInputSimulator());
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Succeeded, actual);
        }

        [Theory]
        [InlineData(@"20250226161354285_ChooseBait_Succeeded.png", new string[] { "koi" })]
        /// <summary>
        /// 测试各种选取鱼饵，结果为失败
        /// </summary>
        public void ChooseBaitTest_VariousBait_ShouldFail(string screenshot1080p, IEnumerable<string> fishNames)
        {
            //
            Bitmap bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new ImageRegion(bitmap, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            var blackboard = new Blackboard(null, sleep: i => { })
            {
                fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, OpenCvSharp.Rect.Empty, 0)).ToList())
            };

            DateTimeOffset dateTime = new DateTimeOffset(2025, 2, 26, 16, 13, 54, 285, TimeSpan.FromHours(8));
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider(dateTime);

            //
            ChooseBait sut = new ChooseBait("-", blackboard, new FakeLogger(), new FakeInputSimulator(), fakeTimeProvider);
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.True(String.IsNullOrEmpty(blackboard.selectedBaitName));
            Assert.True(blackboard.chooseBaitUIOpening);
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(1));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.False(String.IsNullOrEmpty(blackboard.selectedBaitName));
            Assert.True(blackboard.chooseBaitUIOpening);
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(3));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.True(String.IsNullOrEmpty(blackboard.selectedBaitName));
            Assert.False(blackboard.chooseBaitUIOpening);
            Assert.Equal(BehaviourStatus.Failed, actual);
        }
    }
}
