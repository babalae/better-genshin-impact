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

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTask : ISoloTask
    {
        private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
        public string Name => "钓鱼独立任务";

        private CancellationToken ct;

        private Blackboard blackboard;

        public class Blackboard : AutoFishing.Blackboard
        {
            public bool noFish = false;

            internal override void Reset()
            {
                base.Reset();
                noFish = false;
            }
        }

        public Task Start(CancellationToken ct)
        {
            this.ct = ct;

            this.blackboard = new Blackboard()
            {
                Sleep = this.Sleep
            };

            var BehaviourTree = FluentBuilder.Create<CaptureContent>()
                .Sequence("调整视角并钓鱼")
                    .Do($"设置变量{nameof(blackboard.pitchReset)}", _ =>
                    {
                        blackboard.pitchReset = true;
                        return BehaviourStatus.Succeeded;
                    })
                    .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard))
                    .MySimpleParallel("找鱼20秒", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                        .Do("转圈圈调整视角", TurnAround)
                        .PushLeaf(() => new FindFishTimeout("等20秒", 20, blackboard))
                    .End()
                    .PushLeaf(() => new EnterFishingMode("进入钓鱼模式", blackboard))
                    .UntilFailed(@"\")
                        .Sequence("一直钓鱼直到没鱼")
                            .AlwaysSucceed(@"\")
                                .Sequence("从找鱼开始")
                                    .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard))
                                    .MySimpleParallel("找鱼10秒", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                        .PushLeaf(() => new GetFishpond("检测鱼群", blackboard))
                                        .PushLeaf(() => new FindFishTimeout("等10秒", 10, blackboard))
                                    .End()
                                    .PushLeaf(() => new ChooseBait("选择鱼饵", blackboard))
                                    .UntilSuccess("重复抛竿")
                                        .Sequence("重复抛竿序列")
                                            .PushLeaf(() => new MoveViewpointDown("调整视角至俯视", blackboard))
                                            .PushLeaf(() => new ApproachFishAndThrowRod("抛竿", blackboard))
                                        .End()
                                    .End()
                                    .Do("冒泡-抛竿-缺鱼检查", _ => blackboard.noTargetFish ? BehaviourStatus.Failed : BehaviourStatus.Succeeded)
                                    .PushLeaf(() => new CheckThrowRod("检查抛竿结果"))
                                    .MySimpleParallel("下杆中", SimpleParallelPolicy.OnlyOneMustSucceed)
                                        .PushLeaf(() => new FishBite("自动提竿"))
                                        .PushLeaf(() => new FishBiteTimeout("下杆超时检查", 30))
                                    .End()
                                    .PushLeaf(() => new GetFishBoxArea("等待拉条出现", blackboard))
                                    .PushLeaf(() => new Fishing("钓鱼拉条", blackboard))
                                .End()
                            .End()
                            .Do("冒泡-找鱼-没鱼检查", _ => blackboard.noFish ? BehaviourStatus.Failed : BehaviourStatus.Succeeded)
                        .End()
                    .End()
                .End()
                .Build();

            _logger.LogInformation("→ {Text}", "自动钓鱼，启动！");

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

                    var ra = TaskControl.CaptureToRectArea(forceNew: true);
                    BehaviourTree.Tick(new CaptureContent(ra.SrcBitmap, 0, 0));

                    if (BehaviourTree.Status != BehaviourStatus.Running)
                    {
                        _logger.LogInformation("钓鱼结束");

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
            TaskControl.Sleep(millisecondsTimeout, ct);
        }

        public class FindFishTimeout : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private readonly Blackboard blackboard;
            private DateTime? timeout;
            private int seconds;

            /// <summary>
            /// 如果未超时返回运行中，超时返回失败
            /// </summary>
            /// <param name="name"></param>
            /// <param name="seconds"></param>
            public FindFishTimeout(string name, int seconds, Blackboard blackboard) : base(name)
            {
                this.blackboard = blackboard;
                this.seconds = seconds;
            }
            protected override void OnInitialize()
            {
                timeout = DateTime.Now.AddSeconds(seconds);
            }
            protected override BehaviourStatus Update(CaptureContent content)
            {
                if (DateTime.Now >= timeout)
                {
                    blackboard.noFish = true;
                    _logger.LogInformation($"{seconds}秒没有找到鱼，退出钓鱼界面");
                    return BehaviourStatus.Failed;
                }
                else
                {
                    return BehaviourStatus.Running;
                }
            }
        }

        private BehaviourStatus TurnAround(CaptureContent content)
        {
            using var memoryStream = new MemoryStream();
            content.CaptureRectArea.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
            memoryStream.Seek(0, SeekOrigin.Begin);
            var result = Blackboard.predictor.Detect(memoryStream);
            if (result.Boxes.Any())
            {
                Fishpond fishpond = new Fishpond(result);
                int i = 0;
                foreach (var fish in fishpond.Fishes)
                {
                    content.CaptureRectArea.Derive(fish.Rect).DrawSelf($"{fish.FishType.ChineseName}.{i}");
                }
                Sleep(1000);
                VisionContext.Instance().DrawContent.ClearAll();

                var oneFourthX = content.CaptureRectArea.SrcBitmap.Width / 4;
                var threeFourthX = content.CaptureRectArea.SrcBitmap.Width * 3 / 4;
                var centerY = content.CaptureRectArea.SrcBitmap.Height / 2;
                if (fishpond.FishpondRect.Left > threeFourthX)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(100, 0);
                    Sleep(100);
                    return BehaviourStatus.Running;
                }
                else if (fishpond.FishpondRect.Right < oneFourthX)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(-100, 0);
                    Sleep(100);
                    return BehaviourStatus.Running;
                }

                #region 1、使人物朝向和镜头方向一致；2、打断角色待机动作，避免钓鱼F交互键被吞
                // 加入昼夜切换后，使用KeyPress按S键被莫名吞掉了
                // 并且发现如果原地空格跳跃后紧跟按一下S键，角色会向侧后方走去
                // 于是使用“按一段时间”来代替KeyPress的“按一瞬间”，以求稳定的表现
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_S);
                Sleep(100);
                Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_S);
                Sleep(400);
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
                Sleep(100);
                Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
                Sleep(400);
                Sleep(300);
                #endregion

                _logger.LogInformation("视角调整完毕");
                return BehaviourStatus.Succeeded;
            }

            Simulation.SendInput.Mouse.MoveMouseBy(100, 0);
            Sleep(100);

            return BehaviourStatus.Running;
        }

        private class EnterFishingMode : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private readonly Blackboard blackboard;
            public EnterFishingMode(string name, Blackboard blackboard) : base(name)
            {
                this.blackboard = blackboard;
            }

            protected override BehaviourStatus Update(CaptureContent content)
            {
                if (Status == BehaviourStatus.Ready)
                {
                    return BehaviourStatus.Running;
                }

                if (Bv.FindFAndPress(content.CaptureRectArea, "钓鱼"))
                {
                    _logger.LogInformation("按下钓鱼键");
                    blackboard.Sleep(2000);
                    return BehaviourStatus.Running;
                }
                else if (Bv.ClickWhiteConfirmButton(content.CaptureRectArea))
                {
                    _logger.LogInformation("点击开始钓鱼");

                    this.blackboard.pitchReset = true;

                    blackboard.Sleep(2000);    // 这里要多等一会儿界面遮罩消退

                    return BehaviourStatus.Running;
                }

                if (content.CaptureRectArea.Find(AutoFishingAssets.Instance.ExitFishingButtonRo).IsEmpty())
                {
                    _logger.LogInformation("进入钓鱼模式失败");
                    return BehaviourStatus.Failed;
                }
                else
                {
                    _logger.LogInformation("进入钓鱼模式");
                    return BehaviourStatus.Succeeded;
                }
            }
        }
    }
}
