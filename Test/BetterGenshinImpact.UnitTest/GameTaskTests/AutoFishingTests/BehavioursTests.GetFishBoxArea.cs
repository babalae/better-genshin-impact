using BehaviourTree;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.GameTask.Model.Area;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using BetterGenshinImpact.Core.Recognition.ONNX.YOLO;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Theory]
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png")]
        [InlineData(@"20250313-0442-39.4967763.mp4_20250313_132441.664.png")]   // 网友反馈的案例，有偏色
        [InlineData(@"20250313-0442-39.4967763.mp4_20250313_132441.969.png")]
        /// <summary>
        /// 测试获取钓鱼拉扯框，结果为成功
        /// </summary>
        public void GetFishBoxArea_ShouldSuccess(string screenshot1080p)
        {
            //
            Mat bitmap = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0,  drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(null, sleep: i => { });

            //
            GetFishBoxArea sut = new GetFishBoxArea("-", blackboard, new FakeLogger(), false);
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Succeeded, actual);
        }

        [Theory]
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png")]
        [InlineData(@"202503012143011486@900p.png")]
        /// <summary>
        /// 测试获取钓鱼拉扯框，超时后，结果为失败
        /// </summary>
        public void GetFishBoxArea_ShouldFail(string screenshot1080p)
        {
            //
            Mat bitmap = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(bitmap, 0, 0, drawContent: new FakeDrawContent());

            var blackboard = new Blackboard(null, sleep: i => { });

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            //
            GetFishBoxArea sut = new GetFishBoxArea("-", blackboard, new FakeLogger(), false, fakeTimeProvider);
            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.NotEqual(BehaviourStatus.Failed, actual);

            //
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(6));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Failed, actual);
        }
    }
}
