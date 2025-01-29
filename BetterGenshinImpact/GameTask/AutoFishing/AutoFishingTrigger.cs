using BehaviourTree;
using BehaviourTree.FluentBuilder;
using BehaviourTree.Composites;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Drawable;
using Compunet.YoloV8;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using static Vanara.PInvoke.User32;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using Point = OpenCvSharp.Point;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTrigger : ITaskTrigger
    {
        private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();

        public string Name => "自动钓鱼";
        public bool IsEnabled { get; set; }
        public int Priority => 15;

        /// <summary>
        /// 钓鱼是要独占模式的
        /// 在钓鱼的时候，不应该有其他任务在执行
        /// 在触发器发现正在钓鱼的时候，启用独占模式
        /// </summary>
        public bool IsExclusive { get; set; }

        private Blackboard blackboard;
        internal IBehaviour<CaptureContent> BehaviourTree { get; set; }

        /// <summary>
        /// 辣条（误）
        /// </summary>
        private IBehaviour<CaptureContent> BehaviourTreeLaTiao { get; set; }

        public AutoFishingTrigger()
        {
            this.blackboard = new Blackboard()
            {
                Sleep = this.Sleep,
                MoveViewpointDown = this.MoveViewpointDown
            };
        }

        public void Init()
        {
            IsEnabled = TaskContext.Instance().Config.AutoFishingConfig.Enabled;
            IsExclusive = false;

            BehaviourTree = FluentBuilder.Create<CaptureContent>()
                .MySimpleParallel("root", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                    .Do("检查是否在钓鱼界面", CheckFishingUserInterface)
                    .UntilSuccess("钓鱼循环")
                        .Sequence("从选鱼饵开始")
                            .PushLeaf(() => new ThrowRod("抛竿前准备", blackboard))
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
                .Build();

            BehaviourTreeLaTiao = FluentBuilder.Create<CaptureContent>()
                .MySimpleParallel("root", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                    .Do("检查是否在钓鱼界面", CheckFishingUserInterface)
                    .UntilSuccess("拉条循环")
                        .Sequence("拉条")
                            .PushLeaf(() => new GetFishBoxArea("等待拉条出现", blackboard))
                            .PushLeaf(() => new Fishing("钓鱼拉条", blackboard))
                        .End()
                    .End()
                .End()
                .Build();
        }

        private DateTime _prevExecute = DateTime.MinValue;

        public void OnCapture(CaptureContent content)
        {
            if ((DateTime.Now - _prevExecute).TotalMilliseconds <= 67)
            {
                return;
            }

            _prevExecute = DateTime.Now;

            // 进入独占的判定
            if (!IsExclusive)
            {
                // 进入独占模式判断
                CheckFishingUserInterface(content);
            }
            else
            {
                if (TaskContext.Instance().Config.AutoFishingConfig.AutoThrowRodEnabled)
                {
                    BehaviourTree.Tick(content);
                }
                else
                {
                    BehaviourTreeLaTiao.Tick(content);
                }
            }
        }

        // /// <summary>
        // /// 在“开始钓鱼”按钮上方安排一个我们的“开始自动钓鱼”按钮
        // /// 点击按钮进入独占模式
        // /// </summary>
        // /// <param name="content"></param>
        // /// <returns></returns>
        // [Obsolete]
        // private void DisplayButtonOnStartFishPageForExclusive(CaptureContent content)
        // {
        //     VisionContext.Instance().DrawContent.RemoveRect("StartFishingButton");
        //     var info = TaskContext.Instance().SystemInfo;
        //     var srcMat = content.CaptureRectArea.SrcMat;
        //     var rightBottomMat = CropHelper.CutRightBottom(srcMat, srcMat.Width / 2, srcMat.Height / 2);
        //     var list = CommonRecognition.FindGameButton(rightBottomMat);
        //     if (list.Count > 0)
        //     {
        //         foreach (var rect in list)
        //         {
        //             var ro = new RecognitionObject()
        //             {
        //                 Name = "StartFishingText",
        //                 RecognitionType = RecognitionTypes.OcrMatch,
        //                 RegionOfInterest = new Rect(srcMat.Width / 2, srcMat.Height / 2, srcMat.Width - srcMat.Width / 2,
        //                     srcMat.Height - srcMat.Height / 2),
        //                 AllContainMatchText = new List<string>
        //                 {
        //                     "开始", "钓鱼"
        //                 },
        //                 DrawOnWindow = false
        //             };
        //             var ocrRaRes = content.CaptureRectArea.Find(ro);
        //             if (ocrRaRes.IsEmpty())
        //             {
        //                 WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RemoveButton", new object(), "开始自动钓鱼"));
        //             }
        //             else
        //             {
        //                 VisionContext.Instance().DrawContent.PutRect("StartFishingButton", rect.ToWindowsRectangleOffset(srcMat.Width / 2, srcMat.Height / 2).ToRectDrawable());
        //
        //                 var btnPosition = new Rect(rect.X + srcMat.Width / 2, rect.Y + srcMat.Height / 2 - rect.Height - 10, rect.Width, rect.Height);
        //                 var maskButton = new MaskButton("开始自动钓鱼", btnPosition, () =>
        //                 {
        //                     VisionContext.Instance().DrawContent.RemoveRect("StartFishingButton");
        //                     _logger.LogInformation("→ {Text}", "自动钓鱼，启动！");
        //                     // 点击下面的按钮
        //                     var rc = info.CaptureAreaRect;
        //                     Simulation.SendInputEx
        //                         .Mouse
        //                         .MoveMouseTo(
        //                             (rc.X + srcMat.Width * 1d / 2 + rect.X + rect.Width * 1d / 2) * 65535 / info.DesktopRectArea.Width,
        //                             (rc.Y + srcMat.Height * 1d / 2 + rect.Y + rect.Height * 1d / 2) * 65535 / info.DesktopRectArea.Height)
        //                         .LeftButtonClick();
        //                     WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RemoveButton", new object(), "开始自动钓鱼"));
        //                     // 启动要延时一会等待钓鱼界面切换
        //                     Sleep(1000);
        //                     IsExclusive = true;
        //                     _switchBaitContinuouslyFrameNum = 0;
        //                     _waitBiteContinuouslyFrameNum = 0;
        //                     _noFishActionContinuouslyFrameNum = 0;
        //                     _isThrowRod = false;
        //                 });
        //                 WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "AddButton", new object(), maskButton));
        //             }
        //         }
        //     }
        //     else
        //     {
        //         WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RemoveButton", new object(), "开始自动钓鱼"));
        //     }
        // }

        //private bool OcrStartFishingForExclusive(CaptureContent content)
        //{
        //    var srcMat = content.CaptureRectArea.SrcMat;
        //    var rightBottomMat = CutHelper.CutRightBottom(srcMat, srcMat.Width / 2, srcMat.Height / 2);
        //    var text = _ocrService.Ocr(rightBottomMat.ToBitmap());
        //    if (!string.IsNullOrEmpty(text) && StringUtils.RemoveAllSpace(text).Contains("开始") && StringUtils.RemoveAllSpace(text).Contains("钓鱼"))
        //    {
        //        return true;
        //    }
        //    return false;
        //}

        /// <summary>
        /// 找右下角的退出钓鱼按钮
        /// 用于判断是否进入钓鱼界面
        /// 进入钓鱼界面时该触发器进入独占模式
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        private bool FindButtonForExclusive(CaptureContent content)
        {
            return !content.CaptureRectArea.Find(AutoFishingAssets.Instance.ExitFishingButtonRo).IsEmpty();
        }

        /// <summary>
        /// 钓鱼有3种场景
        /// 1. 未抛竿 BaitButtonRo存在 && WaitBiteButtonRo不存在
        /// 2. 抛竿后未拉条 WaitBiteButtonRo存在 && BaitButtonRo不存在
        /// 3. 上钩拉条
        ///
        /// 新AI钓鱼
        /// 前提：必须要正面面对鱼塘，没有识别到鱼的时候不会自动抛竿
        /// 1. 观察周围环境，判断鱼塘位置，视角对上鱼塘位置中心
        /// 2. 根据第一步的观察结果，提前选择鱼饵
        /// </summary>

        public class ThrowRod : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private readonly Blackboard blackboard;
            public ThrowRod(string name, Blackboard blackboard) : base(name)
            {
                this.blackboard = blackboard;
            }

            protected override BehaviourStatus Update(CaptureContent content)
            {
                _logger.LogDebug("ThrowRod");
                // 没有拉条和提竿的时候，自动抛竿
                var baitRectArea = content.CaptureRectArea.Find(AutoFishingAssets.Instance.BaitButtonRo);
                var waitBiteArea = content.CaptureRectArea.Find(AutoFishingAssets.Instance.WaitBiteButtonRo);
                if (!baitRectArea.IsEmpty() && waitBiteArea.IsEmpty())
                {
                    // 1. 观察周围环境，判断鱼塘位置，视角对上鱼塘位置中心
                    using var memoryStream = new MemoryStream();
                    content.CaptureRectArea.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    var result = Blackboard.predictor.Detect(memoryStream);
                    Debug.WriteLine($"YOLOv8识别: {result.Speed}");
                    var fishpond = new Fishpond(result);
                    if (fishpond.FishpondRect == Rect.Empty)
                    {
                        blackboard.Sleep(500);
                        return BehaviourStatus.Running;
                    }
                    else
                    {
                        var centerX = content.CaptureRectArea.SrcBitmap.Width / 2;
                        var centerY = content.CaptureRectArea.SrcBitmap.Height / 2;
                        // 往左移动是正数，往右移动是负数
                        if (fishpond.FishpondRect.Left > centerX)
                        {
                            Simulation.SendInput.Mouse.MoveMouseBy(100, 0);
                        }

                        if (fishpond.FishpondRect.Right < centerX)
                        {
                            Simulation.SendInput.Mouse.MoveMouseBy(-100, 0);
                        }

                        // 鱼塘尽量在上半屏幕
                        if (fishpond.FishpondRect.Bottom > centerY)
                        {
                            Simulation.SendInput.Mouse.MoveMouseBy(0, -100);
                        }

                        if ((fishpond.FishpondRect.Left < centerX && fishpond.FishpondRect.Right > centerX && fishpond.FishpondRect.Bottom >= centerY) || fishpond.FishpondRect.Width < content.CaptureRectArea.SrcBitmap.Width / 4)
                        {
                            blackboard.fishpond = fishpond;
                            _logger.LogInformation("定位到鱼塘：" + string.Join('、', fishpond.Fishes.GroupBy(f => f.FishType).Select(g => $"{g.Key.ChineseName}{g.Count()}条")));

                            return BehaviourStatus.Succeeded;
                        }
                    }
                }

                return BehaviourStatus.Running;
            }
        }

        private class FishBiteTimeout : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private DateTime? waitFishBiteTimeout;
            private int _seconds;

            /// <summary>
            /// 如果未超时返回运行中，超时返回失败
            /// </summary>
            /// <param name="name"></param>
            /// <param name="seconds"></param>
            public FishBiteTimeout(string name, int seconds) : base(name)
            {
                _seconds = seconds;
            }
            protected override void OnInitialize()
            {
                waitFishBiteTimeout = DateTime.Now.AddSeconds(_seconds);
            }
            protected override BehaviourStatus Update(CaptureContent context)
            {
                if (DateTime.Now >= waitFishBiteTimeout)
                {
                    _logger.LogInformation($"{_seconds}秒没有咬杆，本次收杆");
                    Simulation.SendInput.Mouse.LeftButtonClick();
                    Thread.Sleep(1000);
                    return BehaviourStatus.Failed;
                }
                else
                {
                    return BehaviourStatus.Running;
                }
            }
        }

        /// <summary>
        /// 选择鱼饵
        /// </summary>
        private class ChooseBait : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private readonly Blackboard blackboard;
            private DateTime? chooseBaitUIOpenWaitEndTime; // 等待选鱼饵界面出现并尝试找鱼饵的结束时间

            /// <summary>
            /// 选择鱼饵
            /// </summary>
            /// <param name="name"></param>
            /// <param name="autoFishingTrigger"></param>
            public ChooseBait(string name, Blackboard blackboard) : base(name)
            {
                this.blackboard = blackboard;
            }

            protected override BehaviourStatus Update(CaptureContent content)
            {
                if (this.Status == BehaviourStatus.Ready)   // 第一次进来直接返回，更新截图
                {
                    chooseBaitUIOpenWaitEndTime = DateTime.Now.AddSeconds(3);
                    _logger.LogInformation("打开换饵界面");
                    Simulation.SendInput.Mouse.RightButtonClick();
                    blackboard.Sleep(100);
                    Simulation.SendInput.Mouse.MoveMouseBy(0, 200); // 鼠标移走，防止干扰
                    blackboard.Sleep(100);
                    return BehaviourStatus.Running;
                }

                blackboard.selectedBaitName = blackboard.fishpond.Fishes.GroupBy(f => f.FishType).OrderByDescending(g => g.Count()).First().First().FishType.BaitName; // 选择最多鱼吃的饵料
                _logger.LogInformation("选择鱼饵 {Text}", BaitType.FromName(blackboard.selectedBaitName).ChineseName);

                // 寻找鱼饵
                var ro = new RecognitionObject
                {
                    Name = "ChooseBait",
                    RecognitionType = RecognitionTypes.TemplateMatch,
                    TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFishing", $"bait\\{blackboard.selectedBaitName}.png"),
                    Threshold = 0.8,
                    Use3Channels = true,
                    DrawOnWindow = false
                }.InitTemplate();

                var captureRegion = content.CaptureRectArea;
                using var resRa = captureRegion.Find(ro);
                if (resRa.IsEmpty())
                {
                    if (DateTime.Now >= chooseBaitUIOpenWaitEndTime)
                    {
                        Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
                        _logger.LogWarning("没有找到目标鱼饵");
                        blackboard.selectedBaitName = string.Empty;
                        return BehaviourStatus.Failed;
                    }
                    else
                    {
                        return BehaviourStatus.Running;
                    }
                }
                else
                {
                    resRa.Click();
                    blackboard.Sleep(700);
                    // 可能重复点击，所以固定界面点击下
                    captureRegion.ClickTo((int)(captureRegion.Width * 0.675), (int)(captureRegion.Height / 3d));
                    blackboard.Sleep(200);
                    // 点击确定
                    Bv.ClickWhiteConfirmButton(captureRegion);
                    blackboard.Sleep(500); // 等待界面切换
                }

                return BehaviourStatus.Succeeded;
            }
        }

        /// <summary>
        /// 抛竿
        /// </summary>
        public class ApproachFishAndThrowRod : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private readonly Blackboard blackboard;

            private int noPlacementTimes; // 没有落点的次数
            private int noTargetFishTimes; // 没有目标鱼的次数
            public ApproachFishAndThrowRod(string name, Blackboard blackboard) : base(name)
            {
                this.blackboard = blackboard;
            }

            protected override void OnInitialize()
            {
                noPlacementTimes = 0;
                noTargetFishTimes = 0;

                Simulation.SendInput.Mouse.LeftButtonDown();
                _logger.LogInformation("长按预抛竿");
                blackboard.Sleep(3000);
            }

            protected override BehaviourStatus Update(CaptureContent content)
            {
                _logger.LogDebug("ApproachFishAndThrowRod");
                blackboard.noTargetFish = false;
                var prevTargetFishRect = Rect.Empty; // 记录上一个目标鱼的位置

                var ra = content.CaptureRectArea;

                // 找 鱼饵落点
                using var memoryStream = new MemoryStream();
                ra.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
                memoryStream.Seek(0, SeekOrigin.Begin);
                var result = Blackboard.predictor.Detect(memoryStream);
                Debug.WriteLine($"YOLOv8识别: {result.Speed}");
                var fishpond = new Fishpond(result, includeTarget: true);
                Random _rd = new();
                if (fishpond.TargetRect == null || fishpond.TargetRect == Rect.Empty)
                {
                    noPlacementTimes++;
                    blackboard.Sleep(50);
                    Debug.WriteLine("历次未找到鱼饵落点");

                    var cX = ra.SrcBitmap.Width / 2;
                    var cY = ra.SrcBitmap.Height / 2;
                    var rdX = _rd.Next(0, ra.SrcBitmap.Width);
                    var rdY = _rd.Next(0, ra.SrcBitmap.Height);

                    var moveX = 100 * (cX - rdX) / ra.SrcBitmap.Width;
                    var moveY = 100 * (cY - rdY) / ra.SrcBitmap.Height;

                    Simulation.SendInput.Mouse.MoveMouseBy(moveX, moveY);

                    if (noPlacementTimes > 25)
                    {
                        _logger.LogInformation("未找到鱼饵落点，重试");
                        Simulation.SendInput.Mouse.LeftButtonUp();
                        blackboard.Sleep(2000);
                        Simulation.SendInput.Mouse.LeftButtonClick();
                        blackboard.Sleep(500);
                        blackboard.MoveViewpointDown();
                        blackboard.Sleep(1000);    //此处需要久一点
                        return BehaviourStatus.Failed;
                    }

                    return BehaviourStatus.Running;
                }

                Rect fishpondTargetRect = (Rect)fishpond.TargetRect;

                // 找到落点最近的鱼
                OneFish? currentFish = null;
                if (prevTargetFishRect == Rect.Empty)
                {
                    var list = fishpond.FilterByBaitName(blackboard.selectedBaitName);
                    if (list.Count > 0)
                    {
                        currentFish = list[0];
                        prevTargetFishRect = currentFish.Rect;
                    }
                }
                else
                {
                    currentFish = fishpond.FilterByBaitNameAndRecently(blackboard.selectedBaitName, prevTargetFishRect);
                    if (currentFish != null)
                    {
                        prevTargetFishRect = currentFish.Rect;
                    }
                }

                if (currentFish == null)
                {
                    Debug.WriteLine("无目标鱼");
                    noTargetFishTimes++;
                    //if (noTargetFishTimes == 30)
                    //{
                    //    Simulation.SendInputEx.Mouse.MoveMouseBy(0, 100);
                    //}

                    if (noTargetFishTimes > 10)
                    {
                        // 没有找到目标鱼，重新选择鱼饵
                        _logger.LogInformation("没有找到目标鱼，1.直接抛竿");
                        Simulation.SendInput.Mouse.LeftButtonUp();
                        blackboard.Sleep(2000);
                        _logger.LogInformation("没有找到目标鱼，2.收杆");
                        Simulation.SendInput.Mouse.LeftButtonClick();
                        blackboard.Sleep(500);
                        _logger.LogInformation("没有找到目标鱼，3.准备重新选择鱼饵");
                        blackboard.selectedBaitName = string.Empty;
                        blackboard.MoveViewpointDown();
                        blackboard.Sleep(300);

                        blackboard.noTargetFish = true;
                        return BehaviourStatus.Succeeded;
                    }

                    return BehaviourStatus.Running;
                }
                else
                {
                    noTargetFishTimes = 0;
                    content.CaptureRectArea.DrawRect(fishpondTargetRect, "Target");
                    content.CaptureRectArea.Derive(currentFish.Rect).DrawSelf("Fish");

                    // VisionContext.Instance().DrawContent.PutRect("Target", fishpond.TargetRect.ToRectDrawable());
                    // VisionContext.Instance().DrawContent.PutRect("Fish", currentFish.Rect.ToRectDrawable());

                    // 来自 HutaoFisher 的抛竿技术
                    var rod = fishpondTargetRect;
                    var fish = currentFish.Rect;
                    var dx = NormalizeXTo1024(fish.Left + fish.Right - rod.Left - rod.Right) / 2.0;
                    var dy = NormalizeYTo576(fish.Top + fish.Bottom - rod.Top - rod.Bottom) / 2.0;
                    var state = RodNet.GetRodState(new RodInput
                    {
                        rod_x1 = NormalizeXTo1024(rod.Left),
                        rod_x2 = NormalizeXTo1024(rod.Right),
                        rod_y1 = NormalizeYTo576(rod.Top),
                        rod_y2 = NormalizeYTo576(rod.Bottom),
                        fish_x1 = NormalizeXTo1024(fish.Left),
                        fish_x2 = NormalizeXTo1024(fish.Right),
                        fish_y1 = NormalizeYTo576(fish.Top),
                        fish_y2 = NormalizeYTo576(fish.Bottom),
                        fish_label = BigFishType.GetIndex(currentFish.FishType)
                    });

                    //// 如果hutao钓鱼暂时没有更新导致报错，可以先用这段凑合
                    //int state;
                    //System.Drawing.Rectangle rod3XRectangle = new System.Drawing.Rectangle(rod.Left - rod.Width, rod.Top - rod.Height, rod.Width * 3, rod.Height * 3);
                    //System.Drawing.Rectangle rod5XRectangle = new System.Drawing.Rectangle(rod.Left - rod.Width * 2, rod.Top - rod.Height * 2, rod.Width * 5, rod.Height * 5);
                    //System.Drawing.Rectangle fishRectangle = new System.Drawing.Rectangle(fish.Left, fish.Top, fish.Width, fish.Height);
                    //if (rod3XRectangle.IntersectsWith(fishRectangle))
                    //{
                    //    state = 1;
                    //}
                    //else if (rod5XRectangle.IntersectsWith(fishRectangle))
                    //{
                    //    state = 0;
                    //}
                    //else
                    //{
                    //    state = 2;
                    //}

                    if (state == -1)
                    {
                        // 失败 随机移动鼠标
                        var cX = content.CaptureRectArea.SrcBitmap.Width / 2;
                        var cY = content.CaptureRectArea.SrcBitmap.Height / 2;
                        var rdX = _rd.Next(0, content.CaptureRectArea.SrcBitmap.Width);
                        var rdY = _rd.Next(0, content.CaptureRectArea.SrcBitmap.Height);

                        var moveX = 100 * (cX - rdX) / content.CaptureRectArea.SrcBitmap.Width;
                        var moveY = 100 * (cY - rdY) / content.CaptureRectArea.SrcBitmap.Height;

                        _logger.LogInformation("失败 随机移动 {DX}, {DY}", moveX, moveY);
                        Simulation.SendInput.Mouse.MoveMouseBy(moveX, moveY);
                    }
                    else if (state == 0)
                    {
                        // 成功 抛竿
                        Simulation.SendInput.Mouse.LeftButtonUp();
                        _logger.LogInformation("尝试钓取 {Text}", currentFish.FishType.ChineseName);
                        VisionContext.Instance().DrawContent.RemoveRect("Target");
                        VisionContext.Instance().DrawContent.RemoveRect("Fish");
                        return BehaviourStatus.Succeeded;
                    }
                    else if (state == 1)
                    {
                        // 太近
                        var dl = Math.Sqrt(dx * dx + dy * dy);
                        // set a minimum step
                        dx = dx / dl * 30;
                        dy = dy / dl * 30;
                        // _logger.LogInformation("太近 移动 {DX}, {DY}", dx, dy);
                        Simulation.SendInput.Mouse.MoveMouseBy((int)(-dx / 1.5), (int)(-dy * 1.5));
                    }
                    else if (state == 2)
                    {
                        // 太远
                        // _logger.LogInformation("太远 移动 {DX}, {DY}", dx, dy);
                        Simulation.SendInput.Mouse.MoveMouseBy((int)(dx / 1.5), (int)(dy * 1.5));
                    }
                }
                blackboard.Sleep(50);
                return BehaviourStatus.Running;
            }

            private double NormalizeXTo1024(int x)
            {
                return x * 1.0 / TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect.Width * 1024;
            }

            private double NormalizeYTo576(int y)
            {
                return y * 1.0 / TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect.Height * 576;
            }

        }

        /// <summary>
        /// 检查抛竿结果
        /// 避免往红色靶点抛竿导致失败
        /// </summary>
        /// <param name="content"></param>
        private class CheckThrowRod : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private DateTime? timeDelay;

            /// <summary>
            /// 检查抛竿结果
            /// </summary>
            /// <param name="name"></param>
            public CheckThrowRod(string name) : base(name)
            {
            }

            protected override void OnInitialize()
            {
                timeDelay = DateTime.Now.AddSeconds(3);
            }

            protected override BehaviourStatus Update(CaptureContent content)
            {
                if (DateTime.Now < timeDelay)
                {
                    return BehaviourStatus.Running;
                }

                Region baitRectArea = content.CaptureRectArea.Find(AutoFishingAssets.Instance.BaitButtonRo);
                if (baitRectArea.IsEmpty())
                {
                    return BehaviourStatus.Succeeded;
                }
                else
                {
                    _logger.LogInformation("抛竿失败");
                    return BehaviourStatus.Failed;
                }
            }
        }

        /// <summary>
        /// 向下移动视角
        /// </summary>
        private void MoveViewpointDown()
        {
            if (TaskContext.Instance().Config.AutoFishingConfig.AutoThrowRodEnabled)
            {
                // 下移视角方便看鱼
                Simulation.SendInput.Mouse.MoveMouseBy(0, 400);
                Sleep(500);
                //Simulation.SendInput.Mouse.MoveMouseBy(0, 500);
                //Sleep(500);
            }
        }

        [Obsolete]
        private (int, int) MoveMouseToFish(Rect rect1, Rect rect2)
        {
            int minDistance;

            //首先计算两个矩形中心点
            Point c1, c2;
            c1.X = rect1.X + (rect1.Width / 2);
            c1.Y = rect1.Y + (rect1.Height / 2);
            c2.X = rect2.X + (rect2.Width / 2);
            c2.Y = rect2.Y + (rect2.Height / 2);

            // 分别计算两矩形中心点在X轴和Y轴方向的距离
            var dx = Math.Abs(c2.X - c1.X);
            var dy = Math.Abs(c2.Y - c1.Y);

            //两矩形不相交，在X轴方向有部分重合的两个矩形
            if (dx < (rect1.Width + rect2.Width) / 2 && dy >= (rect1.Height + rect2.Height) / 2)
            {
                minDistance = dy - ((rect1.Height + rect2.Height) / 2);

                var moveY = 5;
                if (minDistance >= 100)
                {
                    moveY = 50;
                }

                if (c1.Y > c2.Y)
                {
                    moveY = -moveY;
                }

                //_logger.LogInformation("移动鼠标 {X} {Y}", 0, moveY);
                Simulation.SendInput.Mouse.MoveMouseBy(0, moveY);
                return (0, minDistance);
            }

            //两矩形不相交，在Y轴方向有部分重合的两个矩形
            else if (dx >= (rect1.Width + rect2.Width) / 2 && (dy < (rect1.Height + rect2.Height) / 2))
            {
                minDistance = dx - ((rect1.Width + rect2.Width) / 2);
                var moveX = 10;
                if (minDistance >= 100)
                {
                    moveX = 50;
                }

                if (c1.X > c2.X)
                {
                    moveX = -moveX;
                }

                //_logger.LogInformation("移动鼠标 {X} {Y}", moveX, 0);
                Simulation.SendInput.Mouse.MoveMouseBy(moveX, 0);
                return (minDistance, 0);
            }

            //两矩形不相交，在X轴和Y轴方向无重合的两个矩形
            else if ((dx >= ((rect1.Width + rect2.Width) / 2)) && (dy >= ((rect1.Height + rect2.Height) / 2)))
            {
                var dpX = dx - ((rect1.Width + rect2.Width) / 2);
                var dpY = dy - ((rect1.Height + rect2.Height) / 2);
                //minDistance = (int)Math.Sqrt(dpX * dpX + dpY * dpY);
                var moveX = 10;
                if (dpX >= 100)
                {
                    moveX = 50;
                }

                var moveY = 5;
                if (dpY >= 100)
                {
                    moveY = 50;
                }

                if (c1.Y > c2.Y)
                {
                    moveY = -moveY;
                }

                if (c1.X > c2.X)
                {
                    moveX = -moveX;
                }

                //_logger.LogInformation("移动鼠标 {X} {Y}", moveX, moveY);
                Simulation.SendInput.Mouse.MoveMouseBy(moveX, moveY);
                return (dpX, dpY);
            }

            //两矩形相交
            else
            {
                //_logger.LogInformation("无需移动鼠标");
                minDistance = -1;
                return (0, 0);
            }
        }

        public void Sleep(int millisecondsTimeout)
        {
            NewRetry.Do(() =>
            {
                TaskControl.TrySuspend();
                if (IsEnabled && !SystemControl.IsGenshinImpactActiveByProcess())
                {
                    _logger.LogWarning("当前获取焦点的窗口不是原神，暂停");
                    throw new RetryException("当前获取焦点的窗口不是原神");
                }

            }, TimeSpan.FromSeconds(1), 100);
            //CheckFishingUserInterface(_currContent);
            Thread.Sleep(millisecondsTimeout);
        }

        /// <summary>
        /// 自动提竿
        /// </summary>
        public class FishBite : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private readonly IOcrService ocrService = OcrFactory.Paddle;
            public FishBite(string name) : base(name)
            {
            }
            protected override BehaviourStatus Update(CaptureContent content)
            {
                _logger.LogDebug("FishBite");
                // 自动识别的钓鱼框向下延伸到屏幕中间
                //var liftingWordsAreaRect = new Rect(fishBoxRect.X, fishBoxRect.Y + fishBoxRect.Height * 2,
                //    fishBoxRect.Width, content.CaptureRectArea.SrcMat.Height / 2 - fishBoxRect.Y - fishBoxRect.Height * 5);
                // 上半屏幕和中间1/3的区域
                var liftingWordsAreaRect = new Rect(content.CaptureRectArea.SrcMat.Width / 3, 0, content.CaptureRectArea.SrcMat.Width / 3,
                    content.CaptureRectArea.SrcMat.Height / 2);
                //VisionContext.Instance().DrawContent.PutRect("liftingWordsAreaRect", liftingWordsAreaRect.ToRectDrawable(new Pen(Color.Cyan, 2)));
                var wordCaptureMat = new Mat(content.CaptureRectArea.SrcMat, liftingWordsAreaRect);
                var currentBiteWordsTips = AutoFishingImageRecognition.MatchFishBiteWords(wordCaptureMat, liftingWordsAreaRect);
                if (currentBiteWordsTips != Rect.Empty)
                {
                    // VisionContext.Instance().DrawContent.PutRect("FishBiteTips",
                    //     currentBiteWordsTips
                    //         .ToWindowsRectangleOffset(liftingWordsAreaRect.X, liftingWordsAreaRect.Y)
                    //         .ToRectDrawable());
                    using var tipsRa = content.CaptureRectArea.Derive(currentBiteWordsTips + liftingWordsAreaRect.Location);
                    tipsRa.DrawSelf("FishBiteTips");

                    // 图像提竿判断
                    using var liftRodButtonRa = content.CaptureRectArea.Find(AutoFishingAssets.Instance.LiftRodButtonRo);
                    if (!liftRodButtonRa.IsEmpty())
                    {
                        Simulation.SendInput.Mouse.LeftButtonClick();
                        _logger.LogInformation(@"┌------------------------┐");
                        _logger.LogInformation("  自动提竿(图像识别)");
                        VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
                        return BehaviourStatus.Succeeded;
                    }

                    // OCR 提竿判断
                    var text = ocrService.Ocr(new Mat(content.CaptureRectArea.SrcGreyMat,
                        new Rect(currentBiteWordsTips.X + liftingWordsAreaRect.X,
                            currentBiteWordsTips.Y + liftingWordsAreaRect.Y,
                            currentBiteWordsTips.Width, currentBiteWordsTips.Height)));
                    if (!string.IsNullOrEmpty(text) && StringUtils.RemoveAllSpace(text).Contains("上钩"))
                    {
                        Simulation.SendInput.Mouse.LeftButtonClick();
                        _logger.LogInformation(@"┌------------------------┐");
                        _logger.LogInformation("  自动提竿(OCR)");
                        VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
                        return BehaviourStatus.Succeeded;
                    }

                    Simulation.SendInput.Mouse.LeftButtonClick();
                    _logger.LogInformation(@"┌------------------------┐");
                    _logger.LogInformation("  自动提竿(文字块)");
                    VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
                    return BehaviourStatus.Succeeded;
                }

                return BehaviourStatus.Running;
            }
        }

        /// <summary>
        /// 进入钓鱼界面先尝试获取钓鱼框的位置
        /// </summary>
        public class GetFishBoxArea : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private readonly Blackboard blackboard;
            public GetFishBoxArea(string name, Blackboard blackboard) : base(name)
            {
                this.blackboard = blackboard;
            }

            protected override BehaviourStatus Update(CaptureContent content)
            {
                _logger.LogDebug("GetFishBoxArea");

                using var topMat = new Mat(content.CaptureRectArea.SrcMat, new Rect(0, 0, content.CaptureRectArea.Width, content.CaptureRectArea.Height / 2));

                var rects = AutoFishingImageRecognition.GetFishBarRect(topMat);
                if (rects != null && rects.Count == 2)
                {
                    Rect _cur, _left;
                    if (Math.Abs(rects[0].Height - rects[1].Height) > 10)
                    {
                        TaskControl.Logger.LogError("两个矩形高度差距过大，未识别到钓鱼框");
                        VisionContext.Instance().DrawContent.RemoveRect("FishBox");
                        return BehaviourStatus.Running;
                    }

                    if (rects[0].Width < rects[1].Width)
                    {
                        _cur = rects[0];
                        _left = rects[1];
                    }
                    else
                    {
                        _cur = rects[1];
                        _left = rects[0];
                    }

                    if (_left.X < _cur.X // cur 是游标位置, 在初始状态下，cur 一定在left左边
                        || _cur.Width > _left.Width // left一定比cur宽
                        || _cur.X + _cur.Width > topMat.Width / 2 // cur 一定在屏幕左侧
                        || _cur.X + _cur.Width > _left.X - _left.Width / 2 // cur 一定在left左侧+left的一半宽度
                        || _cur.X + _cur.Width > topMat.Width / 2 - _left.Width // cur 一定在屏幕中轴线减去整个left的宽度的位置左侧
                        || !(_left.X < topMat.Width / 2 && _left.X + _left.Width > topMat.Width / 2) // left肯定穿过游戏中轴线
                       )
                    {
                        VisionContext.Instance().DrawContent.RemoveRect("FishBox");
                        return BehaviourStatus.Running;
                    }

                    int hExtra = _cur.Height, vExtra = _cur.Height / 4;
                    blackboard.fishBoxRect = new Rect(_cur.X - hExtra, _cur.Y - vExtra,
                         (_left.X + _left.Width / 2 - _cur.X) * 2 + hExtra * 2, _cur.Height + vExtra * 2);
                    // VisionContext.Instance().DrawContent.PutRect("FishBox", _fishBoxRect.ToRectDrawable(new Pen(Color.LightPink, 2)));
                    using var boxRa = content.CaptureRectArea.Derive(blackboard.fishBoxRect);
                    boxRa.DrawSelf("FishBox", new Pen(Color.LightPink, 2));
                    return BehaviourStatus.Succeeded;
                }

                VisionContext.Instance().DrawContent.RemoveRect("FishBox");
                return BehaviourStatus.Running;

                //CheckFishingUserInterface(content);
            }
        }

        public class Fishing : BaseBehaviour<CaptureContent>
        {
            private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
            private readonly Blackboard blackboard;
            public Fishing(string name, Blackboard blackboard) : base(name)
            {
                this.blackboard = blackboard;
            }

            private MOUSEEVENTF _prevMouseEvent = 0x0;
            private bool _findFishBoxTips;

            protected override BehaviourStatus Update(CaptureContent content)
            {
                _logger.LogDebug("Fishing");
                var fishBarMat = new Mat(content.CaptureRectArea.SrcMat, blackboard.fishBoxRect);
                var simulator = Simulation.SendInput;
                var rects = AutoFishingImageRecognition.GetFishBarRect(fishBarMat);
                if (rects != null && rects.Count > 0)
                {
                    if (rects.Count >= 2 && _prevMouseEvent == 0x0 && !_findFishBoxTips)
                    {
                        _findFishBoxTips = true;
                        _logger.LogInformation("  识别到钓鱼框，自动拉扯中...");
                    }

                    // 超过3个矩形是异常情况，取高度最高的三个矩形进行识别
                    if (rects.Count > 3)
                    {
                        rects.Sort((a, b) => b.Height.CompareTo(a.Height));
                        rects.RemoveRange(3, rects.Count - 3);
                    }

                    Rect _cur, _left, _right;
                    //Debug.WriteLine($"识别到{rects.Count} 个矩形");
                    if (rects.Count == 2)
                    {
                        if (rects[0].Width < rects[1].Width)
                        {
                            _cur = rects[0];
                            _left = rects[1];
                        }
                        else
                        {
                            _cur = rects[1];
                            _left = rects[0];
                        }

                        PutRects(content, _left, _cur, new Rect());

                        if (_cur.X < _left.X)
                        {
                            if (_prevMouseEvent != MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                            {
                                simulator.Mouse.LeftButtonDown();
                                //Simulator.PostMessage(TaskContext.Instance().GameHandle).LeftButtonDown();
                                _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN;
                                //Debug.WriteLine("进度不到 左键按下");
                            }
                        }
                        else
                        {
                            if (_prevMouseEvent == MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                            {
                                simulator.Mouse.LeftButtonUp();
                                //Simulator.PostMessage(TaskContext.Instance().GameHandle).LeftButtonUp();
                                _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                                //Debug.WriteLine("进度超出 左键松开");
                            }
                        }
                    }
                    else if (rects.Count == 3)
                    {
                        rects.Sort((a, b) => a.X.CompareTo(b.X));
                        _left = rects[0];
                        _cur = rects[1];
                        _right = rects[2];
                        PutRects(content, _left, _cur, _right);

                        if (_right.X + _right.Width - (_cur.X + _cur.Width) <= _cur.X - _left.X)
                        {
                            if (_prevMouseEvent == MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                            {
                                simulator.Mouse.LeftButtonUp();
                                //Simulator.PostMessage(TaskContext.Instance().GameHandle).LeftButtonUp();
                                _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                                //Debug.WriteLine("进入框内中间 左键松开");
                            }
                        }
                        else
                        {
                            if (_prevMouseEvent != MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                            {
                                simulator.Mouse.LeftButtonDown();
                                //Simulator.PostMessage(TaskContext.Instance().GameHandle).LeftButtonDown();
                                _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN;
                                //Debug.WriteLine("未到框内中间 左键按下");
                            }
                        }
                    }
                    else
                    {
                        PutRects(content, new Rect(), new Rect(), new Rect());
                    }
                }
                else
                {
                    PutRects(content, new Rect(), new Rect(), new Rect());
                    // 没有矩形视为已经完成钓鱼
                    VisionContext.Instance().DrawContent.RemoveRect("FishBox");
                    _findFishBoxTips = false;
                    _prevMouseEvent = 0x0;
                    _logger.LogInformation("  钓鱼结束");
                    _logger.LogInformation(@"└------------------------┘");

                    // 保证鼠标松开
                    simulator.Mouse.LeftButtonUp();

                    blackboard.Sleep(1000);

                    blackboard.MoveViewpointDown();
                    blackboard.Sleep(5000);

                    return BehaviourStatus.Succeeded;

                    //CheckFishingUserInterface(content);
                }

                return BehaviourStatus.Running;
            }

            private readonly Pen _pen = new(Color.Red, 1);
            private void PutRects(CaptureContent content, Rect left, Rect cur, Rect right)
            {
                //var list = new List<RectDrawable>
                //{
                //    left.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(_pen),
                //    cur.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(_pen),
                //    right.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(_pen)
                //};
                using var fishBoxRa = content.CaptureRectArea.Derive(blackboard.fishBoxRect);
                var list = new List<RectDrawable>
                {
                    fishBoxRa.ToRectDrawable(left, "left", _pen),
                    fishBoxRa.ToRectDrawable(cur, "cur", _pen),
                    fishBoxRa.ToRectDrawable(right, "right", _pen),
                }.Where(r => r.Rect.Height != 0).ToList();
                VisionContext.Instance().DrawContent.PutOrRemoveRectList("FishingBarAll", list);
            }
        }

        /// <summary>
        /// 检查是否在钓鱼界面
        /// </summary>
        /// <param name="content"></param>
        private BehaviourStatus CheckFishingUserInterface(CaptureContent content)
        {
            var prevIsExclusive = IsExclusive;
            IsExclusive = FindButtonForExclusive(content);
            if (IsExclusive)
            {
                if (IsEnabled && !prevIsExclusive)
                {
                    _logger.LogInformation("→ {Text}", "自动钓鱼，启动！");
                    _logger.LogInformation("当前自动选饵抛竿状态[{Enabled}]", TaskContext.Instance().Config.AutoFishingConfig.AutoThrowRodEnabled.ToChinese());
                }

                return BehaviourStatus.Running;
            }
            else
            {
                if (prevIsExclusive)
                {
                    _logger.LogInformation("← {Text}", "退出钓鱼界面");
                    //_fishBoxRect = Rect.Empty;
                    VisionContext.Instance().DrawContent.ClearAll();
                }

                return BehaviourStatus.Failed;
            }
        }

        ///// <summary>
        ///// 清理画布
        ///// </summary>
        //public void ClearDraw()
        //{
        //    VisionContext.Instance().DrawContent.PutOrRemoveRectList(new List<(string, RectDrawable)>
        //    {
        //        ("FishingBarLeft", new RectDrawable(System.Windows.Rect.Empty)),
        //        ("FishingBarCur", new RectDrawable(System.Windows.Rect.Empty)),
        //        ("FishingBarRight", new RectDrawable(System.Windows.Rect.Empty))
        //    });
        //    VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
        //    VisionContext.Instance().DrawContent.RemoveRect("StartFishingButton");
        //    WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RemoveButton", new object(), "开始自动钓鱼"));
        //}

        //public void Stop()
        //{
        //    ClearDraw();
        //}

    }
}
