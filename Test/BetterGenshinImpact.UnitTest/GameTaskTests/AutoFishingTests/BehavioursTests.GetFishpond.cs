using BetterGenshinImpact.GameTask.AutoFishing;
using BehaviourTree;
using BetterGenshinImpact.GameTask.Model.Area;
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
        public void GetFishpondTest_VariousFishExist_ShouldSuccess(string screenshot1080p, IEnumerable<string> fishNames)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(Predictor, sleep: i => { });

            //
            GetFishpond sut = new GetFishpond("-", blackboard, new FakeLogger(), false, new FakeTimeProvider(), drawContent: new FakeDrawContent());
            BehaviourStatus actualStatus = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Succeeded, actualStatus);
            foreach (var g in fishNames.GroupBy(n => n))
            {
                string fishName = g.Key;
                var fish = blackboard.fishpond.Fishes.Where(f => f.FishType.Name == fishName);
                Assert.NotEmpty(fish);
            }
        }

        [Theory]
        [InlineData("20250225101257889_GetFishpond_Succeeded.png", new string[] { "fruit paste bait", "fruit paste bait", "redrot bait", "redrot bait" }, new string[] { "false worm bait", "false worm bait", "fake fly bait", "fake fly bait" })]
        /// 测试鱼的鱼饵均在失败列表中且被忽略，结果为运行中
        /// </summary>
        public void GetFishpondTest_AllIgnored_ShouldBeRunning(string screenshot1080p, IEnumerable<string> chooseBaitfailures, IEnumerable<string> throwRodNoTargetFishfailures)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(Predictor, sleep: i => { });
            blackboard.chooseBaitFailures = chooseBaitfailures.ToList();
            blackboard.throwRodNoBaitFishFailures = throwRodNoTargetFishfailures.ToList();

            //
            GetFishpond sut = new GetFishpond("-", blackboard, new FakeLogger(), false, new FakeTimeProvider(), drawContent: new FakeDrawContent());
            BehaviourStatus actualStatus = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actualStatus);
            Assert.NotEmpty(blackboard.fishpond.Fishes);
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
        public void GetFishpondTest_FishCount_ShouldSuccess(string screenshot1080p, string fishName, int count)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(Predictor, sleep: i => { });

            //
            GetFishpond sut = new GetFishpond("-", blackboard, new FakeLogger(), false, new FakeTimeProvider(), drawContent: new FakeDrawContent());
            sut.Tick(imageRegion);
            int actual = blackboard.fishpond?.Fishes?.Count(f => f.FishType.Name == fishName) ?? 0;

            //
            Assert.Equal(count, actual);
        }
    }
}
