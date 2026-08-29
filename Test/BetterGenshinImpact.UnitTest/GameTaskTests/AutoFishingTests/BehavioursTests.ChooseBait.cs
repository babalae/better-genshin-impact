using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.Helpers.Extensions;
using CsTrees;
using CsTrees.Blackboard;
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
        [Fact]
        /// <summary>
        /// 测试识别数量不足的鱼饵，由于图标变灰，识别应失败
        /// </summary>
        public void FindBaitTest_RecognitionShouldFail()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\202509141339218213_ChooseBait.png");
            var imageRegion = new ImageRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);

            //
            ChooseBait sut = new ChooseBait("-", new FakeLogger(), systemInfo, new FakeInputSimulator(), this.session, this.prototypes, new Blackboard());
            var result = sut.FindBait(imageRegion).OrderBy(r => r.Item1.X).ToArray();

            //
            Assert.Equal(3, result.Length);
            Assert.Equal(BaitType.FruitPasteBait.GetDescription(), result[0].Item2);
            Assert.Equal(BaitType.BerryBait.GetDescription(), result[1].Item2);
            Assert.Null(result[2].Item2);
        }

        [Theory]
        [InlineData(@"20250225101300361_ChooseBait_Succeeded.png", new string[] { "medaka", "butterflyfish", "butterflyfish", "pufferfish" })]
        [InlineData(@"20250226161354285_ChooseBait_Succeeded.png", new string[] { "medaka" })]  // 不稳定的测试用例，因未学习被照亮的场景
        [InlineData(@"202503160917566615@900p.png", new string[] { "pufferfish" })]
        [InlineData(@"202509141339218213_ChooseBait.png", new string[] { "axehead" })]
        [InlineData(@"202509141339218213_ChooseBait.png", new string[] { "mauler shark", "crystal eye", "medaka", "medaka", "medaka" })]
        /// <summary>
        /// 测试各种选取鱼饵，结果为成功
        /// </summary>
        public async Task ChooseBaitTest_VariousBait_ShouldSuccess(string screenshot1080p, IEnumerable<string> fishNames)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new ImageRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var access = blackboard.GrantWrite<Fishpond>(null!, "Fishpond");
            access.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));

            //
            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion, imageRegion], bb!))
                        .ChooseBait("-", new FakeLogger(), systemInfo, new FakeInputSimulator(), this.session, this.prototypes)
                    .End()
                .End()
                .Build();

            Status actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Running, actual);

            //
            actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Success, actual);
        }

        [Theory]
        [InlineData(@"20250226161354285_ChooseBait_Succeeded.png", new string[] { "koi" })]
        [InlineData(@"202509141339218213_ChooseBait.png", new string[] { "mauler shark", "crystal eye" })]
        /// <summary>
        /// 测试各种选取鱼饵，结果为失败
        /// </summary>
        public async Task ChooseBaitTest_VariousBait_ShouldFail(string screenshot1080p, IEnumerable<string> fishNames)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new ImageRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var fishpondAccess = blackboard.GrantWrite<Fishpond>(null!, "Fishpond");
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));
            var selectedBaitAccess = blackboard.GrantRead<BaitType?>(null!, "SelectedBait");
            var chooseBaitUIOpeningAccess = blackboard.GrantRead<bool>(null!, "ChooseBaitUIOpening");

            DateTimeOffset dateTime = new DateTimeOffset(2025, 2, 26, 16, 13, 54, 285, TimeSpan.FromHours(8));
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider(dateTime);

            //
            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion, imageRegion, imageRegion], bb!))
                        .ChooseBait("-", new FakeLogger(), systemInfo, new FakeInputSimulator(), this.session, this.prototypes, fakeTimeProvider)
                    .End()
                .End()
                .Build();

            Status actual = await sut.TickOnce();

            //
            Assert.False(selectedBaitAccess.Exists());
            Assert.True(chooseBaitUIOpeningAccess.Get());
            Assert.Equal(Status.Running, actual);

            //
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(1));

            //
            actual = await sut.TickOnce();

            //
            Assert.True(selectedBaitAccess.Exists());
            Assert.True(chooseBaitUIOpeningAccess.Get());
            Assert.Equal(Status.Running, actual);

            //
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(3));

            //
            actual = await sut.TickOnce();

            //
            Assert.Null(selectedBaitAccess.Get());
            Assert.False(chooseBaitUIOpeningAccess.Get());
            Assert.Equal(Status.Failure, actual);
        }

        /// <summary>
        /// 测试选鱼饵失败若干次，失败列表应符合预期
        /// 这个测试侧重连续选鱼饵失败、两次选鱼饵失败之间穿插一次选鱼饵成功的情况
        /// </summary>
        [Fact]
        public async Task ChooseBaitTest_AllBaitIgnored_Case1_FailureListShouldBeExpected()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\20250226161354285_ChooseBait_Succeeded.png");
            var imageRegion = new ImageRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var fishpondAccess = blackboard.GrantWrite<Fishpond>(null!, "Fishpond");
            IEnumerable<string> fishNames = new string[] { "sunfish", "koi", "koi head", "medaka" };
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));
            var selectedBaitAccess = blackboard.GrantRead<BaitType?>(null!, "SelectedBait");
            var chooseBaitFailuresAccess = blackboard.GrantRead<List<BaitType>>(null!, "ChooseBaitFailures");
            var abortAccess = blackboard.GrantRead<bool>(null!, "Abort");

            DateTimeOffset dateTime = new DateTimeOffset(2025, 2, 26, 16, 13, 54, 285, TimeSpan.FromHours(8));
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider(dateTime);

            #region 第1次失败
            //
            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(imageRegion, 10), bb!))
                        .ChooseBait("-", new FakeLogger(), systemInfo, new FakeInputSimulator(), this.session, this.prototypes, fakeTimeProvider)
                    .End()
                .End()
                .Build();

            await sut.TickOnce();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(3));
            Status actual = await sut.TickOnce();

            //
            Assert.Null(selectedBaitAccess.Get());
            Assert.Equal(Status.Failure, actual);
            Assert.Single(chooseBaitFailuresAccess.Get().Where(f => f == BaitType.FakeFlyBait));
            #endregion

            #region 第2次失败
            //
            //blackboard.Clear();
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(10));

            //
            await sut.TickOnce();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(13));
            actual = await sut.TickOnce();

            //
            Assert.Null(selectedBaitAccess.Get());
            Assert.Equal(Status.Failure, actual);
            Assert.Equal(2, chooseBaitFailuresAccess.Get().Where(f => f == BaitType.FakeFlyBait).Count());
            Assert.False(abortAccess.Exists());
            #endregion

            #region medaka受到遮挡，第3次失败
            //
            //blackboard.Clear();
            fishNames = new string[] { "koi", "koi head", "sunfish" };
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));

            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(20));

            //
            await sut.TickOnce();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(23));
            actual = await sut.TickOnce();

            //
            Assert.Null(selectedBaitAccess.Get());
            Assert.Equal(Status.Failure, actual);
            Assert.Single(chooseBaitFailuresAccess.Get().Where(f => f == BaitType.SpinelgrainBait));
            #endregion

            #region sunfish受到遮挡，medaka再次出现，第4次成功，并钓起medaka
            //
            //blackboard.Clear();
            fishNames = new string[] { "koi", "koi head", "medaka" };
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(30));

            //
            await sut.TickOnce();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(33));
            actual = await sut.TickOnce();

            //
            Assert.True(selectedBaitAccess.Exists());    // todo 更新用例
            Assert.Equal(Status.Success, actual);
            Assert.Single(chooseBaitFailuresAccess.Get().Where(f => f == BaitType.SpinelgrainBait));
            #endregion

            #region sunfish再次出现，第5次失败
            //
            //blackboard.Clear();
            fishNames = new string[] { "koi", "koi head", "sunfish" };
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(40));

            //
            await sut.TickOnce();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(43));
            actual = await sut.TickOnce();

            //
            Assert.Null(selectedBaitAccess.Get());
            Assert.Equal(Status.Failure, actual);
            Assert.Equal(2, chooseBaitFailuresAccess.Get().Where(f => f == BaitType.SpinelgrainBait).Count());
            #endregion
        }

        /// <summary>
        /// 测试选鱼饵失败若干次，失败列表应符合预期
        /// 这个测试侧重两种鱼饵交替失败的情况
        /// </summary>
        [Fact]
        public async Task ChooseBaitTest_AllBaitIgnored_Case2_FailureListShouldBeExpected()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\20250226161354285_ChooseBait_Succeeded.png");
            var imageRegion = new ImageRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d));

            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var fishpondAccess = blackboard.GrantWrite<Fishpond>(null!, "Fishpond");
            IEnumerable<string> fishNames = new string[] { "koi", "koi head", "sunfish" };
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));
            var selectedBaitAccess = blackboard.GrantRead<BaitType?>(null!, "SelectedBait");
            var chooseBaitFailuresAccess = blackboard.GrantRead<List<BaitType>>(null!, "ChooseBaitFailures");
            var abortAccess = blackboard.GrantRead<bool>(null!, "Abort");

            DateTimeOffset dateTime = new DateTimeOffset(2025, 2, 26, 16, 13, 54, 285, TimeSpan.FromHours(8));
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider(dateTime);

            #region 第1次失败
            //
            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(imageRegion, 8), bb!))
                        .ChooseBait("-", new FakeLogger(), systemInfo, new FakeInputSimulator(), this.session, this.prototypes, fakeTimeProvider)
                    .End()
                .End()
                .Build();

            await sut.TickOnce();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(3));
            var actual = await sut.TickOnce();

            //
            Assert.Null(selectedBaitAccess.Get());
            Assert.Equal(Status.Failure, actual);
            Assert.Single(chooseBaitFailuresAccess.Get().Where(f => f == BaitType.FakeFlyBait));
            #endregion

            #region koi受到遮挡，第2次失败
            //
            fishNames = new string[] { "sunfish" };
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(10));

            //
            await sut.TickOnce();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(13));
            actual = await sut.TickOnce();

            //
            Assert.Null(selectedBaitAccess.Get());
            Assert.Equal(Status.Failure, actual);
            Assert.Single(chooseBaitFailuresAccess.Get().Where(f => f == BaitType.SpinelgrainBait));
            Assert.False(abortAccess.Exists());
            #endregion

            #region koi再次出现，第3次失败
            //
            fishNames = new string[] { "koi", "koi head", "sunfish" };
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(20));

            //
            await sut.TickOnce();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(23));
            actual = await sut.TickOnce();

            //
            Assert.Null(selectedBaitAccess.Get());
            Assert.Equal(Status.Failure, actual);
            Assert.Equal(2, chooseBaitFailuresAccess.Get().Where(f => f == BaitType.FakeFlyBait).Count());
            #endregion

            #region 第4次失败
            //
            fishNames = new string[] { "koi", "koi head", "sunfish" };
            fishpondAccess.Set(new Fishpond(fishNames.Select(n => new OneFish(n, default, 0)).ToList()));
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(40));

            //
            await sut.TickOnce();
            fakeTimeProvider.SetUtcNow(dateTime.AddSeconds(43));
            actual = await sut.TickOnce();

            //
            Assert.Null(selectedBaitAccess.Get());
            Assert.Equal(Status.Failure, actual);
            Assert.Equal(2, chooseBaitFailuresAccess.Get().Where(f => f == BaitType.SpinelgrainBait).Count());
            #endregion
        }
    }
}
