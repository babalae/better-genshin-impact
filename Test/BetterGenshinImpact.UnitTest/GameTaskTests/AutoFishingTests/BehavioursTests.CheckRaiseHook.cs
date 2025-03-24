using BehaviourTree;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BehaviourTree.Composites;
using BehaviourTree.FluentBuilder;
using Microsoft.Extensions.Time.Testing;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Fact]
        /// <summary>
        /// 即使抛竿的瞬间、开始检测咬杆时遇到了假阳性，CheckRaiseHook也能驳回这种情况，返回失败
        /// </summary>
        public void CheckRaiseHook_BreakThrough_When_ThrowRod_ShouldFail()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\20250314164703100_FishBite_Succeeded_FP.png");

            FakeInputSimulator input = new FakeInputSimulator();
            FakeDrawContent drawContent = new FakeDrawContent();
            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);
            AutoFishingAssets autoFishingAssets = new AutoFishingAssets(systemInfo);

            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(input.Mouse), converter: new ScaleConverter(1d), drawContent);

            var blackboard = new Blackboard(sleep: i => { }, autoFishingAssets: autoFishingAssets)
            {
            };

            FakeTimeProvider timeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();

            //

            var sut = FluentBuilder.Create<ImageRegion>()
                .MySimpleParallel("下杆中", SimpleParallelPolicy.OnlyOneMustSucceed)
                    .PushLeaf(() => new CheckThrowRod("检查抛竿结果", blackboard, logger, false, timeProvider))    // todo 后面串联一个召回率高的下杆中检测方法
                    .PushLeaf(() => new FishBite("自动提竿", blackboard, logger, false, input, drawContent))
                    .PushLeaf(() => new FishBiteTimeout("下杆超时检查", 15, logger, false, input, timeProvider))
                .End()
                .Build();

            BehaviourStatus actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Succeeded, actual);    // 此时下杆中状态瞬间完成，进入拉条

            //
            sut = FluentBuilder.Create<ImageRegion>()
                .MySimpleParallel("拉条中", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                    .PushLeaf(() => new CheckRaiseHook("检查提竿结果", blackboard, logger, false, timeProvider))
                    .Sequence("拉条序列")
                        .PushLeaf(() => new GetFishBoxArea("等待拉条出现", blackboard, logger, false, timeProvider))
                        .PushLeaf(() => new Fishing("钓鱼拉条", blackboard, logger, false, input, timeProvider, drawContent))
                    .End()
                .End()
                .Build();

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            timeProvider.Advance(TimeSpan.FromSeconds(1));  // 1秒后浮漂落入水面
            mat = new Mat(@$"..\..\..\Assets\AutoFishing\20250306111749714_CheckThrowRod_Succeeded.png");   // 一张正常下杆的图片
            imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(input.Mouse), converter: new ScaleConverter(1d), drawContent);

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Running, actual);

            //
            timeProvider.Advance(TimeSpan.FromSeconds(2));

            //
            actual = sut.Tick(imageRegion);

            //
            Assert.Equal(BehaviourStatus.Failed, actual);
        }
    }
}
