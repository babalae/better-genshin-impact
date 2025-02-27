using BetterGenshinImpact.GameTask.AutoFishing;
using BehaviourTree;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Core.Config;
using Compunet.YoloV8;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Theory]
        [InlineData("20250225101257889_GetFishpond_Succeeded.png")]
        [InlineData("202502252347412417.png")]
        [InlineData("202502252350206390.png")]
        /// <summary>
        /// 测试各种鱼塘的获取，结果为成功
        /// </summary>
        public void GetFishpondTest_VariousFishpond_ShouldSuccess(string screenshot1080p)
        {
            //
            Bitmap bitmap = new Bitmap(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new ImageRegion(bitmap, 0, 0);

            var predictor = YoloV8Builder.CreateDefaultBuilder().UseOnnxModel(Global.Absolute(@"Assets\Model\Fish\bgi_fish.onnx")).Build();

            var blackboard = new Blackboard(predictor, sleep: i => { });

            //
            GetFishpond sut = new GetFishpond("-", blackboard, new FakeLogger());
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Succeeded, actual);
        }
    }
}
