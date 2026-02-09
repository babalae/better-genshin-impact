using BehaviourTree;
using BehaviourTree.Composites;
using BehaviourTree.FluentBuilder;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Drawable;
using Compunet.YoloSharp;
using Fischless.WindowsInput;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTask : ISoloTask
    {
        private readonly ILogger _logger = App.GetLogger<AutoFishingTask>();
        private readonly InputSimulator input = Simulation.SendInput;
        public string Name => Lang.S["GameTask_10732_14ced4"];

        private CancellationToken _ct;

        private readonly AutoFishingTaskParam param;

        private readonly BgiYoloPredictor _predictor =
            App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiFish);

        public AutoFishingTask(AutoFishingTaskParam param)
        {
            this.param = param;
        }

        public Task Start(CancellationToken ct)
        {
            this._ct = ct;

            IOcrService ocrService = OcrFactory.Paddle;
            using InferenceSession session = GridIconsAccuracyTestTask.LoadModel(out Dictionary<string, float[]> prototypes);

            Blackboard blackboard = new Blackboard(_predictor, this.Sleep, AutoFishingAssets.Instance);

            // @formatter:off
            var behaviourTree = FluentBuilder.Create<ImageRegion>()
                .Sequence(Lang.S["GameTask_10731_f46489"])
                    .MySimpleParallel(Lang.S["GameTask_10730_2dadf5"], policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                        .Sequence(Lang.S["GameTask_10729_26206f"])
                            .PushLeaf(() => new MoveViewpointDown(Lang.S["GameTask_10715_834b53"], blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                            .MySimpleParallel(Lang.S["GameTask_10728_6c2c6b"], policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                .PushLeaf(() => new TurnAround(Lang.S["GameTask_10727_68ba47"], blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                .PushLeaf(() => new FindFishTimeout(Lang.S["GameTask_10726_c57b76"], 20, blackboard, _logger, param.SaveScreenshotOnKeyTick))
                            .End()
                            .PushLeaf(() => new EnterFishingMode(Lang.S["GameTask_10680_c9e1da"], blackboard, _logger, param.SaveScreenshotOnKeyTick, input, session, prototypes, cultureInfo: param.GameCultureInfo, stringLocalizer: param.StringLocalizer))
                            .UntilFailed(@"\")
                                .Sequence(Lang.S["GameTask_10725_146638"])
                                    .AlwaysSucceed(@"\")
                                        .Sequence(Lang.S["GameTask_10724_7b7d4f"])
                                            .PushLeaf(() => new MoveViewpointDown(Lang.S["GameTask_10715_834b53"], blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                            .MySimpleParallel(Lang.S["GameTask_10723_9dc65c"], policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                                .UntilSuccess(Lang.S["GameTask_10722_757f82"])
                                                    .Sequence("-")
                                                        .PushLeaf(() => new CheckInitalState(Lang.S["GameTask_10721_705acf"], blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                                        .PushLeaf(() => new GetFishpond(Lang.S["GameTask_10720_731180"], blackboard, _logger, param.SaveScreenshotOnKeyTick))
                                                    .End()
                                                .End()
                                                .PushLeaf(() => new FindFishTimeout(Lang.S["GameTask_10719_41c737"], 10, blackboard, _logger, param.SaveScreenshotOnKeyTick))
                                            .End()
                                            .PushLeaf(() => new ChooseBait(Lang.S["GameTask_10718_54378c"], blackboard, _logger, param.SaveScreenshotOnKeyTick, TaskContext.Instance().SystemInfo, input, session, prototypes))
                                            .MySimpleParallel(Lang.S["GameTask_10717_d49069"], policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                                .UntilSuccess(Lang.S["GameTask_10716_5dc63b"])
                                                    .Sequence("-")
                                                        .PushLeaf(() => new MoveViewpointDown(Lang.S["GameTask_10715_834b53"], blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                                            //.MySimpleParallel("举起鱼竿并抛竿", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                                            //    .PushLeaf(() => new LiftAndHold("举起鱼竿", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                                            .PushLeaf(() => new ThrowRod(Lang.S["GameTask_10714_cf8688"], blackboard, param.UseTorch, _logger, param.SaveScreenshotOnKeyTick, input))
                                                    //.End()
                                                    .End()
                                                .End()
                                                .Do(Lang.S["GameTask_10713_747f6b"], _ => (blackboard.abort || blackboard.throwRodNoTarget || blackboard.throwRodNoBaitFish) ? BehaviourStatus.Failed : BehaviourStatus.Running)
                                            .End()
                                            .MySimpleParallel(Lang.S["GameTask_10712_92bde1"], SimpleParallelPolicy.OnlyOneMustSucceed)
                                                .PushLeaf(() => new CheckThrowRod(Lang.S["GameTask_10711_ebcce2"], blackboard, _logger, param.SaveScreenshotOnKeyTick))    // todo 后面串联一个召回率高的下杆中检测方法
                                                .PushLeaf(() => new FishBite(Lang.S["GameTask_10710_4cee06"], blackboard, _logger, param.SaveScreenshotOnKeyTick, input, ocrService, cultureInfo: param.GameCultureInfo, stringLocalizer: param.StringLocalizer))
                                                .PushLeaf(() => new FishBiteTimeout(Lang.S["GameTask_10709_607774"], param.ThrowRodTimeOutTimeoutSeconds, _logger, param.SaveScreenshotOnKeyTick, input))
                                            .End()
                                            .MySimpleParallel(Lang.S["GameTask_10708_acf3a1"], policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                                .PushLeaf(() => new CheckRaiseHook(Lang.S["GameTask_10707_cd7b90"], blackboard, _logger, param.SaveScreenshotOnKeyTick))
                                                .Sequence(Lang.S["GameTask_10706_3038c7"])
                                                    .PushLeaf(() => new GetFishBoxArea(Lang.S["GameTask_10705_2a9ba4"], blackboard, _logger, param.SaveScreenshotOnKeyTick))
                                                    .PushLeaf(() => new Fishing(Lang.S["GameTask_10704_2d23c6"], blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                                .End()
                                            .End()
                                        .End()
                                    .End()
                                    .Do(Lang.S["GameTask_10703_b523d8"], _ => blackboard.abort ? BehaviourStatus.Failed : BehaviourStatus.Succeeded)
                                .End()
                            .End()
                        .End()
                        .PushLeaf(() => new WholeProcessTimeout(Lang.S["GameTask_10702_b8a48a"], param.WholeProcessTimeoutSeconds, _logger, param.SaveScreenshotOnKeyTick))
                    .End()
                    .PushLeaf(() => new QuitFishingMode(Lang.S["GameTask_10701_24ec64"], blackboard, _logger, param.SaveScreenshotOnKeyTick, input, param.GameCultureInfo, param.StringLocalizer))
                .End()
                .Build();
            // @formatter:on
            _logger.LogInformation("→ {Text}", Lang.S["GameTask_10700_13acbe"]);
            _logger.LogWarning(Lang.S["GameTask_10698_68b96f"], "跟宠");
            _logger.LogInformation(
                $"{Lang.S["GameTask_10697_d4640a"]});
            TaskContext.Instance().Config.AutoFishingConfig.Enabled = false;
            _logger.LogInformation(Lang.S["GameTask_10696_77705a"]);

            void tickARound()
            {
                blackboard.Reset();

                var prevManualGc = DateTime.MinValue;
                while (!ct.IsCancellationRequested)
                {
                    if (!SystemControl.IsGenshinImpactActiveByProcess())
                    {
                        var name = SystemControl.GetActiveByProcess();
                        _logger.LogWarning($"{Lang.S["GameTask_10695_b94701"]});
                        break;
                    }

                    using var bitmap =
                        TaskControl.CaptureGameImageNoRetry(TaskTriggerDispatcher.Instance().GameCapture);
                    if (bitmap == null)
                    {
                        _logger.LogWarning(Lang.S["GameTask_10694_4dad2c"]);
                        continue;
                    }

                    using var content = new CaptureContent(bitmap, 0, 0);
                    behaviourTree.Tick(content.CaptureRectArea);

                    if (behaviourTree.Status != BehaviourStatus.Running)
                    {
                        _logger.LogInformation(Lang.S["GameTask_10693_c123d3"]);

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
                _logger.LogInformation(Lang.S["GameTask_10692_1ccc77"]);
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

            _logger.LogInformation(Lang.S["GameTask_10691_f8c7fd"]);

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
                logger.LogInformation($"{Lang.S["GameTask_10690_8114df"]});
                timeout = DateTime.Now.AddSeconds(seconds);
            }

            protected override BehaviourStatus Update(ImageRegion _)
            {
                if (DateTime.Now >= timeout)
                {
                    logger.LogInformation($"{Lang.S["GameTask_10689_17e393"]});
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
                    logger.LogInformation($"{Lang.S["GameTask_10688_3c8237"]});
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
                    logger.LogInformation(Lang.S["GameTask_10687_a53c94"] + string.Join('、',
                        fishpond.Fishes.GroupBy(f => f.FishType).Select(g => $"{Lang.S["GameTask_10686_ee8a97"]})));
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

                    logger.LogInformation(Lang.S["GameTask_10685_604b95"]);
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
            private readonly InferenceSession session;
            private readonly Dictionary<string, float[]> prototypes;
            private readonly TimeProvider timeProvider;
            private DateTimeOffset? pressFWaitEndTime;
            private DateTimeOffset? clickWhiteConfirmButtonWaitEndTime;
            private DateTimeOffset? overallWaitEndTime;
            private readonly string fishingLocalizedString;

            public EnterFishingMode(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminate,
                IInputSimulator input, InferenceSession session, Dictionary<string, float[]> prototypes, TimeProvider? timeProvider = null, CultureInfo? cultureInfo = null, IStringLocalizer? stringLocalizer = null) : base(name,
                logger, saveScreenshotOnTerminate)
            {
                this.blackboard = blackboard;
                this.input = input;
                this.session = session;
                this.prototypes = prototypes;
                this.timeProvider = timeProvider ?? TimeProvider.System;
                this.fishingLocalizedString = stringLocalizer == null ? Lang.S["GameTask_10679_34e96b"] : stringLocalizer.WithCultureGet(cultureInfo, "钓鱼");
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
                    logger.LogInformation(Lang.S["GameTask_10684_28017d"]);
                    pressFWaitEndTime = timeProvider.GetLocalNow().AddSeconds(3);
                    return BehaviourStatus.Running;
                }
                else if ((clickWhiteConfirmButtonWaitEndTime == null ||
                          clickWhiteConfirmButtonWaitEndTime < timeProvider.GetLocalNow()) &&
                         Bv.ClickWhiteConfirmButton(imageRegion))
                {
                    using Mat subMat = imageRegion.SrcMat.SubMat(new Rect((int)(0.824 * imageRegion.Width), (int)(0.669 * imageRegion.Height), (int)(0.065 * imageRegion.Width), (int)(0.065 * imageRegion.Width)));
                    using Mat resized = subMat.Resize(new Size(125, 125));
                    (string predName, _) = GridIconsAccuracyTestTask.Infer(resized, this.session, this.prototypes);
                    if (predName.TryGetEnumValueFromDescription(out this.blackboard.selectedBait))
                    {
                        logger.LogInformation(Lang.S["GameTask_10683_3279d2"], this.blackboard.selectedBait.Value.GetDescription());
                    }
                    else
                    {
                        logger.LogInformation(Lang.S["GameTask_10682_d8f044"]);
                    }

                    this.blackboard.pitchReset = true;

                    clickWhiteConfirmButtonWaitEndTime = timeProvider.GetLocalNow().AddSeconds(3);

                    return BehaviourStatus.Running;
                }

                if (imageRegion.Find(blackboard.AutoFishingAssets.ExitFishingButtonRo).IsEmpty())
                {
                    if (overallWaitEndTime < timeProvider.GetLocalNow())
                    {
                        logger.LogInformation(Lang.S["GameTask_10681_9c6709"]);
                        return BehaviourStatus.Failed;
                    }
                    else
                    {
                        return BehaviourStatus.Running;
                    }
                }
                else
                {
                    logger.LogInformation(Lang.S["GameTask_10680_c9e1da"]);
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
                IInputSimulator input, CultureInfo? cultureInfo = null, IStringLocalizer? stringLocalizer = null) : base(name, logger, saveScreenshotOnTerminate)
            {
                this.blackboard = blackboard;
                this.input = input;
                this.fishingLocalizedString = stringLocalizer == null ? Lang.S["GameTask_10679_34e96b"] : stringLocalizer.WithCultureGet(cultureInfo, "钓鱼");
            }

            protected override BehaviourStatus Update(ImageRegion imageRegion)
            {
                if (Status == BehaviourStatus.Ready)
                {
                    return BehaviourStatus.Running;
                }

                if (Bv.FindF(imageRegion, this.fishingLocalizedString))
                {
                    logger.LogInformation(Lang.S["GameTask_10678_38092b"]);
                    return BehaviourStatus.Succeeded;
                }
                else if (Bv.ClickBlackConfirmButton(imageRegion))
                {
                    logger.LogInformation(Lang.S["GameTask_10677_7533c0"]);
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
