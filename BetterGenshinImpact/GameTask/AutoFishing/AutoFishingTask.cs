using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.GetGridIcons;
using CsTrees;
using CsTrees.Composites;
using Fischless.WindowsInput;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
            App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiFish);

        public AutoFishingTask(AutoFishingTaskParam param)
        {
            this.param = param;
        }

        public async Task Start(CancellationToken ct)
        {
            this._ct = ct;

            IOcrService ocrService = OcrFactory.Paddle;
            using InferenceSession session = GridIconsAccuracyTestTask.LoadModel(out Dictionary<string, float[]> prototypes);

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            // @formatter:off
            var builder = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("-", memory: false)
                        .OneShot(@"\")
                            .SetSleep("设置sleep方法", Sleep)
                        .End()
                        .Always("总是截图")
                            .TakeScreenshot("截图", _logger)
                        .Then()
                            .Parallel("在整体超时时间内钓鱼", policy: new ParallelPolicy.SuccessOnOne())
                                .Sequence("调整视角并钓鱼", memory: true)
                                    .Parallel("找鱼并且不超时", policy: new ParallelPolicy.SuccessOnAll())
                                        .Sequence("找鱼", memory: true)
                                            .MoveViewpointDown("调整视角至俯视", _logger, input)
                                            .Parallel("找鱼20秒", policy: new ParallelPolicy.SuccessOnOne())
                                                .TurnAround("转圈圈调整视角", _logger, input, _predictor)
                                                .FindFishTimeout("找到鱼", 20, _logger)
                                            .End()
                                        .End()
                                        .BubbleAbortCheck("终止检查")
                                    .End()
                                    .Try("进入钓鱼模式")
                                        .Sequence("-", memory: true)
                                            .EnterFishingMode("进入钓鱼模式", _logger, input, session, prototypes, cultureInfo: param.GameCultureInfo, stringLocalizer: param.StringLocalizer)
                                            .SuccessIsRunning(@"\")
                                                .Sequence("一直钓鱼直到没鱼", memory: false)
                                                    .FailureIsSuccess(@"总是成功")
                                                        .Sequence("从找鱼开始", memory: true)
                                                            .MoveViewpointDown("调整视角至俯视", _logger, input)
                                                            .Parallel("找鱼10秒", policy: new ParallelPolicy.SuccessOnOne())
                                                                .Sequence("-", memory: true)
                                                                    .CheckInitalState("初始状态确认", _logger, input)
                                                                    .GetFishpond("检测鱼群", _logger, _predictor)
                                                                .End()
                                                                .FindFishTimeout("确认初始状态和找到鱼", 10, _logger)
                                                            .End()
                                                            .ChooseBait("选择鱼饵", _logger, TaskContext.Instance().SystemInfo, input, session, prototypes)
                                                            .Try("抛竿等待上钩提竿")
                                                                .Sequence("抛竿等待上钩", memory: true)
                                                                    .Parallel("抛竿直到成功或出错", policy: new ParallelPolicy.SuccessOnOne())
                                                                        .FailureIsRunning("重复抛竿直到成功")
                                                                            .Selector("失败了就先抛竿然后提竿再抛", memory: true)
                                                                                .Try("举起鱼竿瞄准并抛竿")
                                                                                    .Sequence("-", memory: true)
                                                                                        .MoveViewpointDown("调整视角至俯视", _logger, input)
                                                                                        .LiftRod("举起鱼竿瞄准", _logger, input, _predictor)
                                                                                    .End()
                                                                                .Finally()
                                                                                    .Cast("抛竿", _logger, input)
                                                                                .End()
                                                                                .SuccessIsFailure("-")
                                                                                    .RaiseHook("提竿", _logger, input)
                                                                                .End()
                                                                            .End()
                                                                        .End()
                                                                        .CheckThrowRodResult("抛竿检查")
                                                                    .End()
                                                                    .Parallel("下杆中", new ParallelPolicy.SuccessOnOne())
                                                                        .CheckThrowRod("检查抛竿结果", _logger)    // todo 后面串联一个召回率高的下杆中检测方法
                                                                        .CheckFishBite("检查上钩", _logger, ocrService, cultureInfo: param.GameCultureInfo, stringLocalizer: param.StringLocalizer)
                                                                        .FishBiteTimeout("下杆超时检查", param.ThrowRodTimeOutTimeoutSeconds, _logger, input)
                                                                    .End()
                                                                .End()
                                                            .Finally()
                                                                .RaiseHook("提竿", _logger, input)
                                                            .End()
                                                            .Parallel("拉条中", policy: new ParallelPolicy.SuccessOnOne())
                                                                .CheckRaiseHook("检查提竿结果", _logger)
                                                                .Sequence("拉条序列", memory: true)
                                                                    .GetFishBoxArea("等待拉条出现", _logger, param.SaveScreenshotOnKeyTick)
                                                                    .Fishing("钓鱼拉条", _logger, param.SaveScreenshotOnKeyTick, input)
                                                                .End()
                                                            .End()
                                                        .End()
                                                    .End()
                                                    .BubbleAbortCheck("冒泡-终止检查")
                                                .End()
                                            .End()
                                        .End()
                                    .Finally()
                                        .QuitFishingMode("退出钓鱼模式", _logger, input, param.GameCultureInfo, param.StringLocalizer)
                                    .End()
                                .End()
                                .WholeProcessTimeout("检查整体超时", _logger, param.WholeProcessTimeoutSeconds)
                            .End()
                        .End()
                    .End()
                .End();
            // @formatter:on

            var behaviourTree = SetUpTree(builder);

            /*
            Blackboard blackboard = new Blackboard(_predictor, this.Sleep);

            var behaviourTree = FluentBuilder.Create<ImageRegion>()
                .Sequence("钓鱼并确保完成后退出钓鱼模式")
                    .MySimpleParallel("在整体超时时间内钓鱼", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                        .Sequence("调整视角并钓鱼")
                            .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                            .MySimpleParallel("找鱼20秒", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                .PushLeaf(() => new TurnAround("转圈圈调整视角", blackboard, _logger, param.SaveScreenshotOnKeyTick, input))
                                .PushLeaf(() => new FindFishTimeout("找到鱼", 20, blackboard, _logger, param.SaveScreenshotOnKeyTick))
                            .End()
                            .PushLeaf(() => new EnterFishingMode("进入钓鱼模式", blackboard, _logger, param.SaveScreenshotOnKeyTick, input, session, prototypes, cultureInfo: param.GameCultureInfo, stringLocalizer: param.StringLocalizer))
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
                                            .PushLeaf(() => new ChooseBait("选择鱼饵", blackboard, _logger, param.SaveScreenshotOnKeyTick, TaskContext.Instance().SystemInfo, input, session, prototypes))
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
                                                .PushLeaf(() => new FishBite("自动提竿", blackboard, _logger, param.SaveScreenshotOnKeyTick, input, ocrService, cultureInfo: param.GameCultureInfo, stringLocalizer: param.StringLocalizer))
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
                    .PushLeaf(() => new QuitFishingMode("退出钓鱼模式", blackboard, _logger, param.SaveScreenshotOnKeyTick, input, param.GameCultureInfo, param.StringLocalizer))
                .End()
                .Build();
            */
            _logger.LogInformation("→ {Text}", "自动钓鱼，启动！");
            _logger.LogWarning("请不要携带任何{Msg}，极有可能会误识别导致拖慢速度！", "跟宠");
            _logger.LogInformation(
                $"当前参数：{param.WholeProcessTimeoutSeconds}，{param.ThrowRodTimeOutTimeoutSeconds}，{param.FishingTimePolicy}, {param.SaveScreenshotOnKeyTick}, {param.GameCultureInfo}");
            TaskContext.Instance().Config.AutoFishingConfig.Enabled = false;
            _logger.LogInformation("全自动运行时，自动切换实时任务中的半自动钓鱼功能为关闭状态");

            async Task tickARound()
            {
                blackboard.Clear();
                behaviourTree = SetUpTree(builder);

                try
                {
                    var prevManualGc = DateTime.MinValue;
                    while (!ct.IsCancellationRequested)
                    {
                        if (!SystemControl.IsGenshinImpactActiveByProcess())
                        {
                            var name = SystemControl.GetActiveByProcess();
                            _logger.LogWarning($"当前获取焦点的窗口为: {name}，不是原神，停止执行");
                            return;
                        }

                        await behaviourTree.Tick();

                        if (behaviourTree.Root.Status != Status.Running)
                        {
                            //string snapshot = CsTrees.Display.Display.AsciiTree(behaviourTree.Root, showStatus: true);
                            //_logger.LogDebug(snapshot);
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
                finally
                {
                    if (ct.IsCancellationRequested)
                    {
                        _logger.LogInformation("钓鱼开始收尾");
                        var handoffTimeSecondsWriteAccess = blackboard.GrantExclusiveWrite<int>(null!, "HandoffTimeSeconds");
                        handoffTimeSecondsWriteAccess.Set(1);   // 暂时只考虑手动停止，那人类接管就在下一秒。未来应使用入参
                        var sleepWriteAccess = blackboard.GrantWrite<Action<int>>(null!, "Sleep");
                        sleepWriteAccess.Set(TaskControl.Sleep);

                        while (behaviourTree.Root.Status == Status.Running)
                        {
                            await behaviourTree.Handoff();
                        }
                        _logger.LogInformation("钓鱼收尾完毕");
                    }
                }
            }

            using var ra = TaskControl.CaptureToRectArea(forceNew: true);
            if (ra.FindMulti(RecognitionAssets.Get("AutoFight", "P", ra)).Count != 0)
            {
                _logger.LogInformation("当前处于联机状态，不使用昼夜设置");
                await tickARound();
            }
            else if (param.FishingTimePolicy == FishingTimePolicy.DontChange)
            {
                await tickARound();
            }
            else
            {
                SetTimeTask setTimeTask = new SetTimeTask();
                foreach (int hour in param.FishingTimePolicy == FishingTimePolicy.Daytime
                             ? [7]
                             : (param.FishingTimePolicy == FishingTimePolicy.Nighttime ? [19] : new int[] { 7, 19 }))
                {
                    setTimeTask.Start(hour, 0, ct).Wait(ct);
                    await tickARound();
                }
            }

            _logger.LogInformation("→ 钓鱼任务结束");

            return;
        }

        public void Sleep(int millisecondsTimeout)
        {
            TaskControl.Sleep(millisecondsTimeout, _ct);
        }

        private BehaviourTree SetUpTree(AutoFishingBuilder builder)
        {
            var behaviourTree = new BehaviourTree(builder.Build());
            if (param.SaveScreenshotOnKeyTick)
            {
                behaviourTree.AddVisitor(new ScreenshotVisitor(_logger));
            }
            return behaviourTree;
        }
    }
}
