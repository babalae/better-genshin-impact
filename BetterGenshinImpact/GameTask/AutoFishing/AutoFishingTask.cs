using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
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
using Compunet.YoloV8;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using Fischless.WindowsInput;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTask : ISoloTask
    {
        private readonly ILogger _logger = App.GetLogger<AutoFishingTask>();
        private readonly InputSimulator input = Simulation.SendInput;
        public string Name => "钓鱼独立任务";

        private CancellationToken _ct;

        private readonly Blackboard blackboard;

        public AutoFishingTask()
        {
            var predictor = YoloV8Builder.CreateDefaultBuilder().UseOnnxModel(Global.Absolute(@"Assets\Model\Fish\bgi_fish.onnx")).WithSessionOptions(BgiSessionOption.Instance.Options).Build();
            this.blackboard = new Blackboard(predictor, this.Sleep);
        }

        public class Blackboard : AutoFishing.Blackboard
        {
            public bool noFish = false;

            public Blackboard(YoloV8Predictor predictor, Action<int> sleep) : base(predictor, sleep)
            {
            }

            internal override void Reset()
            {
                base.Reset();
                noFish = false;
            }
        }

        public Task Start(CancellationToken ct)
        {
            this._ct = ct;

            var autoThrowRodTimeOut = TaskContext.Instance().Config.AutoFishingConfig.AutoThrowRodTimeOut;

            var behaviourTree = FluentBuilder.Create<ImageRegion>()
                .Sequence("调整视角并钓鱼")
                    .Do($"设置变量{nameof(blackboard.pitchReset)}", _ =>
                    {
                        blackboard.pitchReset = true;
                        return BehaviourStatus.Succeeded;
                    })
                    .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard, _logger, input))
                    .MySimpleParallel("找鱼20秒", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                        .PushLeaf(() => new TurnAround("转圈圈调整视角", blackboard, _logger, input))
                        .PushLeaf(() => new FindFishTimeout("等20秒", 20, blackboard, _logger))
                    .End()
                    .PushLeaf(() => new EnterFishingMode("进入钓鱼模式", blackboard, _logger, input))
                    .UntilFailed(@"\")
                        .Sequence("一直钓鱼直到没鱼")
                            .AlwaysSucceed(@"\")
                                .Sequence("从找鱼开始")
                                    .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard, _logger, input))
                                    .MySimpleParallel("找鱼10秒", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                        .PushLeaf(() => new GetFishpond("检测鱼群", blackboard, _logger))
                                        .PushLeaf(() => new FindFishTimeout("等10秒", 10, blackboard, _logger))
                                    .End()
                                    .PushLeaf(() => new ChooseBait("选择鱼饵", blackboard, _logger, input))
                                    .UntilSuccess("重复抛竿")
                                        .Sequence("重复抛竿序列")
                                            .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard, _logger, input))
                                            .PushLeaf(() => new ThrowRod("抛竿", blackboard, _logger, input))
                                        .End()
                                    .End()
                                    .Do("冒泡-抛竿-缺鱼检查", _ => blackboard.noTargetFish ? BehaviourStatus.Failed : BehaviourStatus.Succeeded)
                                    .PushLeaf(() => new CheckThrowRod("检查抛竿结果", _logger))
                                    .MySimpleParallel("下杆中", SimpleParallelPolicy.OnlyOneMustSucceed)
                                        .PushLeaf(() => new FishBite("自动提竿", _logger, input))
                                        .PushLeaf(() => new FishBiteTimeout("下杆超时检查", autoThrowRodTimeOut, blackboard, _logger, input))
                                    .End()
                                    .PushLeaf(() => new GetFishBoxArea("等待拉条出现", blackboard, _logger))
                                    .PushLeaf(() => new Fishing("钓鱼拉条", blackboard, _logger, input))
                                .End()
                            .End()
                            .Do("冒泡-找鱼-没鱼检查", _ => blackboard.noFish ? BehaviourStatus.Failed : BehaviourStatus.Succeeded)
                        .End()
                    .End()
                .End()
                .Build();

            _logger.LogInformation("→ {Text}", "自动钓鱼，启动！");
            TaskContext.Instance().Config.AutoFishingConfig.Enabled = false;
            _logger.LogInformation("全自动运行时，自动切换实时任务中的半自动钓鱼功能为关闭状态");


            SetTimeTask setTimeTask = new SetTimeTask();
            foreach (int hour in new int[] { 7, 19 })
            {
                setTimeTask.Start(hour, 0, ct).Wait();

                this.blackboard.Reset();

                while (!ct.IsCancellationRequested)
                {
                    if (!SystemControl.IsGenshinImpactActiveByProcess())
                    {
                        _logger.LogInformation("当前获取焦点的窗口不是原神，停止执行");
                        break;
                    }

                    var bitmap = TaskControl.CaptureGameBitmapNoRetry(TaskTriggerDispatcher.Instance().GameCapture);
                    if (bitmap == null)
                    {
                        _logger.LogWarning("截图失败");
                        continue;
                    }
                    var content = new CaptureContent(bitmap, 0, 0);
                    behaviourTree.Tick(content.CaptureRectArea);

                    if (behaviourTree.Status != BehaviourStatus.Running)
                    {
                        _logger.LogInformation("钓鱼结束");


                        var ra = content.CaptureRectArea;
                        if (!ra.Find(AutoFishingAssets.Instance.ExitFishingButtonRo).IsEmpty())
                        {
                            _logger.LogInformation("← {Text}", "退出钓鱼界面");
                            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                            Sleep(1000);
                            ra = TaskControl.CaptureToRectArea(forceNew: true);
                            Bv.ClickBlackConfirmButton(ra);
                            Sleep(1000);
                        }
                        break;
                    }
                }
            }

            _logger.LogInformation("→ 钓鱼任务结束");

            return Task.CompletedTask;  // todo 这个行为树库不支持异步编程。。。
        }

        public void Sleep(int millisecondsTimeout)
        {
            TaskControl.Sleep(millisecondsTimeout, _ct);
        }

        public class FindFishTimeout : BaseBehaviour<ImageRegion>
        {
            private readonly ILogger logger;
            private readonly Blackboard blackboard;
            private DateTime? timeout;
            private int seconds;

            /// <summary>
            /// 如果未超时返回运行中，超时返回失败
            /// </summary>
            /// <param name="name"></param>
            /// <param name="seconds"></param>
            public FindFishTimeout(string name, int seconds, Blackboard blackboard, ILogger logger) : base(name)
            {
                this.blackboard = blackboard;
                this.logger = logger;
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
                    blackboard.noFish = true;
                    logger.LogInformation($"{seconds}秒没有找到鱼，退出钓鱼界面");
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
            private readonly ILogger logger;
            private readonly IInputSimulator input;
            private readonly Blackboard blackboard;
            public TurnAround(string name, Blackboard blackboard, ILogger logger, IInputSimulator input) : base(name)
            {
                this.blackboard = blackboard;
                this.logger = logger;
                this.input = input;
            }

            protected override BehaviourStatus Update(ImageRegion imageRegion)
            {
                using var memoryStream = new MemoryStream();
                imageRegion.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
                memoryStream.Seek(0, SeekOrigin.Begin);
                var result = blackboard.predictor.Detect(memoryStream);
                if (result.Boxes.Any())
                {
                    Fishpond fishpond = new Fishpond(result);
                    logger.LogInformation("定位到鱼塘：" + string.Join('、', fishpond.Fishes.GroupBy(f => f.FishType).Select(g => $"{g.Key.ChineseName}{g.Count()}条")));
                    int i = 0;
                    foreach (var fish in fishpond.Fishes)
                    {
                        imageRegion.Derive(fish.Rect).DrawSelf($"{fish.FishType.ChineseName}.{i++}");
                    }
                    blackboard.Sleep(1000);
                    VisionContext.Instance().DrawContent.ClearAll();

                    var oneFourthX = imageRegion.SrcBitmap.Width / 4;
                    var threeFourthX = imageRegion.SrcBitmap.Width * 3 / 4;
                    var centerY = imageRegion.SrcBitmap.Height / 2;
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
            private readonly ILogger logger;
            private readonly IInputSimulator input;
            private readonly Blackboard blackboard;
            public EnterFishingMode(string name, Blackboard blackboard, ILogger logger, IInputSimulator input) : base(name)
            {
                this.blackboard = blackboard;
                this.logger = logger;
                this.input = input;
            }

            protected override BehaviourStatus Update(ImageRegion imageRegion)
            {
                if (Status == BehaviourStatus.Ready)
                {
                    return BehaviourStatus.Running;
                }

                if (Bv.FindFAndPress(imageRegion, input.Keyboard, "钓鱼"))
                {
                    logger.LogInformation("按下钓鱼键");
                    blackboard.Sleep(2000);
                    return BehaviourStatus.Running;
                }
                else if (Bv.ClickWhiteConfirmButton(imageRegion))
                {
                    logger.LogInformation("点击开始钓鱼");

                    this.blackboard.pitchReset = true;

                    blackboard.Sleep(3000);    // 这里要多等一会儿界面遮罩消退

                    return BehaviourStatus.Running;
                }

                if (imageRegion.Find(AutoFishingAssets.Instance.ExitFishingButtonRo).IsEmpty())
                {
                    logger.LogInformation("进入钓鱼模式失败");
                    return BehaviourStatus.Failed;
                }
                else
                {
                    logger.LogInformation("进入钓鱼模式");
                    return BehaviourStatus.Succeeded;
                }
            }
        }
    }
}
