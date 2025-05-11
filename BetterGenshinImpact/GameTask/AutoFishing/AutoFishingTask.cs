using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BehaviourTree.Composites;
using BehaviourTree.FluentBuilder;
using BehaviourTree;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using Vanara.PInvoke;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using Fischless.WindowsInput;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Core.Recognition.ONNX;
using static Vanara.PInvoke.User32;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using System.Globalization;
using Microsoft.Extensions.Localization;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Core.Recognition.OCR;
using Compunet.YoloSharp;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTask : ISoloTask
    {
        private readonly ILogger _logger = App.GetLogger<AutoFishingTask>();
        private readonly InputSimulator input = Simulation.SendInput;
        public string Name => "钓鱼独立任务";

        private CancellationToken _ct;

        private readonly AutoFishingTaskParam param;

        private readonly BgiYoloPredictor _predictor =
            BgiOnnxFactory.Instance.CreateYoloPredictor(BgiOnnxModel.BgiFish);

        public AutoFishingTask(AutoFishingTaskParam param)
        {
            this.param = param;
        }

        public Task Start(CancellationToken ct)
        {
            this._ct = ct;

            IOcrService ocrService = OcrFactory.Paddle;

            IStringLocalizer<AutoFishingImageRecognition> stringLocalizer =
                App.GetService<IStringLocalizer<AutoFishingImageRecognition>>() ??
                throw new NullReferenceException(nameof(stringLocalizer));

            Blackboard blackboard = new Blackboard(_predictor, this.Sleep, AutoFishingAssets.Instance);

            // @formatter:off
            var behaviourTree = FluentBuilder.Create<ImageRegion>()
                .Sequence("钓鱼并确保完成后退出钓鱼模式")
                    .MySimpleParallel("在整体超时时间内钓鱼", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                        .Sequence("调整视角并钓鱼")
                            .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                            .MySimpleParallel("找鱼20秒", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                .PushLeaf(() => new TurnAround("转圈圈调整视角", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                .PushLeaf(() => new FindFishTimeout("找到鱼", 20, blackboard, _logger, param.SaveScreenshotOnKeyTick))
                            .End()
                            .PushLeaf(() => new EnterFishingMode("进入钓鱼模式", blackboard, _logger, param.SaveScreenshotOnKeyTick, input, cultureInfo: param.GameCultureInfo))
                            .UntilFailed(@"\")
                                .Sequence("一直钓鱼直到没鱼")
                                    .AlwaysSucceed(@"\")
                                        .Sequence("从找鱼开始")
                                            .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                            .MySimpleParallel("找鱼10秒", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                                .UntilSuccess("找鱼 + 初始状态确认")
                                                    .Sequence("-")
                                                        .PushLeaf(() => new CheckInitalState("初始状态确认", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                                        .PushLeaf(() => new GetFishpond("检测鱼群", blackboard, _logger, param.SaveScreenshotOnKeyTick))
                                                    .End()
                                                .End()
                                                .PushLeaf(() => new FindFishTimeout("确认初始状态和找到鱼", 10, blackboard, _logger, param.SaveScreenshotOnKeyTick))
                                            .End()
                                            .PushLeaf(() => new ChooseBait("选择鱼饵", blackboard, _logger, param.SaveScreenshotOnKeyTick, TaskContext.Instance().SystemInfo, input))
                                            .MySimpleParallel("抛竿直到成功或出错", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                                .UntilSuccess("重复抛竿")
                                                    .Sequence("-")
                                                        .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                                            //.MySimpleParallel("举起鱼竿并抛竿", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                                            //    .PushLeaf(() => new LiftAndHold("举起鱼竿", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                                            .PushLeaf(() => new ThrowRod("抛竿", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                                    //.End()
                                                    .End()
                                                .End()
                                                .Do("抛竿检查", _ => (blackboard.abort || blackboard.throwRodNoTarget || blackboard.throwRodNoBaitFish) ? BehaviourStatus.Failed : BehaviourStatus.Running)
                                            .End()
                                            .MySimpleParallel("下杆中", SimpleParallelPolicy.OnlyOneMustSucceed)
                                                .PushLeaf(() => new CheckThrowRod("检查抛竿结果", blackboard, _logger, param.SaveScreenshotOnKeyTick))    // todo 后面串联一个召回率高的下杆中检测方法
                                                .PushLeaf(() => new FishBite("自动提竿", blackboard, _logger, param.SaveScreenshotOnKeyTick, input, ocrService, cultureInfo: param.GameCultureInfo, stringLocalizer: stringLocalizer))
                                                .PushLeaf(() => new FishBiteTimeout("下杆超时检查", param.ThrowRodTimeOutTimeoutSeconds, _logger, param.SaveScreenshotOnKeyTick, input))
                                            .End()
                                            .MySimpleParallel("拉条中", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                                .PushLeaf(() => new CheckRaiseHook("检查提竿结果", blackboard, _logger, param.SaveScreenshotOnKeyTick))
                                                .Sequence("拉条序列")
                                                    .PushLeaf(() => new GetFishBoxArea("等待拉条出现", blackboard, _logger, param.SaveScreenshotOnKeyTick))
                                                    .PushLeaf(() => new Fishing("钓鱼拉条", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                                .End()
                                            .End()
                                        .End()
                                    .End()
                                    .Do("冒泡-终止检查", _ => blackboard.abort ? BehaviourStatus.Failed : BehaviourStatus.Succeeded)
                                .End()
                            .End()
                        .End()
                        .PushLeaf(() => new WholeProcessTimeout("检查整体超时", param.WholeProcessTimeoutSeconds, _logger, param.SaveScreenshotOnKeyTick))
                    .End()
                    .PushLeaf(() => new QuitFishingMode("退出钓鱼模式", blackboard, _logger, param.SaveScreenshotOnKeyTick, input, param.GameCultureInfo))
                .End()
                .Build();
            // @formatter:on
            _logger.LogInformation("→ {Text}", "自动钓鱼，启动！");
            _logger.LogWarning("请不要携带任何{Msg}，极有可能会误识别导致无法结束自动钓鱼！", "跟宠");
            _logger.LogInformation(
                $"当前参数：{param.WholeProcessTimeoutSeconds}，{param.ThrowRodTimeOutTimeoutSeconds}，{param.FishingTimePolicy}, {param.SaveScreenshotOnKeyTick}, {param.GameCultureInfo}");
            TaskContext.Instance().Config.AutoFishingConfig.Enabled = false;
            _logger.LogInformation("全自动运行时，自动切换实时任务中的半自动钓鱼功能为关闭状态");

            void tickARound()
            {
                blackboard.Reset();

                var prevManualGc = DateTime.MinValue;
                while (!ct.IsCancellationRequested)
                {
                    if (!SystemControl.IsGenshinImpactActiveByProcess())
                    {
                        _logger.LogInformation("当前获取焦点的窗口不是原神，停止执行");
                        break;
                    }

                    using var bitmap =
                        TaskControl.CaptureGameImageNoRetry(TaskTriggerDispatcher.Instance().GameCapture);
                    if (bitmap == null)
                    {
                        _logger.LogWarning("截图失败");
                        continue;
                    }

                    using var content = new CaptureContent(bitmap, 0, 0);
                    behaviourTree.Tick(content.CaptureRectArea);

                    if (behaviourTree.Status != BehaviourStatus.Running)
                    {
                        _logger.LogInformation("钓鱼结束");

                        break;
                    }

                    if ((DateTime.Now - prevManualGc).TotalSeconds > 2)
                    {
                        GC.Collect();
                        prevManualGc = DateTime.Now;
                    }
                }
            }

            using var ra = TaskControl.CaptureToRectArea(forceNew: true);
            if (ra.FindMulti(AutoFightAssets.Instance.PRa).Count != 0)
            {
                _logger.LogInformation("当前处于联机状态，不使用昼夜设置");
                tickARound();
            }
            else if (param.FishingTimePolicy == FishingTimePolicy.DontChange)
            {
                tickARound();
            }
            else
            {
                SetTimeTask setTimeTask = new SetTimeTask();
                foreach (int hour in param.FishingTimePolicy == FishingTimePolicy.Daytime
                             ? [7]
                             : (param.FishingTimePolicy == FishingTimePolicy.Nighttime ? [19] : new int[] { 7, 19 }))
                {
                    setTimeTask.Start(hour, 0, ct).Wait(ct);
                    tickARound();
                }
            }

            _logger.LogInformation("→ 钓鱼任务结束");

            return Task.CompletedTask; // todo 这个行为树库不支持异步编程。。。
        }

        public void Sleep(int millisecondsTimeout)
        {
            TaskControl.Sleep(millisecondsTimeout, _ct);
        }

        public class WholeProcessTimeout : BaseBehaviour<ImageRegion>
        {
            private DateTime? timeout;
            private readonly int seconds;

            /// <summary>
            /// 如果未超时返回运行中，超时返回成功
            /// </summary>
            /// <param name="name"></param>
            /// <param name="seconds"></param>
            public WholeProcessTimeout(string name, int seconds, ILogger logger, bool saveScreenshotOnTerminate) : base(
                name, logger, saveScreenshotOnTerminate)
            {
                this.seconds = seconds;
            }

            protected override void OnInitialize()
            {
                logger.LogInformation($"钓鱼任务将在{seconds}秒后超时");
                timeout = DateTime.Now.AddSeconds(seconds);
            }

            protected override BehaviourStatus Update(ImageRegion _)
            {
                if (DateTime.Now >= timeout)
                {
                    logger.LogInformation($"{seconds}秒超时已到，强制结束任务");
                    return BehaviourStatus.Succeeded;
                }
                else
                {
                    return BehaviourStatus.Running;
                }
            }
        }

        public class FindFishTimeout : BaseBehaviour<ImageRegion>
        {
            private readonly Blackboard blackboard;
            private DateTime? timeout;
            private readonly int seconds;

            /// <summary>
            /// 如果未超时返回运行中，超时返回失败
            /// </summary>
            /// <param name="name">行为名将反映在提示语中</param>
            /// <param name="seconds"></param>
            public FindFishTimeout(string name, int seconds, Blackboard blackboard, ILogger logger,
                bool saveScreenshotOnTerminate) : base(name, logger, saveScreenshotOnTerminate)
            {
                this.blackboard = blackboard;
                this.seconds = seconds;
            }

            protected override void OnInitialize()
            {
                timeout = DateTime.Now.AddSeconds(seconds);
            }

            protected override BehaviourStatus Update(ImageRegion _)
            {
                if (DateTime.Now >= timeout)
                {
                    blackboard.abort = true;
                    logger.LogInformation($"{seconds}秒没有{Name}，退出钓鱼界面");
                    return BehaviourStatus.Failed;
                }
                else
                {
                    return BehaviourStatus.Running;
                }
            }
        }

        public class TurnAround : BaseBehaviour<ImageRegion>
        {
            private readonly IInputSimulator input;
            private readonly Blackboard blackboard;

            public TurnAround(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminate,
                IInputSimulator input) : base(name, logger, saveScreenshotOnTerminate)
            {
                this.blackboard = blackboard;
                this.input = input;
            }

            protected override BehaviourStatus Update(ImageRegion imageRegion)
            {
                var result = blackboard.Predictor.Predictor.Detect(imageRegion.CacheImage);
                if (result.Any())
                {
                    Fishpond fishpond = new Fishpond(result);
                    logger.LogInformation("定位到鱼塘：" + string.Join('、',
                        fishpond.Fishes.GroupBy(f => f.FishType).Select(g => $"{g.Key.ChineseName}{g.Count()}条")));
                    int i = 0;
                    foreach (var fish in fishpond.Fishes)
                    {
                        imageRegion.Derive(fish.Rect).DrawSelf($"{fish.FishType.ChineseName}.{i++}");
                    }

                    blackboard.Sleep(1000);
                    VisionContext.Instance().DrawContent.ClearAll();

                    var oneFourthX = imageRegion.CacheImage.Width / 4;
                    var threeFourthX = imageRegion.CacheImage.Width * 3 / 4;
                    var centerY = imageRegion.CacheImage.Height / 2;
                    if (fishpond.FishpondRect.Left > threeFourthX)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy(100, 0);
                        blackboard.Sleep(100);
                        return BehaviourStatus.Running;
                    }
                    else if (fishpond.FishpondRect.Right < oneFourthX)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy(-100, 0);
                        blackboard.Sleep(100);
                        return BehaviourStatus.Running;
                    }

                    #region 1、使人物朝向和镜头方向一致；2、打断角色待机动作，避免钓鱼F交互键被吞

                    // 加入昼夜切换后，使用KeyPress按S键被莫名吞掉了
                    // 并且发现如果原地空格跳跃后紧跟按一下S键，角色会向侧后方走去
                    // 于是使用“按一段时间”来代替KeyPress的“按一瞬间”，以求稳定的表现
                    Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_S);
                    blackboard.Sleep(100);
                    Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_S);
                    blackboard.Sleep(400);
                    Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
                    blackboard.Sleep(100);
                    Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
                    blackboard.Sleep(400);
                    blackboard.Sleep(300);

                    #endregion

                    logger.LogInformation("视角调整完毕");
                    return BehaviourStatus.Succeeded;
                }

                input.Mouse.MoveMouseBy(100, 0);
                blackboard.Sleep(100);

                return BehaviourStatus.Running;
            }
        }

        private class EnterFishingMode : BaseBehaviour<ImageRegion>
        {
            private readonly IInputSimulator input;
            private readonly Blackboard blackboard;
            private readonly TimeProvider timeProvider;
            private DateTimeOffset? pressFWaitEndTime;
            private DateTimeOffset? clickWhiteConfirmButtonWaitEndTime;
            private DateTimeOffset? overallWaitEndTime;
            private readonly string fishingLocalizedString;

            public EnterFishingMode(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminate,
                IInputSimulator input, TimeProvider? timeProvider = null, CultureInfo? cultureInfo = null) : base(name,
                logger, saveScreenshotOnTerminate)
            {
                this.blackboard = blackboard;
                this.input = input;
                this.timeProvider = timeProvider ?? TimeProvider.System;
                IStringLocalizer<AutoFishingImageRecognition> stringLocalizer =
                    App.GetService<IStringLocalizer<AutoFishingImageRecognition>>() ??
                    throw new NullReferenceException(nameof(stringLocalizer));
                this.fishingLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "钓鱼");
            }

            protected override BehaviourStatus Update(ImageRegion imageRegion)
            {
                if (Status == BehaviourStatus.Ready)
                {
                    overallWaitEndTime = timeProvider.GetLocalNow().AddSeconds(10);
                    return BehaviourStatus.Running;
                }

                if ((pressFWaitEndTime == null || pressFWaitEndTime < timeProvider.GetLocalNow()) &&
                    Bv.FindFAndPress(imageRegion, input.Keyboard, this.fishingLocalizedString))
                {
                    logger.LogInformation("按下钓鱼键");
                    pressFWaitEndTime = timeProvider.GetLocalNow().AddSeconds(3);
                    return BehaviourStatus.Running;
                }
                else if ((clickWhiteConfirmButtonWaitEndTime == null ||
                          clickWhiteConfirmButtonWaitEndTime < timeProvider.GetLocalNow()) &&
                         Bv.ClickWhiteConfirmButton(imageRegion))
                {
                    logger.LogInformation("点击开始钓鱼");

                    this.blackboard.pitchReset = true;

                    clickWhiteConfirmButtonWaitEndTime = timeProvider.GetLocalNow().AddSeconds(3);

                    return BehaviourStatus.Running;
                }

                if (imageRegion.Find(blackboard.AutoFishingAssets.ExitFishingButtonRo).IsEmpty())
                {
                    if (overallWaitEndTime < timeProvider.GetLocalNow())
                    {
                        logger.LogInformation("进入钓鱼模式失败");
                        return BehaviourStatus.Failed;
                    }
                    else
                    {
                        return BehaviourStatus.Running;
                    }
                }
                else
                {
                    logger.LogInformation("进入钓鱼模式");
                    return BehaviourStatus.Succeeded;
                }
            }
        }

        private class QuitFishingMode : BaseBehaviour<ImageRegion>
        {
            private readonly IInputSimulator input;
            private readonly Blackboard blackboard;
            private readonly string fishingLocalizedString;

            public QuitFishingMode(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminate,
                IInputSimulator input, CultureInfo? cultureInfo = null) : base(name, logger, saveScreenshotOnTerminate)
            {
                this.blackboard = blackboard;
                this.input = input;
                IStringLocalizer<AutoFishingImageRecognition> stringLocalizer =
                    App.GetService<IStringLocalizer<AutoFishingImageRecognition>>() ??
                    throw new NullReferenceException(nameof(stringLocalizer));
                this.fishingLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "钓鱼");
            }

            protected override BehaviourStatus Update(ImageRegion imageRegion)
            {
                if (Status == BehaviourStatus.Ready)
                {
                    return BehaviourStatus.Running;
                }

                if (Bv.FindF(imageRegion, this.fishingLocalizedString))
                {
                    logger.LogInformation("退出完成");
                    return BehaviourStatus.Succeeded;
                }
                else if (Bv.ClickBlackConfirmButton(imageRegion))
                {
                    logger.LogInformation("在“是否退出钓鱼？”界面点击确认");
                    blackboard.Sleep(1000);
                }
                else
                {
                    input.Keyboard.KeyPress(VK.VK_ESCAPE);
                    blackboard.Sleep(2000);
                }

                return BehaviourStatus.Running;
            }
        }
    }
}
