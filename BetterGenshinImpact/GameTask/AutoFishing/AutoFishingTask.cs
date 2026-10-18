using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using CsTrees;
using CsTrees.Composites;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTask : ISoloTask
    {
        private readonly ILogger _logger = App.GetLogger<AutoFishingTask>();
        private IInputChannel input => InputHub.Foreground;
        public string Name => "钓鱼独立任务";

        private CancellationToken _ct;

        private readonly AutoFishingTaskParam param;

        /// <summary>
        /// 本次运行使用的运行环境：截图、等待、前台判断都只使用它
        /// </summary>
        private readonly GameRuntime _runtime;

        /// <summary>
        /// 配置根，由创建方传入
        /// </summary>
        private readonly AllConfig _config;

        /// <summary>
        /// 截图保存服务，只在 <see cref="AutoFishingTaskParam.SaveScreenshotOnKeyTick"/> 为 true 时使用；可以为 null
        /// </summary>
        private readonly GameScreenshotService? _screenshots;

        /// <summary>
        /// 创建钓鱼独立任务
        /// </summary>
        /// <param name="runtime">本次运行使用的运行环境</param>
        /// <param name="config">配置根</param>
        /// <param name="param">任务参数</param>
        /// <param name="screenshots">关键帧与出错截图的保存服务；只有开启 SaveScreenshotOnKeyTick 时需要，未提供时跳过保存</param>
        public AutoFishingTask(GameRuntime runtime, AllConfig config, AutoFishingTaskParam param, GameScreenshotService? screenshots = null)
        {
            _runtime = runtime;
            _config = config;
            this.param = param;
            _screenshots = screenshots;
        }

        public async Task Start(CancellationToken ct)
        {
            using var predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiFish);
            this._ct = ct;

            IOcrService ocrService = OcrFactory.Paddle;
            using IItemIconRecognizer itemRecognizer = ItemIconRecognizerFactory.CreateConfigured();

            // 出错截图只在开启关键帧截图且创建方提供了截图服务时保存
            var screenshots = param.SaveScreenshotOnKeyTick ? _screenshots : null;
            if (param.SaveScreenshotOnKeyTick && screenshots == null)
            {
                _logger.LogWarning("未提供截图服务，本次钓鱼不保存关键帧截图");
            }

            CsTrees.Blackboard.Blackboard blackboard = new CsTrees.Blackboard.Blackboard();

            // @formatter:off
            var builder = new AutoFishingBuilder()
                .WithBlackboard(blackboard)
                    .Sequence("-", memory: false)
                        .OneShot(@"\")
                            .SetSleep("设置sleep方法", Sleep)
                        .End()
                        .Always("总是截图")
                            .TakeScreenshot("截图", _runtime, _logger)
                        .Then()
                            .Parallel("在整体超时时间内钓鱼", policy: new ParallelPolicy.SuccessOnOne())
                                .Sequence("调整视角并钓鱼", memory: true)
                                    .Parallel("找鱼并且不超时", policy: new ParallelPolicy.SuccessOnAll())
                                        .Sequence("找鱼", memory: true)
                                            .MoveViewpointDown("调整视角至俯视", _logger, input)
                                            .Parallel("找鱼20秒", policy: new ParallelPolicy.SuccessOnOne())
                                                .TurnAround("转圈圈调整视角", _logger, input, predictor)
                                                .FindFishTimeout("找到鱼", 20, _logger)
                                            .End()
                                        .End()
                                        .BubbleAbortCheck("终止检查")
                                    .End()
                                    .Try("进入钓鱼模式")
                                        .Sequence("-", memory: true)
                                            .EnterFishingMode("进入钓鱼模式", _logger, input, itemRecognizer, cultureInfo: param.GameCultureInfo, stringLocalizer: param.StringLocalizer)
                                            .SuccessIsRunning(@"\")
                                                .Sequence("一直钓鱼直到没鱼", memory: false)
                                                    .FailureIsSuccess(@"总是成功")
                                                        .Sequence("从找鱼开始", memory: true)
                                                            .MoveViewpointDown("调整视角至俯视", _logger, input)
                                                            .Parallel("找鱼10秒", policy: new ParallelPolicy.SuccessOnOne())
                                                                .Sequence("-", memory: true)
                                                                    .CheckInitalState("初始状态确认", _logger, input)
                                                                    .GetFishpond("检测鱼群", _logger, predictor)
                                                                .End()
                                                                .FindFishTimeout("确认初始状态和找到鱼", 10, _logger)
                                                            .End()
                                                            .ChooseBait("选择鱼饵", _logger, _runtime.SystemInfo, input, itemRecognizer)
                                                            .Try("抛竿等待上钩提竿")
                                                                .Sequence("抛竿等待上钩", memory: true)
                                                                    .Parallel("抛竿直到成功或出错", policy: new ParallelPolicy.SuccessOnOne())
                                                                        .FailureIsRunning("重复抛竿直到成功")
                                                                            .Selector("失败了就先抛竿然后提竿再抛", memory: true)
                                                                                .Try("举起鱼竿瞄准并抛竿")
                                                                                    .Sequence("-", memory: true)
                                                                                        .MoveViewpointDown("调整视角至俯视", _logger, input)
                                                                                        .LiftRod("举起鱼竿瞄准", _logger, input, predictor)
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
                                                                    .GetFishBoxArea("等待拉条出现", _logger, param.SaveScreenshotOnKeyTick, null, screenshots)
                                                                    .Fishing("钓鱼拉条", _logger, param.SaveScreenshotOnKeyTick, input, null, screenshots)
                                                                .End()
                                                            .End()
                                                        .End()
                                                    .End()
                                                    .BubbleAbortCheck("冒泡-终止检查")
                                                .End()
                                            .End()
                                        .End()
                                    .Finally()
                                        .QuitFishingMode("退出钓鱼模式", _logger, input)
                                    .End()
                                .End()
                                .WholeProcessTimeout("检查整体超时", _logger, param.WholeProcessTimeoutSeconds)
                            .End()
                        .End()
                    .End()
                .End();
            // @formatter:on

            var behaviourTree = SetUpTree(builder, screenshots);

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
                            .PushLeaf(() => new EnterFishingMode("进入钓鱼模式", blackboard, _logger, param.SaveScreenshotOnKeyTick, input, itemRecognizer, cultureInfo: param.GameCultureInfo, stringLocalizer: param.StringLocalizer))
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
                                            .PushLeaf(() => new ChooseBait("选择鱼饵", blackboard, _logger, param.SaveScreenshotOnKeyTick, TaskContext.Instance().SystemInfo, input, itemRecognizer))
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
            _config.AutoFishingConfig.Enabled = false;
            _logger.LogInformation("全自动运行时，自动切换实时任务中的半自动钓鱼功能为关闭状态");

            async Task tickARound()
            {
                blackboard.Clear();
                behaviourTree = SetUpTree(builder, screenshots);

                try
                {
                    var prevManualGc = DateTime.MinValue;
                    while (!ct.IsCancellationRequested)
                    {
                        if (!_runtime.IsOperable())
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
                        // 收尾不受任务取消影响，但运行环境停止时仍会中断
                        sleepWriteAccess.Set(ms => _runtime.Sleep(ms));

                        while (behaviourTree.Root.Status == Status.Running)
                        {
                            await behaviourTree.Handoff();
                        }
                        _logger.LogInformation("钓鱼收尾完毕");
                    }
                }
            }

            using var ra = _runtime.CaptureToRectArea();
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
                SetTimeTask setTimeTask = new SetTimeTask(_runtime);
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
            _runtime.Sleep(millisecondsTimeout, _ct);
        }

        /// <summary>
        /// 构建行为树；提供了截图服务时挂上关键帧截图访问器
        /// </summary>
        /// <param name="builder">钓鱼行为树构建器</param>
        /// <param name="screenshots">截图保存服务，为 null 时不保存关键帧截图</param>
        private BehaviourTree SetUpTree(AutoFishingBuilder builder, GameScreenshotService? screenshots)
        {
            var behaviourTree = new BehaviourTree(builder.Build());
            if (screenshots != null)
            {
                behaviourTree.AddVisitor(new ScreenshotVisitor(screenshots, _logger));
            }
            return behaviourTree;
        }
    }
}
