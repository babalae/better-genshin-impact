using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using CsTrees;
using CsTrees.Composites;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        /// <summary>
        /// CheckThrowRod 双重校验成功路径：
        /// 换饵按钮消失且"等待咬钩"按钮出现 → 判定抛竿成功（hasChecked=true，保持下杆中 Running）
        /// </summary>
        [Fact]
        public async Task CheckThrowRod_BothConditionsMet_ShouldKeepRunning()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\20250306111749714_CheckThrowRod_Succeeded.png");   // 一张正常下杆的图片
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(imageRegion, 4), bb!))
                        .CheckThrowRod("检查抛竿结果", new FakeLogger(), fakeTimeProvider)
                    .End()
                .End()
                .Build();

            //
            // 第一次 tick：3 秒基础延迟内 → Running
            Status actual = await sut.TickOnce();
            Assert.Equal(Status.Running, actual);

            //
            // 越过基础延迟后判定：双重校验满足 → hasChecked=true，保持下杆中（Running）
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(4));
            actual = await sut.TickOnce();
            Assert.Equal(Status.Running, actual);

            //
            // 后续 tick 不再重复判定，保持 Running
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(2));
            actual = await sut.TickOnce();
            Assert.Equal(Status.Running, actual);
        }

        /// <summary>
        /// CheckThrowRod 双重校验的等待窗口：
        /// 换饵按钮已消失但"等待咬钩"按钮尚未渲染出来时，不立即判定抛竿失败，
        /// 而是在判定截止时间（3s 基础延迟 + 2s 渲染窗口）前持续用新截图重试；
        /// 超过截止时间仍未满足才返回 Failure。修复"几乎每次下杆都重复一次"的问题。
        /// </summary>
        [Fact]
        public async Task CheckThrowRod_WaitBiteButtonNotRenderedYet_ShouldRetryUntilDeadline()
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\202503230049406101_en.png");   // 移除了右下角按钮的帧：bait/wait_bite 均漏配，判定不满足
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(imageRegion, 8), bb!))
                        .CheckThrowRod("检查抛竿结果", new FakeLogger(), fakeTimeProvider)
                    .End()
                .End()
                .Build();

            //
            // 第一次 tick：3 秒基础延迟内 → Running
            Status actual = await sut.TickOnce();
            Assert.Equal(Status.Running, actual);

            //
            // 越过基础延迟（3s）但未到判定截止时间（3+2=5s）：判定不满足 → 等待渲染窗口继续重试（Running），而非立即 Failure
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(4));
            actual = await sut.TickOnce();
            Assert.Equal(Status.Running, actual);

            //
            // 越过判定截止时间（5s）仍未满足 → 抛竿失败
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(2));
            actual = await sut.TickOnce();
            Assert.Equal(Status.Failure, actual);
        }

        /// <summary>
        /// CheckThrowRod 等待窗口的过渡路径：
        /// 第一次判定时"等待咬钩"按钮尚未渲染（判定不满足），处于等待窗口内继续重试；
        /// 随后新帧渲染出"等待咬钩"按钮 → 应判定成功（hasChecked=true）并保持下杆中 Running。
        /// 防止实现被误改成"窗口内永不重新判定成功"。
        /// </summary>
        [Fact]
        public async Task CheckThrowRod_RecoverToSuccessWithinWindow_ShouldKeepRunning()
        {
            //
            Mat failMat = new Mat(@$"..\..\..\Assets\AutoFishing\202503230049406101_en.png");   // 右下角按钮被移除：bait/wait_bite 均漏配
            var failRegion = new GameCaptureRegion(failMat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            Mat okMat = new Mat(@$"..\..\..\Assets\AutoFishing\20250306111749714_CheckThrowRod_Succeeded.png");   // 正常下杆帧
            var okRegion = new GameCaptureRegion(okMat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [failRegion, okRegion, okRegion, okRegion], bb!))
                        .CheckThrowRod("检查抛竿结果", new FakeLogger(), fakeTimeProvider)
                    .End()
                .End()
                .Build();

            //
            // 第一次 tick：3 秒基础延迟内 → Running
            Status actual = await sut.TickOnce();
            Assert.Equal(Status.Running, actual);

            //
            // 越过基础延迟（3s）后第一次判定：wait_bite 未渲染 → 不满足，但未到截止时间（5s）→ 等待窗口继续 Running
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(4));
            actual = await sut.TickOnce();
            Assert.Equal(Status.Running, actual);

            //
            // 下一帧渲染出 wait_bite → 判定成功（hasChecked=true），保持下杆中 Running
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            actual = await sut.TickOnce();
            Assert.Equal(Status.Running, actual);

            //
            // 后续 tick 保持 Running（已判定成功，不再重复判定）
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            actual = await sut.TickOnce();
            Assert.Equal(Status.Running, actual);
        }
    }
}
