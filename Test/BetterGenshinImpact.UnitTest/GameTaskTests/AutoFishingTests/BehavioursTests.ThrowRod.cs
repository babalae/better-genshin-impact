using BehaviourTree;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.GameTask.Model.Area;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using BetterGenshinImpact.Core.Config;
using Compunet.YoloV8;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", "false worm bait")]
        [InlineData(@"20250226162217468_ThrowRod_Succeeded.png", "fruit paste bait")]
        /// <summary>
        /// 测试各种抛竿，结果为成功
        /// </summary>
        public void ThrowRodTest_VariousFish_ShouldSuccess(string screenshot1080p, string selectedBaitName)
        {
            //
            Bitmap bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            var predictor = YoloV8Builder.CreateDefaultBuilder().UseOnnxModel(Global.Absolute(@"Assets\Model\Fish\bgi_fish.onnx")).Build();

            var blackboard = new Blackboard(predictor, sleep: i => { })
            {
                selectedBaitName = selectedBaitName
            };

            //
            ThrowRod sut = new ThrowRod("-", blackboard, new FakeLogger(), new FakeInputSimulator());
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.False(blackboard.noTargetFish);
            Assert.Equal(BehaviourStatus.Succeeded, actual);
        }

        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", "fruit paste bait")]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", "redrot bait")]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", "fake fly bait")]
        /// <summary>
        /// 测试各种抛竿，结果为运行中
        /// </summary>
        public void ThrowRodTest_VariousFish_ShouldFail(string screenshot1080p, string selectedBaitName)
        {
            //
            Bitmap bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            var predictor = YoloV8Builder.CreateDefaultBuilder().UseOnnxModel(Global.Absolute(@"Assets\Model\Fish\bgi_fish.onnx")).Build();

            var blackboard = new Blackboard(predictor, sleep: i => { })
            {
                selectedBaitName = selectedBaitName
            };

            //
            ThrowRod sut = new ThrowRod("-", blackboard, new FakeLogger(), new FakeInputSimulator());
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.False(blackboard.noTargetFish);
            Assert.Equal(BehaviourStatus.Running, actual);
        }
    }
}
