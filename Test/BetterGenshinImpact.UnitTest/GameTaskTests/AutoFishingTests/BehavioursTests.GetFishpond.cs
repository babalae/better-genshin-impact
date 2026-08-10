using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using CsTrees;
using CsTrees.FluentBuilder;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Theory]
        [InlineData("20250225101257889_GetFishpond_Succeeded.png", new string[] { "medaka", "butterflyfish", "pufferfish", "stickleback" })]
        [InlineData("202502252347412417.png", new string[] { "medaka", "koi", "koi head" })]
        [InlineData("202502252350206390.png", new string[] { "phony unihornfish", "magma rapidfish" })]
        /// <summary>
        /// 测试各种鱼的获取，结果为成功
        /// </summary>
        public async Task GetFishpondTest_VariousFishExist_ShouldSuccess(string screenshot1080p, IEnumerable<string> fishNames)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, drawContent: new FakeDrawContent());

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var access = blackboard.GrantRead<Fishpond>(null!, "Fishpond");

            var sut = TreeBuilder.Create()
                .WithBlackboard(blackboard)
                    .Sequence("用例")
                        .SetSleep("设置sleep方法", _ => { })
                        .ScreenshotQueue("用例", [imageRegion, imageRegion])
                        .GetFishpond("-", new FakeLogger(), BehavioursTests.Predictor, new FakeTimeProvider(), drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();

            //
            Status actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Success, actual);
            foreach (var g in fishNames.GroupBy(n => n))
            {
                string fishName = g.Key;
                var fish = access.Get().Fishes.Where(f => f.FishType.Name == fishName);
                Assert.NotEmpty(fish);
            }
        }

        [Theory]
        [InlineData("20250225101257889_GetFishpond_Succeeded.png", new BaitType[] { BaitType.FruitPasteBait, BaitType.FruitPasteBait, BaitType.RedrotBait, BaitType.RedrotBait }, new BaitType[] { BaitType.FalseWormBait, BaitType.FalseWormBait, BaitType.FakeFlyBait, BaitType.FakeFlyBait })]
        /// 测试鱼的鱼饵均在失败列表中且被忽略，结果为运行中
        /// </summary>
        public async Task GetFishpondTest_AllIgnored_ShouldBeRunning(string screenshot1080p, IEnumerable<BaitType> chooseBaitfailures, IEnumerable<BaitType> throwRodNoTargetFishfailures)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, drawContent: new FakeDrawContent());

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var chooseBaitFailuresAccess = blackboard.GrantWrite<List<BaitType>>(null!, "ChooseBaitFailures");
            var throwRodNoBaitFishFailuresAccess = blackboard.GrantWrite<List<BaitType>>(null!, "ThrowRodNoBaitFishFailures");
            chooseBaitFailuresAccess.Set(chooseBaitfailures.ToList());
            throwRodNoBaitFishFailuresAccess.Set(throwRodNoTargetFishfailures.ToList());
            var fishpondAccess = blackboard.GrantRead<Fishpond>(null!, "Fishpond");

            var sut = TreeBuilder.Create()
                .WithBlackboard(blackboard)
                    .Sequence("用例")
                        .SetSleep("设置sleep方法", _ => { })
                        .ScreenshotQueue("用例", [imageRegion])
                        .GetFishpond("-", new FakeLogger(), BehavioursTests.Predictor, new FakeTimeProvider(), drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();

            //
            Status actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Running, actual);
            Assert.NotEmpty(fishpondAccess.Get().Fishes);
        }

        [Theory]
        [InlineData("20250225101257889_GetFishpond_Succeeded.png", "medaka", 1)]
        [InlineData("20250301192848793_GetFishpond_Succeeded.png", "medaka", 2)]
        [InlineData("20250226161354285_ChooseBait_Succeeded.png", "medaka", 0)]
        [InlineData("202503012143011486@900p.png", "medaka", 0)]
        [InlineData("20250301231059172_GetFishpond_Succeeded.png", "medaka", 0)]
        [InlineData("20250301234659009_GetFishpond_Succeeded.png", "axe marlin", 2)]
        [InlineData("20250301235638915_GetFishpond_Succeeded.png", "butterflyfish", 1)]
        [InlineData("20250302001049589_GetFishpond_Succeeded.png", "axe marlin", 0)]
        [InlineData("20250306165029475_GetFishpond_Succeeded.png", "butterflyfish", 0)]
        [InlineData("20250306171545590_GetFishpond_Succeeded.png", "heartfeather bass", 0)]
        /// <summary>
        /// 测试各种鱼的获取数量，数量应相符
        /// </summary>
        public async Task GetFishpondTest_FishCount_ShouldSuccess(string screenshot1080p, string fishName, int count)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, drawContent: new FakeDrawContent());

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var fishpondAccess = blackboard.GrantRead<Fishpond>(null!, "Fishpond");

            var sut = TreeBuilder.Create()
                .WithBlackboard(blackboard)
                    .Sequence("用例")
                        .SetSleep("设置sleep方法", _ => { })
                        .ScreenshotQueue("用例", [imageRegion])
                        .GetFishpond("-", new FakeLogger(), BehavioursTests.Predictor, new FakeTimeProvider(), drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();

            //
            Status status = await sut.TickOnce();
            int actual = fishpondAccess.Exists() ? (fishpondAccess.Get().Fishes?.Count(f => f.FishType.Name == fishName) ?? 0) : 0;

            //
            Assert.Equal(count, actual);
        }
    }
}
