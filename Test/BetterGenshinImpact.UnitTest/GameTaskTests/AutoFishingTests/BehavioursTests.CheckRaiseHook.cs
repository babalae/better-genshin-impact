using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using CsTrees;
using CsTrees.Composites;
using CsTrees.FluentBuilder;
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
        /// 即使抛竿的瞬间、开始检测咬杆时遇到了假阳性，CheckRaiseHook也能驳回这种情况，返回失败
        /// </summary>
        public async Task CheckRaiseHook_BreakThrough_When_ThrowRod_ShouldFail()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\20250314164703100_FishBite_Succeeded_FP.png");

            FakeInputSimulator input = new FakeInputSimulator();
            FakeDrawContent drawContent = new FakeDrawContent();
            var imageRegion1 = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(input.Mouse), converter: new ScaleConverter(1d), drawContent);

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            FakeTimeProvider timeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();

            //

            var sut = TreeBuilder.Create()
                .WithBlackboard(blackboard)
                    .Sequence("用例")
                        .ScreenshotQueue("用例", [imageRegion1])
                        .Parallel("下杆中", new ParallelPolicy.SuccessOnOne())
                            .CheckThrowRod("检查抛竿结果", logger, timeProvider)    // todo 后面串联一个召回率高的下杆中检测方法
                            .FishBite("自动提竿", logger, input, OcrService, drawContent)
                            .FishBiteTimeout("下杆超时检查", 15, logger, input, timeProvider)
                        .End()
                    .End()
                .End()
                .Build();

            await sut.TickOnce();
            Status actual = sut.Status;

            //
            Assert.Equal(Status.Success, actual);    // 此时下杆中状态瞬间完成，进入拉条

            //
            mat = new Mat(@$"..\..\..\Assets\AutoFishing\20250306111749714_CheckThrowRod_Succeeded.png");   // 一张正常下杆的图片
            var imageRegion2 = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(input.Mouse), converter: new ScaleConverter(1d), drawContent);

            blackboard = new CsTrees.Blackboard.Blackboard();

            sut = TreeBuilder.Create()
                .WithBlackboard(blackboard)
                    .Sequence("用例")
                        .ScreenshotQueue("用例", [imageRegion1, imageRegion2, imageRegion2])
                        .Parallel("拉条中", new ParallelPolicy.SuccessOnOne())
                            .CheckRaiseHook("检查提竿结果", logger, timeProvider)
                            .SequenceWithMemory("拉条序列")
                                .GetFishBoxArea("等待拉条出现", logger, false, timeProvider)
                                .Fishing("钓鱼拉条", logger, false, input, timeProvider, drawContent)
                            .End()
                        .End()
                    .End()
                .End()
                .Build();

            //
            await sut.TickOnce();
            actual = sut.Status;

            //
            Assert.Equal(Status.Running, actual);

            //
            timeProvider.Advance(TimeSpan.FromSeconds(1));  // 1秒后浮漂落入水面

            //
            await sut.TickOnce();
            actual = sut.Status;

            //
            Assert.Equal(Status.Running, actual);

            //
            timeProvider.Advance(TimeSpan.FromSeconds(2));

            //
            await sut.TickOnce();
            actual = sut.Status;

            //
            Assert.Equal(Status.Failure, actual);
        }
    }
}
