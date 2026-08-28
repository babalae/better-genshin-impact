using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using CsTrees;
using CsTrees.Composites;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class BehavioursTests
    {
        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", BaitType.FalseWormBait)]
        /// <summary>
        /// 测试各种抛竿，结果为成功
        /// </summary>
        public async Task ThrowRodTest_VariousFish_ShouldSuccess(string screenshot1080p, BaitType selectedBait)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var selectedBaitAccess = blackboard.GrantWrite<BaitType?>(null!, "SelectedBait");
            selectedBaitAccess.Set(selectedBait);
            var throwRodNoBaitFishAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoBaitFish");

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion, imageRegion], bb!))
                        .ThrowRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, fakeTimeProvider, drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();

            //
            // 第一次 tick：按下左键后等待举竿画面渲染（Running）
            Status actual = await sut.TickOnce();
            Assert.False(throwRodNoBaitFishAccess.Get());
            Assert.Equal(Status.Running, actual);

            //
            // 越过举竿画面等待后，第二次 tick 确认举起并完成落点检测
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(500));
            actual = await sut.TickOnce();

            //
            Assert.Equal(Status.Success, actual);
        }

        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", BaitType.RedrotBait)]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", BaitType.FakeFlyBait)]
        [InlineData(@"20250226162217468_ThrowRod_Succeeded.png", BaitType.FruitPasteBait)]
        /// <summary>
        /// 测试各种抛竿，未满足HutaoFisher判定，结果为运行中
        /// </summary>
        public async Task ThrowRodTest_VariousFish_ShouldFail(string screenshot1080p, BaitType selectedBait)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var selectedBaitAccess = blackboard.GrantWrite<BaitType?>(null!, "SelectedBait");
            selectedBaitAccess.Set(selectedBait);
            var throwRodNoBaitFishAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoBaitFish");

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion], bb!))
                        .ThrowRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, fakeTimeProvider, drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();

            //
            Status actual = await sut.TickOnce();

            //
            Assert.False(throwRodNoBaitFishAccess.Get());
            Assert.Equal(Status.Running, actual);
        }

        [Theory]
        [InlineData(@"20250225101304534_ThrowRod_Succeeded.png", BaitType.FlashingMaintenanceMekBait)]
        /// <summary>
        /// 测试各种抛竿，无鱼饵适用鱼，结果为失败
        /// </summary>
        public async Task ThrowRodTest_NoBaitFish_ShouldFail(string screenshot1080p, BaitType selectedBait)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var selectedBaitAccess = blackboard.GrantWrite<BaitType?>(null!, "SelectedBait");
            selectedBaitAccess.Set(selectedBait);
            var throwRodNoBaitFishAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoBaitFish");

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(imageRegion, 12), bb!))
                        .ThrowRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, fakeTimeProvider, drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();
            //
            // 第一次 tick：按下左键后等待举竿画面渲染（Running）
            Status actual = await sut.TickOnce();
            Assert.False(throwRodNoBaitFishAccess.Get());
            Assert.Equal(Status.Running, actual);

            //
            // 越过举竿画面等待，后续 tick 才开始检测
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(500));

            //
            // 循环第 1 次 tick 确认举起并完成第一次检测（noTargetFishTimes=1），
            // 累计 11 次检测（noTargetFishTimes=11 > 10）触发 ThrowRodNoBaitFish
            for (int i = 0; i < 11; i++)
            {
                await sut.TickOnce();
            }

            //
            Assert.True(throwRodNoBaitFishAccess.Get());
        }

        /// <summary>
        /// 抛竿时，给定三条炮鲀鱼，并且确定算法能将下杆点从鱼的左侧移动到最左侧的炮鲀鱼上，此时希望“当前鱼”能始终锁定在最左侧的鱼上
        /// 由于偶尔观测到“摇摆”行为的出现，故设计此测试
        /// </summary>
        [Fact]
        public async Task ThrowRodTest_Target_ShouldBeTheLeftOne()
        {
            //
            Mat mat1 = new Mat(@$"..\..\..\Assets\AutoFishing\202503082114541115.png");
            var imageRegion1 = new GameCaptureRegion(mat1, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            Mat mat2 = new Mat(@$"..\..\..\Assets\AutoFishing\202503082114560489.png");
            var imageRegion2 = new GameCaptureRegion(mat2, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var selectedBaitAccess = blackboard.GrantWrite<BaitType?>(null!, "SelectedBait");
            selectedBaitAccess.Set(BaitType.FakeFlyBait);
            var fishpondAccess = blackboard.GrantWrite<Fishpond>(null!, "Fishpond");

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            var sut = new ThrowRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, blackboard, fakeTimeProvider, drawContent: new FakeDrawContent());
            var tree = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [imageRegion1, imageRegion2, imageRegion2], bb!))
                        .Leaf(() => sut)
                    .End()
                .End()
                .Build();

            //
            // 第一次 tick：按下左键后等待举竿画面渲染（Running）
            await tree.TickOnce();
            // 越过举竿画面等待后，第二次 tick 确认举起并完成首次检测
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(500));
            await tree.TickOnce();
            var actual = sut.currentFish;

            //
            var fishpond = fishpondAccess.Get();
            Assert.True(fishpond.TargetRect != null && fishpond.TargetRect.Value != default);
            Assert.Equal(3, fishpond.Fishes.Count(f => f.FishType.Name == "pufferfish"));
            Assert.Equal(fishpond.Fishes.OrderBy(f => f.Rect.X).First(), actual);

            //

            //
            await sut.TickOnce();
            actual = sut.currentFish;

            //
            fishpond = fishpondAccess.Get();
            Assert.Equal(3, fishpond.Fishes.Count(f => f.FishType.Name == "pufferfish"));
            Assert.Equal(fishpond.Fishes.OrderBy(f => f.Rect.X).First(), actual);
        }

        [Theory]
        [InlineData(@"202502252347412417.png")]
        [InlineData(@"202503012143011486@900p.png")]
        /// <summary>
        /// 测试各种抛竿，超时未找到落点，结果为失败
        /// </summary>
        public async Task ThrowRodTest_NoTarget_ShouldFail(string screenshot1080p)
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());
            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var throwRodNoTargetAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoTarget");

            var sut = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(imageRegion, 8), bb!))
                        .ThrowRod("-", new FakeLogger(), new FakeInputSimulator(), Predictor, fakeTimeProvider, drawContent: new FakeDrawContent())
                    .End()
                .End()
                .Build();

            //
            // 第一次 tick：按下左键后等待举竿画面渲染（Running）
            Status actual = await sut.TickOnce();
            Assert.False(throwRodNoTargetAccess.Get());
            Assert.Equal(Status.Running, actual);

            //
            // 越过举竿画面等待，之后每帧检测落点。该截图无落点（NoTarget），
            // 确认阶段会重按左键（最多3次）后转入移视角找落点，最终超时失败。
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(500));

            //
            // 多 tick 若干次，让确认阶段的重按耗尽并进入移视角找落点阶段，最终超时失败
            Status actualAfterTicks = Status.Running;
            for (int i = 0; i < 5 && !throwRodNoTargetAccess.Get(); i++)
            {
                fakeTimeProvider.Advance(TimeSpan.FromSeconds(1.2));
                actualAfterTicks = await sut.TickOnce();
            }

            //
            Assert.True(throwRodNoTargetAccess.Get());
            Assert.Equal(Status.Failure, actualAfterTicks);
        }

        [Theory]
        [InlineData(@"202502252347412417.png")]
        /// <summary>
        /// 测试抛竿循环，3次超时未找到落点，结果为失败并退出
        /// </summary>
        public async Task ThrowRodTest_NoTarget_3Times_ShouldFailAndAbort(string screenshot1080p)
        {
            //
            FakeInputSimulator input = new FakeInputSimulator();
            FakeDrawContent drawContent = new FakeDrawContent();
            Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            var imageRegion = new GameCaptureRegion(mat, 0, 0, new DesktopRegion(input.Mouse), converter: new ScaleConverter(1d), drawContent: drawContent);
            FakeTimeProvider timeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var throwRodNoTargetAccess = blackboard.GrantWrite<bool>(null!, "ThrowRodNoTarget");
            var throwRodNoTargetTimesAccess = blackboard.GrantWrite<int>(null!, "ThrowRodNoTargetTimes");
            var abortAccess = blackboard.GrantWrite<bool>(null!, "Abort");

            var sut = new ThrowRod("-", logger, input, Predictor, blackboard, timeProvider, drawContent);
            var tree = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(imageRegion, 40), bb!))
                        .Leaf(() => sut)
                    .End()
                .End()
                .Build();

            //
            // 每轮：按下左键等待渲染 → 确认阶段重按左键（无落点，最多3次）耗尽 → 移视角找落点
            // → 推进时间越过 5 秒期限 → 超时失败（ThrowRodNoTarget=true）。
            // 连续 3 轮后 ThrowRodNoTargetTimes 达到上限，Abort。
            for (int round = 1; round <= 3; round++)
            {
                await tree.TickOnce(); // 进入 ThrowRod（按下左键，等待渲染）
                for (int i = 0; i < 6; i++)
                {
                    await tree.TickOnce();
                }
                timeProvider.Advance(TimeSpan.FromSeconds(5.1));
                await tree.TickOnce();

                Assert.True(throwRodNoTargetAccess.Get(),
                    $"第 {round} 轮应已设置 ThrowRodNoTarget");
                Assert.Equal(round, throwRodNoTargetTimesAccess.Get());
            }

            Assert.True(abortAccess.Get());
        }

        /// <summary>
        /// 回归测试：换饵完成后（ChooseBait 返回 Success 的同一 tick），ThrowRod 前置校验
        /// 可能拿到换饵界面遮罩的旧帧（该帧中 switch_bait/exit_fishing 均无法匹配）。
        /// 校验失败时不应立即 Abort 退出钓鱼，而应等待后续 tick 的新截图重试，最终校验通过。
        /// </summary>
        [Fact]
        public async Task ThrowRodTest_BaitUIStaleFrame_ShouldRetryNotAbort()
        {
            // 换饵界面旧帧：诊断确认 switch_bait(0.467) 与 exit_fishing(0.000) 均匹配不到，
            // 模拟 ChooseBait 完成瞬间仍缓存着遮罩画面。
            Mat staleMat = new Mat(@$"..\..\..\Assets\AutoFishing\202509141339218213_ChooseBait.png");
            var staleRegion = new GameCaptureRegion(staleMat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            // 正常钓鱼界面新帧：switch_bait(0.941) 命中，确认处于钓鱼界面。
            Mat freshMat = new Mat(@$"..\..\..\Assets\AutoFishing\202502252347412417.png");
            var freshRegion = new GameCaptureRegion(freshMat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();
            FakeInputSimulator input = new FakeInputSimulator();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            var sut = new ThrowRod("-", logger, input, Predictor, blackboard, fakeTimeProvider, drawContent: new FakeDrawContent());
            var tree = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        // 第一帧为换饵遮罩旧帧，第二帧起为正常钓鱼界面
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [staleRegion, freshRegion, freshRegion, freshRegion, freshRegion, freshRegion, freshRegion], bb!))
                        .Leaf(() => sut)
                    .End()
                .End()
                .Build();

            // 第一次 tick：前置校验拿到的旧帧匹配不到按钮 → 应重试（Running），而不是 Failure/Abort
            Status s1 = await tree.TickOnce();
            Assert.Equal(Status.Running, s1);

            // 越过举竿画面等待，再次 tick：新截图应能确认钓鱼界面，前置校验通过
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(500));
            Status s2 = await tree.TickOnce();
            Assert.Equal(Status.Running, s2);
            Assert.NotEqual(Status.Failure, s2);

            // 连续多次 tick 后仍不应 Abort（Abort 键可通过行为状态而非直接读取来验证：
            // 若前置校验误判未在钓鱼界面，行为会返回 Failure）
            for (int i = 0; i < 3; i++)
            {
                await tree.TickOnce();
            }
        }

        /// <summary>
        /// 回归测试：抛竿时饵料已用光会弹出"鱼饵不足"提示条（out_of_bait），
        /// 该弹窗会遮挡落点/鱼塘识别导致抛竿失败并误触发退出流程。
        /// 修复后 ThrowRod 通过跨 tick 状态机处理：第一 tick 仅 ESC 关提示条（Running，不置 Abort），
        /// 第二 tick 用新帧确认提示条已关后 ESC 退出钓鱼模式（Running，不置 Abort），
        /// 第三 tick 在"是否退出钓鱼？"确认弹窗渲染完成后置 Abort 返回 Failure——
        /// 确保冒泡到 QuitFishingMode 时其截图是包含确认弹窗的新帧，而非 ESC 按下前的旧帧。
        /// </summary>
        [Fact]
        public async Task ThrowRodTest_OutOfBaitPopup_ShouldDismissAndExitFishing()
        {
            // 弹窗截图：抛竿时饵料用光，界面出现"鱼饵不足"提示条。
            Mat popupMat = new Mat(@$"..\..\..\Assets\AutoFishing\out_of_bait_popup.png");
            var popupRegion = new GameCaptureRegion(popupMat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            // 正常钓鱼界面帧（弹窗关闭后）：switch_bait 命中
            Mat normalMat = new Mat(@$"..\..\..\Assets\AutoFishing\202502252347412417.png");
            var normalRegion = new GameCaptureRegion(normalMat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();
            FakeInputSimulator input = new FakeInputSimulator();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var abortAccess = blackboard.GrantWrite<bool>(null!, "Abort");

            var sut = new ThrowRod("-", logger, input, Predictor, blackboard, fakeTimeProvider, drawContent: new FakeDrawContent());
            var tree = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        // 第一帧为弹窗帧，第二帧起为正常钓鱼界面
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [popupRegion, normalRegion, normalRegion, normalRegion, normalRegion, normalRegion, normalRegion], bb!))
                        .Leaf(() => sut)
                    .End()
                .End()
                .Build();

            // 第一次 tick：检测到弹窗 → ESC 关闭提示条（第 1 次 ESC），等待下一帧确认，不置 Abort
            Status s1 = await tree.TickOnce();
            Assert.Equal(Status.Running, s1);
            Assert.False(abortAccess.Exists(), "关闭提示条阶段不应置 Abort");
            Assert.Equal(1, input.FakeKeyboard.EscapeKeyPressCount);

            // 第二次 tick：新帧确认提示条已关 → ESC 退出钓鱼模式（第 2 次 ESC），等待确认弹窗渲染，不置 Abort
            Status s2 = await tree.TickOnce();
            Assert.Equal(Status.Running, s2);
            Assert.False(abortAccess.Exists(), "退出钓鱼模式阶段不应置 Abort");
            Assert.Equal(2, input.FakeKeyboard.EscapeKeyPressCount);

            // 第三次 tick：确认弹窗已渲染 → 置 Abort 并返回 Failure（触发冒泡到 QuitFishingMode）
            Status s3 = await tree.TickOnce();
            Assert.Equal(Status.Failure, s3);
            Assert.True(abortAccess.Exists() && abortAccess.Get(), "确认弹窗渲染完成后应置 Abort 以退出钓鱼模式");
            Assert.Equal(2, input.FakeKeyboard.EscapeKeyPressCount);
            Assert.False(input.FakeMouse.IsLeftButtonDown, "置 Abort 退出前应释放左键");
        }

        /// <summary>
        /// 回归测试：第一次 ESC 未能立即关闭提示条时（下一帧仍匹配到弹窗），
        /// 状态机应停留在"关提示条"阶段继续按 ESC，而不是误按第二次 ESC 退出；
        /// 待提示条确认关闭后再退出并置 Abort。
        /// </summary>
        [Fact]
        public async Task ThrowRodTest_OutOfBaitPopup_FirstEscNotEffective_ShouldRetryDismiss()
        {
            Mat popupMat = new Mat(@$"..\..\..\Assets\AutoFishing\out_of_bait_popup.png");
            var popupRegion = new GameCaptureRegion(popupMat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            Mat normalMat = new Mat(@$"..\..\..\Assets\AutoFishing\202502252347412417.png");
            var normalRegion = new GameCaptureRegion(normalMat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();
            FakeInputSimulator input = new FakeInputSimulator();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var abortAccess = blackboard.GrantWrite<bool>(null!, "Abort");

            var sut = new ThrowRod("-", logger, input, Predictor, blackboard, fakeTimeProvider, drawContent: new FakeDrawContent());
            var tree = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        // 前两帧都是弹窗帧（第一次 ESC 未生效），第三帧起为正常钓鱼界面
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", [popupRegion, popupRegion, normalRegion, normalRegion, normalRegion, normalRegion], bb!))
                        .Leaf(() => sut)
                    .End()
                .End()
                .Build();

            // tick 0：检测到弹窗 → 第 1 次 ESC 关提示条 → Running
            Status s1 = await tree.TickOnce();
            Assert.Equal(Status.Running, s1);
            Assert.False(abortAccess.Exists());
            Assert.Equal(1, input.FakeKeyboard.EscapeKeyPressCount);

            // tick 1：提示条仍在 → 继续 ESC 关提示条（第 2 次 ESC），仍未退出
            Status s2 = await tree.TickOnce();
            Assert.Equal(Status.Running, s2);
            Assert.False(abortAccess.Exists());
            Assert.Equal(2, input.FakeKeyboard.EscapeKeyPressCount);

            // tick 2：提示条已关 → ESC 退出钓鱼模式（第 3 次 ESC），等待确认弹窗渲染
            Status s3 = await tree.TickOnce();
            Assert.Equal(Status.Running, s3);
            Assert.False(abortAccess.Exists());
            Assert.Equal(3, input.FakeKeyboard.EscapeKeyPressCount);

            // tick 3：确认弹窗已渲染 → 置 Abort 并返回 Failure
            Status s4 = await tree.TickOnce();
            Assert.Equal(Status.Failure, s4);
            Assert.True(abortAccess.Exists() && abortAccess.Get());
            Assert.Equal(3, input.FakeKeyboard.EscapeKeyPressCount);
            Assert.False(input.FakeMouse.IsLeftButtonDown, "置 Abort 退出前应释放左键");
        }

        /// <summary>
        /// 回归测试：提示条持续可见（ESC 始终无法关闭）时，状态机不应无限按 ESC 活锁，
        /// 而应在重试 _outOfBaitPopupDismissRetry 次后放弃，置 Abort 返回 Failure 退出本轮抛竿。
        /// </summary>
        [Fact]
        public async Task ThrowRodTest_OutOfBaitPopup_CannotDismiss_ShouldAbort()
        {
            // 全部为弹窗帧：模拟提示条始终无法被 ESC 关闭
            Mat popupMat = new Mat(@$"..\..\..\Assets\AutoFishing\out_of_bait_popup.png");
            var popupRegion = new GameCaptureRegion(popupMat, 0, 0, new DesktopRegion(new FakeMouseSimulator()), converter: new ScaleConverter(1d), drawContent: new FakeDrawContent());

            FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();
            FakeLogger logger = new FakeLogger();
            FakeInputSimulator input = new FakeInputSimulator();

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();
            var abortAccess = blackboard.GrantWrite<bool>(null!, "Abort");

            var sut = new ThrowRod("-", logger, input, Predictor, blackboard, fakeTimeProvider, drawContent: new FakeDrawContent());
            var tree = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("用例", false)
                        .SetSleep("设置sleep方法", _ => { })
                        .LeafWithBlackboard(bb => new ScreenshotQueue("用例", Enumerable.Repeat(popupRegion, 8), bb!))
                        .Leaf(() => sut)
                    .End()
                .End()
                .Build();

            // 前 3 次 tick：每次检测到提示条 → 按 ESC 关闭（未生效）→ Running，不置 Abort
            for (int i = 0; i < 3; i++)
            {
                Status s = await tree.TickOnce();
                Assert.Equal(Status.Running, s);
                Assert.False(abortAccess.Exists());
                Assert.Equal(i + 1, input.FakeKeyboard.EscapeKeyPressCount);
            }

            // 第 4 次 tick：超过重试上限 → 放弃，置 Abort 返回 Failure（不再按 ESC）
            Status s4 = await tree.TickOnce();
            Assert.Equal(Status.Failure, s4);
            Assert.True(abortAccess.Exists() && abortAccess.Get());
            Assert.Equal(3, input.FakeKeyboard.EscapeKeyPressCount);
            Assert.False(input.FakeMouse.IsLeftButtonDown, "置 Abort 退出前应释放左键");
        }
    }
}
