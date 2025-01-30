using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing;
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
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using Vanara.PInvoke;
using System.Runtime.InteropServices;
using Compunet.YoloV8;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using System.Drawing;

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
        }

        public Task Start(CancellationToken ct)
        {
            this.ct = ct;

            this.blackboard = new Blackboard()
            {
                Sleep = this.Sleep,
                MoveViewpointDown = this.MoveViewpointDown
            };

            var BehaviourTree = FluentBuilder.Create<CaptureContent>()
                    .Sequence("调整视角并钓鱼")
                        .MySimpleParallel("找鱼20秒", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                            .PushLeaf(() => new FindFishTimeout("等20秒", 20, blackboard))
                            .PushLeaf(() => new ChangeView("调整视角"))
                        .End()
                        .MySimpleParallel("全部钓完", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                            .Do("各种检查", VariousCheck)
                            .UntilSuccess("钓鱼循环")
                                .AlwaysFail("钓鱼循环1")
                                    .Sequence("从找鱼开始")
                                        .MySimpleParallel("找鱼10秒", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                                            .PushLeaf(() => new FindFishTimeout("等10秒", 10, blackboard))
                                            .PushLeaf(() => new ThrowRod("抛竿前准备", blackboard))
                                        .End()
                                        .PushLeaf(() => new ChooseBait("选择鱼饵", blackboard))
                                        .UntilSuccess("重复抛竿")
                                            .PushLeaf(() => new ApproachFishAndThrowRod("抛竿", blackboard))
                                        .End()
                                        .Do("冒泡-抛竿-缺鱼检查", _ => blackboard.noTargetFish ? BehaviourStatus.Failed : BehaviourStatus.Succeeded)
                                        .PushLeaf(() => new CheckThrowRod("检查抛竿结果"))
                                        .MySimpleParallel("下杆中", SimpleParallelPolicy.OnlyOneMustSucceed)
                                            .PushLeaf(() => new FishBiteTimeout("下杆超时检查", 30))
                                            .PushLeaf(() => new FishBite("自动提竿"))
                                        .End()
                                        .PushLeaf(() => new GetFishBoxArea("等待拉条出现", blackboard))
                                        .PushLeaf(() => new Fishing("钓鱼拉条", blackboard))
                                    .End()
                                .End()
                            .End()
                        .End()
                    .End()
                    .Build();

            _logger.LogInformation("→ {Text}", "自动钓鱼，启动！");

            while (!ct.IsCancellationRequested)
            {
                var ra = TaskControl.CaptureToRectArea(forceNew: true);
                BehaviourTree.Tick(new CaptureContent(ra.SrcBitmap, 0, 0));

                if (BehaviourTree.Status == BehaviourStatus.Failed)
                {
                    _logger.LogInformation("→ 钓鱼任务结束");

                    if (!ra.Find(AutoFishingAssets.Instance.ExitFishingButtonRo).IsEmpty())
                    {
                        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                        Thread.Sleep(1000);
                        ra = TaskControl.CaptureToRectArea(forceNew: true);
                        Bv.ClickBlackConfirmButton(ra);
                        Thread.Sleep(1000);
                    }
                    break;
                }
            }


            return Task.CompletedTask;
        }

        public void Sleep(int millisecondsTimeout)
        {
            TaskControl.Sleep(millisecondsTimeout, ct);
        }

        /// <summary>
        /// 向下移动视角
        /// </summary>
        private void MoveViewpointDown()
        {
            // 下移视角方便看鱼
            Simulation.SendInput.Mouse.MoveMouseBy(0, 400);
            Sleep(500);
        }

        /// <summary>
        /// 检查是否在钓鱼界面、是否没鱼了
        /// </summary>
        /// <param name="content"></param>
        private BehaviourStatus VariousCheck(CaptureContent content)
        {
            if (blackboard.chooseBaitUIOpening)
            {
                return BehaviourStatus.Running;
            }
            else
            {
                if (content.CaptureRectArea.Find(AutoFishingAssets.Instance.ExitFishingButtonRo).IsEmpty())
                {
                    _logger.LogInformation("← {Text}", "退出钓鱼界面");

                    return BehaviourStatus.Failed;
                }
            }

            if (blackboard.noFish)
            {
                _logger.LogInformation("← {Text}", "找不到鱼了");

                return BehaviourStatus.Failed;
            }
            return BehaviourStatus.Running;
        }

        public class FindFishTimeout : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private readonly Blackboard blackboard;
            private DateTime? timeout;
            private int seconds;

            /// <summary>
            /// 如果未超时返回运行中，超时返回成功
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

        private class ChangeView : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            public ChangeView(string name) : base(name)
            {
            }

            private double theta = 0;

            private readonly Pen pen = new(Color.Red, 1);
            protected override BehaviourStatus Update(CaptureContent content)
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
                        content.CaptureRectArea.Derive(fish.Rect).DrawSelf($"{fish.FishType.ChineseName}.{i}", pen);
                    }
                    TaskControl.Sleep(1000);
                    VisionContext.Instance().DrawContent.ClearAll();

                    _logger.LogInformation("视角调整完毕");
                    return BehaviourStatus.Succeeded;
                }

                theta += Math.PI / 6;
                _logger.LogInformation(theta.ToString());
                double r = 30 + 1 * theta;
                int x = (int)(r * Math.Cos(theta));
                int y = (int)(2 * r * Math.Sin(theta));
                Simulation.SendInput.Mouse.MoveMouseBy(x, y);

                return BehaviourStatus.Running;
            }
        }
    }
}
