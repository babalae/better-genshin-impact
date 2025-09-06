using BehaviourTree;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
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
        [InlineData(@"20250225101300361_ChooseBait_Succeeded.png", new string[] { "medaka", "butterflyfish", "butterflyfish", "pufferfish" })]
        [InlineData(@"20250226161354285_ChooseBait_Succeeded.png", new string[] { "medaka", "medaka" })]    // todo 更新用例
        [InlineData(@"202503160917566615@900p.png", new string[] { "pufferfish" })]
        /// <summary>
        /// 测试各种选取鱼饵，结果为成功
        /// </summary>
        public void ChooseBaitTest_VariousBait_ShouldSuccess(string screenshot1080p, IEnumerable<string> fishNames)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new ImageRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);
            var blackboard = new Blackboard(sleep: i => { })
            {
                fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList())
            };

            //
            ChooseBait sut = new ChooseBait("-", blackboard, new FakeLogger(), false, systemInfo, new FakeInputSimulator(), this.session, this.prototypes);
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
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new ImageRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);
            var blackboard = new Blackboard(sleep: i => { })
            {
                fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList())
            };

            DateTimeOffset dateTime = new DateTimeOffset(2025, 2, 26, 16, 13, 54, 285, TimeSpan.FromHours(8));
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider(dateTime);

            //
            ChooseBait sut = new ChooseBait("-", blackboard, new FakeLogger(), false, systemInfo, new FakeInputSimulator(), this.session, this.prototypes, fakeTimeProvider);
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.True(blackboard.chooseBaitUIOpening);
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(1));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.NotNull(blackboard.selectedBait);
            Assert.True(blackboard.chooseBaitUIOpening);
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(3));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.False(blackboard.chooseBaitUIOpening);
            Assert.Equal(BehaviourStatus.Failed, actual);
        }

        /// <summary>
        /// 测试选鱼饵失败若干次，失败列表应符合预期
        /// 这个测试侧重连续选鱼饵失败、两次选鱼饵失败之间穿插一次选鱼饵成功的情况
        /// </summary>
        [Fact]
        public void ChooseBaitTest_AllBaitIgnored_Case1_FailureListShouldBeExpected()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\20250226161354285_ChooseBait_Succeeded.png");
            var imageRegion = new ImageRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            IEnumerable<string> fishNames = new string[] { "sunfish", "koi", "koi head", "medaka" };

            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);
            var blackboard = new Blackboard(sleep: i => { })
            {
                fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList())
            };

            DateTimeOffset dateTime = new DateTimeOffset(2025, 2, 26, 16, 13, 54, 285, TimeSpan.FromHours(8));
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider(dateTime);

            #region 第1次失败
            //
            ChooseBait sut = new ChooseBait("-", blackboard, new FakeLogger(), false, systemInfo, new FakeInputSimulator(), this.session, this.prototypes, fakeTimeProvider);
            BehaviourStatus actual = sut.Tick(imageRegion);
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(3));
            actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.Equal(BehaviourStatus.Failed, actual);
            Assert.Single(blackboard.chooseBaitFailures.Where(f => f == BaitType.FakeFlyBait));
            #endregion

            #region 第2次失败
            //
            sut.Reset();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(10));

            //
            actual = sut.Tick(imageRegion);
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(13));
            actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.Equal(BehaviourStatus.Failed, actual);
            Assert.Equal(2, blackboard.chooseBaitFailures.Where(f => f == BaitType.FakeFlyBait).Count());
            Assert.False(blackboard.abort);
            #endregion

            #region medaka受到遮挡，第3次失败
            //
            fishNames = new string[] { "koi", "koi head", "sunfish" };
            blackboard.fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList());
            sut.Reset();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(20));

            //
            actual = sut.Tick(imageRegion);
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(23));
            actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.Equal(BehaviourStatus.Failed, actual);
            Assert.Single(blackboard.chooseBaitFailures.Where(f => f == BaitType.SpinelgrainBait));
            #endregion

            #region sunfish受到遮挡，medaka再次出现，第4次成功，并钓起medaka
            //
            fishNames = new string[] { "koi", "koi head", "medaka" };
            blackboard.fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList());
            sut.Reset();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(30));

            //
            actual = sut.Tick(imageRegion);
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(33));
            actual = sut.Tick(imageRegion);

            //
            Assert.NotNull(blackboard.selectedBait);    // todo 更新用例
            Assert.Equal(BehaviourStatus.Succeeded, actual);
            Assert.Single(blackboard.chooseBaitFailures.Where(f => f == BaitType.SpinelgrainBait));
            #endregion

            #region sunfish再次出现，第5次失败
            //
            fishNames = new string[] { "koi", "koi head", "sunfish" };
            blackboard.fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList());
            sut.Reset();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(40));

            //
            actual = sut.Tick(imageRegion);
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(43));
            actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.Equal(BehaviourStatus.Failed, actual);
            Assert.Equal(2, blackboard.chooseBaitFailures.Where(f => f == BaitType.SpinelgrainBait).Count());
            #endregion
        }

        /// <summary>
        /// 测试选鱼饵失败若干次，失败列表应符合预期
        /// 这个测试侧重两种鱼饵交替失败的情况
        /// </summary>
        [Fact]
        public void ChooseBaitTest_AllBaitIgnored_Case2_FailureListShouldBeExpected()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\20250226161354285_ChooseBait_Succeeded.png");
            var imageRegion = new ImageRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);
            IEnumerable<string> fishNames = new string[] { "koi", "koi head", "sunfish" };
            var blackboard = new Blackboard(sleep: i => { })
            {
                fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList())
            };

            DateTimeOffset dateTime = new DateTimeOffset(2025, 2, 26, 16, 13, 54, 285, TimeSpan.FromHours(8));
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider(dateTime);

            #region 第1次失败
            //
            ChooseBait sut = new ChooseBait("-", blackboard, new FakeLogger(), false, systemInfo, new FakeInputSimulator(), this.session, this.prototypes, fakeTimeProvider);
            BehaviourStatus actual = sut.Tick(imageRegion);
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(3));
            actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.Equal(BehaviourStatus.Failed, actual);
            Assert.Single(blackboard.chooseBaitFailures.Where(f => f == BaitType.FakeFlyBait));
            #endregion

            #region koi受到遮挡，第2次失败
            //
            fishNames = new string[] { "sunfish" };
            blackboard.fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList());
            sut.Reset();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(10));

            //
            actual = sut.Tick(imageRegion);
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(13));
            actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.Equal(BehaviourStatus.Failed, actual);
            Assert.Single(blackboard.chooseBaitFailures.Where(f => f == BaitType.SpinelgrainBait));
            Assert.False(blackboard.abort);
            #endregion

            #region koi再次出现，第3次失败
            //
            fishNames = new string[] { "koi", "koi head", "sunfish" };
            blackboard.fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList());
            sut.Reset();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(20));

            //
            actual = sut.Tick(imageRegion);
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(23));
            actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.Equal(BehaviourStatus.Failed, actual);
            Assert.Equal(2, blackboard.chooseBaitFailures.Where(f => f == BaitType.FakeFlyBait).Count());
            #endregion

            #region 第4次失败
            //
            fishNames = new string[] { "koi", "koi head", "sunfish" };
            blackboard.fishpond = new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList());
            sut.Reset();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(40));

            //
            actual = sut.Tick(imageRegion);
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(43));
            actual = sut.Tick(imageRegion);

            //
            Assert.Null(blackboard.selectedBait);
            Assert.Equal(BehaviourStatus.Failed, actual);
            Assert.Equal(2, blackboard.chooseBaitFailures.Where(f => f == BaitType.SpinelgrainBait).Count());
            #endregion
        }
    }
}
