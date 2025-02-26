using BehaviourTree;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using Compunet.YoloV8;
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
        /// 测试各种选取鱼饵，结果为运行中
        /// todo DateTime改TimeProvider
        /// </summary>
        public void ChooseBaitTest_VariousBait_ShouldBeRunning(string screenshot1080p, IEnumerable<string> fishNames)
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
            Assert.Equal(BehaviourStatus.Running, actual);
        }
    }
}
