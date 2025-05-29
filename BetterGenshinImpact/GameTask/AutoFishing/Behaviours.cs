using BehaviourTree;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using static Vanara.PInvoke.User32;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using System.Linq;
using Fischless.WindowsInput;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model;
using System.Globalization;
using Compunet.YoloSharp;
using Microsoft.Extensions.Localization;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    /// <summary>
    /// 检测鱼群
    /// </summary>
    public class GetFishpond : BaseBehaviour<ImageRegion>
    {
        private readonly Blackboard blackboard;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? detectInterval;
        private readonly DrawContent drawContent;
        public GetFishpond(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminat, TimeProvider? timeProvider = null, DrawContent? drawContent = null) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.blackboard = blackboard;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
        }

        protected override void OnInitialize()
        {
            logger.LogInformation("开始寻找鱼塘");
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            if (detectInterval != null && timeProvider.GetLocalNow() < detectInterval)
            {
                return BehaviourStatus.Running;
            }
            else
            {
                detectInterval = timeProvider.GetLocalNow().AddSeconds(0.5);
            }
            var result = blackboard.Predictor.Predictor.Detect(imageRegion.CacheImage);
            Debug.WriteLine($"YOLO识别: {result.Speed}");
            var fishpond = new Fishpond(result, ignoreObtained: true);
            if (fishpond.FishpondRect == default)
            {
                return BehaviourStatus.Running;
            }
            else
            {
                blackboard.fishpond = fishpond;

                string[] chooseBaitfailuresIgnoredBaits = blackboard.chooseBaitFailures.GroupBy(f => f).Where(g => g.Count() >= ChooseBait.MAX_FAILED_TIMES).Select(g => g.Key).ToArray();
                string[] throwRodNoTargetFishfailuresIgnoredBaits = blackboard.throwRodNoBaitFishFailures.GroupBy(f => f).Where(g => g.Count() >= ThrowRod.MAX_NO_BAIT_FISH_TIMES).Select(g => g.Key).ToArray();

                logger.LogInformation("定位到鱼塘：" + string.Join('、', fishpond.Fishes.GroupBy(f => f.FishType)
                    .Select(g => $"{g.Key.ChineseName}{g.Count()}条" + ((chooseBaitfailuresIgnoredBaits.Contains(g.Key.BaitName) || throwRodNoTargetFishfailuresIgnoredBaits.Contains(g.Key.BaitName)) ? "（忽略）" : ""))
                    ));
                int i = 0;
                foreach (var fish in fishpond.Fishes)
                {
                    imageRegion.Derive(fish.Rect).DrawSelf($"{fish.FishType.ChineseName}.{i++}");
                }
                blackboard.Sleep(1000);
                drawContent.ClearAll();
                if (blackboard.fishpond.Fishes.Any(f =>
                    !chooseBaitfailuresIgnoredBaits.Contains(f.FishType.BaitName)
                    && !throwRodNoTargetFishfailuresIgnoredBaits.Contains(f.FishType.BaitName)))
                {
                    return BehaviourStatus.Succeeded;
                }
                else
                {
                    return BehaviourStatus.Running;
                }
            }
        }
    }

    /// <summary>
    /// 选择鱼饵
    /// </summary>
    public class ChooseBait : BaseBehaviour<ImageRegion>
    {
        private readonly ISystemInfo systemInfo;
        private readonly IInputSimulator input;
        private readonly Blackboard blackboard;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? chooseBaitUIOpenWaitEndTime; // 等待选鱼饵界面出现并尝试找鱼饵的结束时间
        public const int MAX_FAILED_TIMES = 2;

        /// <summary>
        /// 选择鱼饵
        /// </summary>
        /// <param name="name"></param>
        /// <param name="autoFishingTrigger"></param>
        public ChooseBait(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminat, ISystemInfo systemInfo, IInputSimulator input, TimeProvider? timeProvider = null) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.blackboard = blackboard;
            this.systemInfo = systemInfo;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            if (this.Status == BehaviourStatus.Ready)
            {
                if (blackboard.fishpond.Fishes.Any(f => f.FishType.BaitName == blackboard.selectedBaitName))    // 如果该种鱼没钓完就不用换饵
                {
                    return BehaviourStatus.Succeeded;
                }
                chooseBaitUIOpenWaitEndTime = timeProvider.GetLocalNow().AddSeconds(3);
                logger.LogInformation("打开换饵界面");
                blackboard.chooseBaitUIOpening = true;
                input.Mouse.RightButtonClick();
                blackboard.Sleep(100);
                input.Mouse.MoveMouseBy(0, 200); // 鼠标移走，防止干扰
                blackboard.Sleep(500);
                return BehaviourStatus.Running;
            }

            blackboard.selectedBaitName = blackboard.fishpond.Fishes.GroupBy(f => f.FishType.BaitName)
                .Where(b => !blackboard.chooseBaitFailures.GroupBy(f => f).Where(g => g.Count() >= MAX_FAILED_TIMES).Any(g => g.Key == b.Key))  // 不能是已经失败两次的饵
                .OrderByDescending(g => g.Count()).First().Key; // 选择最多鱼吃的饵料
            logger.LogInformation("选择鱼饵 {Text}", BaitType.FromName(blackboard.selectedBaitName).ChineseName);

            // 寻找鱼饵
            var ro = new RecognitionObject
            {
                Name = "ChooseBait",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFishing", $"bait\\{blackboard.selectedBaitName}.png", systemInfo),
                Threshold = 0.8,
                Use3Channels = true,
                DrawOnWindow = false
            }.InitTemplate();

            using var resRa = imageRegion.Find(ro);
            if (resRa.IsEmpty())
            {
                if (timeProvider.GetLocalNow() >= chooseBaitUIOpenWaitEndTime)
                {
                    logger.LogWarning("没有找到目标鱼饵");
                    input.Keyboard.KeyPress(VK.VK_ESCAPE);
                    blackboard.chooseBaitUIOpening = false;
                    logger.LogInformation("退出换饵界面");

                    blackboard.chooseBaitFailures.Add(blackboard.selectedBaitName);
                    if (blackboard.chooseBaitFailures.Count(f => f == blackboard.selectedBaitName) >= MAX_FAILED_TIMES)
                    {
                        logger.LogWarning($"本次将忽略{BaitType.FromName(blackboard.selectedBaitName).ChineseName}");
                    }

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
                imageRegion.ClickTo((int)(imageRegion.Width * 0.675), (int)(imageRegion.Height / 3d));
                blackboard.Sleep(200);
                // 点击确定
                using var ra = imageRegion.Find(new RecognitionObject
                {
                    Name = "BtnWhiteConfirm",
                    RecognitionType = RecognitionTypes.TemplateMatch,
                    TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_white_confirm.png", systemInfo),
                    Use3Channels = true
                }.InitTemplate());
                if (ra.IsExist())
                {
                    ra.Click();
                }
                blackboard.chooseBaitUIOpening = false;
                logger.LogInformation("退出换饵界面");
                blackboard.Sleep(500); // 等待界面切换
            }

            return BehaviourStatus.Succeeded;
        }
    }

    [Obsolete]
    /// <summary>
    /// 《How to Cast a Fly Rod: Step-by-Step Guide for Beginners》：https://hookedonfly.fishing/2024/10/how-to-cast-a-fly-rod/
    /// 《How to Catch Fish》：https://game8.co/games/Genshin-Impact/archives/340798
    /// 《Tutorial/Fishing》：https://genshin-impact.fandom.com/wiki/Tutorial/Fishing
    /// </summary>
    public class LiftAndHold : BaseBehaviour<ImageRegion>
    {
        private readonly Blackboard blackboard;
        private readonly IInputSimulator input;
        public LiftAndHold(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminate, IInputSimulator input) : base(name, logger, saveScreenshotOnTerminate)
        {
            this.blackboard = blackboard;
            this.input = input;
        }

        protected override void OnInitialize()
        {
            input.Mouse.LeftButtonDown();
            blackboard.pitchReset = true;
            logger.LogInformation("长按举起鱼竿");
        }

        protected override BehaviourStatus Update(ImageRegion context)
        {
            // todo 这个方案不能令人满意，应该是底层做一个事件监听来记录被点击，底层向上暴露一个和Timer用起来差不多的东西，它应该有个开始记录方法、有个获取从开始到目前是否被点击的方法
            // 但说到底，检查是否鼠标被干扰，不是一个必选的方法。做一个精确度高的图形检测方案，来检测当前位于哪个步骤，会更好。
            if (!Simulation.IsKeyDown(VK.VK_LBUTTON))
            {
                logger.LogWarning("检测到当前鼠标左键状态不符合要求，可能受到干扰，退出任务");
                blackboard.abort = true;
                return BehaviourStatus.Failed;
            }
            return BehaviourStatus.Running;
        }
    }

    /// <summary>
    /// 抛竿
    /// </summary>
    public class ThrowRod : BaseBehaviour<ImageRegion>
    {
        private readonly IInputSimulator input;
        private readonly Blackboard blackboard;
        private readonly DrawContent drawContent;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? ignoreObtainedEndTime;
        public const int MAX_NO_BAIT_FISH_TIMES = 2;
        private DateTimeOffset? findTargetEndTime;
        private bool foundTarget;

        private int noPlacementTimes; // 没有落点的次数
        private int noTargetFishTimes; // 没有目标鱼的次数
        public ThrowRod(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminat, IInputSimulator input, TimeProvider? timeProvider = null, DrawContent? drawContent = null) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.blackboard = blackboard;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
        }

        protected override void OnInitialize()
        {
            noPlacementTimes = 0;
            noTargetFishTimes = 0;
            blackboard.throwRodNoBaitFish = false;
            ignoreObtainedEndTime = timeProvider.GetLocalNow().AddSeconds(6);
            blackboard.throwRodNoTarget = false;
            findTargetEndTime = timeProvider.GetLocalNow().AddSeconds(5);
            foundTarget = false;
            mouseMoveI *= -1;
            mouseMoveR = 0d;

            input.Mouse.LeftButtonDown();
            blackboard.pitchReset = true;
            logger.LogInformation("长按举起鱼竿");
        }

        protected override void OnTerminate(BehaviourStatus status)
        {
            drawContent.RemoveRect("Target");
            drawContent.RemoveRect("Fish");
        }

        /// <summary>
        /// 当前鱼
        /// </summary>
        public OneFish? currentFish { get; private set; }

        private int mouseMoveI = 1; // 上下移动视角的初始方向控制参数
        private double mouseMoveR; // 上下移动视角的切换频率控制参数

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            // 找 鱼饵落点
            var result = blackboard.Predictor.Predictor.Detect(imageRegion.CacheImage);
            Debug.WriteLine($"YOLOv8识别: {result.Speed}");
            var fishpond = new Fishpond(result, includeTarget: timeProvider.GetLocalNow() <= ignoreObtainedEndTime);
            blackboard.fishpond = fishpond;
            Random _rd = new();
            if (fishpond.TargetRect == null || fishpond.TargetRect == default)
            {
                if (!foundTarget)
                {
                    if (timeProvider.GetLocalNow() <= findTargetEndTime)
                    {
                        // 上下移动视角方便看落点
                        mouseMoveR += Math.PI / 16d;
                        input.Mouse.MoveMouseBy(0, mouseMoveI * 80 * Math.Sign(Math.Cos(mouseMoveR)));
                        blackboard.Sleep(100);
                        return BehaviourStatus.Running;
                    }
                    else
                    {
                        logger.LogInformation("举起鱼竿失败，始终没有找到落点");
                        input.Mouse.LeftButtonUp();
                        blackboard.Sleep(2000);
                        input.Mouse.LeftButtonClick();
                        blackboard.Sleep(800);

                        blackboard.throwRodNoTarget = true;
                        blackboard.throwRodNoTargetTimes++;
                        if (blackboard.throwRodNoTargetTimes > 2)
                        {
                            logger.LogWarning("没有找到落点次数过多，目前位置可能视野不佳，退出");
                            blackboard.abort = true;
                        }

                        return BehaviourStatus.Failed;
                    }
                }

                noPlacementTimes++;
                blackboard.Sleep(50);
                Debug.WriteLine($"{noPlacementTimes}次未找到鱼饵落点");

                var cX = imageRegion.CacheImage.Width / 2;
                var cY = imageRegion.CacheImage.Height / 2;
                var rdX = _rd.Next(0, imageRegion.CacheImage.Width);
                var rdY = _rd.Next(0, imageRegion.CacheImage.Height);

                var moveX = 100 * (cX - rdX) / imageRegion.CacheImage.Width;
                var moveY = 100 * (cY - rdY) / imageRegion.CacheImage.Height;

                input.Mouse.MoveMouseBy(moveX, moveY);

                if (noPlacementTimes > 25)
                {
                    logger.LogInformation("中途丢失鱼饵落点，重试");
                    input.Mouse.LeftButtonUp();
                    blackboard.Sleep(2000);
                    input.Mouse.LeftButtonClick();
                    blackboard.Sleep(2000);    //此处需要久一点
                    return BehaviourStatus.Failed;
                }

                return BehaviourStatus.Running;
            }
            else
            {
                foundTarget = true;
            }

            Rect fishpondTargetRect = (Rect)fishpond.TargetRect;

            // 找到落点最近的鱼
            currentFish = null;
            string[] ignoredBaits = blackboard.throwRodNoBaitFishFailures.GroupBy(f => f).Where(g => g.Count() >= MAX_NO_BAIT_FISH_TIMES).Select(g => g.Key).ToArray();
            var list = fishpond.Fishes
                .Where(f => !ignoredBaits.Contains(f.FishType.BaitName))   // 不能是已经失败两次的饵;
                .Where(f => f.FishType.BaitName == blackboard.selectedBaitName).OrderByDescending(f => f.Confidence)
                .ToList();
            if (list.Count > 0)
            {
                currentFish = list.OrderBy(f => f.Rect.GetCenterPoint().DistanceTo(fishpond.TargetRect.Value.GetCenterPoint())).ThenByDescending(fish => fish.Confidence).First();
            }

            if (currentFish == null)
            {
                Debug.WriteLine("无鱼饵适用鱼");
                noTargetFishTimes++;

                if (noTargetFishTimes > 10)
                {
                    // 没有找到鱼饵适用鱼，重新选择鱼饵
                    blackboard.throwRodNoBaitFish = true;
                    blackboard.throwRodNoBaitFishFailures.Add(blackboard.selectedBaitName);
                    if (blackboard.throwRodNoBaitFishFailures.Count(f => f == blackboard.selectedBaitName) >= MAX_NO_BAIT_FISH_TIMES)
                    {
                        logger.LogWarning($"本次将忽略{BaitType.FromName(blackboard.selectedBaitName).ChineseName}");
                    }

                    blackboard.selectedBaitName = string.Empty;
                    logger.LogInformation("没有找到鱼饵适用鱼");
                    input.Mouse.LeftButtonUp();
                    blackboard.Sleep(2000);
                    input.Mouse.LeftButtonClick();
                    blackboard.Sleep(800);

                    return BehaviourStatus.Succeeded;
                }

                return BehaviourStatus.Running;
            }
            else
            {
                noTargetFishTimes = 0;
                imageRegion.DrawRect(fishpondTargetRect, "Target", new Pen(Color.White));
                imageRegion.Derive(currentFish.Rect).DrawSelf("Fish");

                // drawContent.PutRect("Target", fishpond.TargetRect.ToRectDrawable());
                // drawContent.PutRect("Fish", currentFish.Rect.ToRectDrawable());

                // 来自 HutaoFisher 的抛竿技术
                var rod = fishpondTargetRect;
                var fish = currentFish.Rect;
                if (ScaleMax1080PCaptureRect == default)  // todo 等配置能注入后和SystemInfo.ScaleMax1080PCaptureRect放到一起
                {
                    if (imageRegion.Width > 1920)
                    {
                        var scale = imageRegion.Width / 1920d;
                        ScaleMax1080PCaptureRect = new Rect(imageRegion.X, imageRegion.Y, 1920, (int)(imageRegion.Height / scale));
                    }
                    else
                    {
                        ScaleMax1080PCaptureRect = new Rect(imageRegion.X, imageRegion.Y, imageRegion.Width, imageRegion.Height);
                    }
                }
                var dx = NormalizeXTo1024(fish.Left + fish.Right - rod.Left - rod.Right) / 2.0;
                var dy = NormalizeYTo576(fish.Top + fish.Bottom - rod.Top - rod.Bottom) / 2.0;
                var dl = Math.Sqrt(dx * dx + dy * dy);
                //logger.LogInformation("dl = {dl}", dl);
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

                // 如果hutao钓鱼暂时没有更新导致报错，可以先用这段凑合
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
                    var cX = imageRegion.CacheImage.Width / 2;
                    var cY = imageRegion.CacheImage.Height / 2;
                    var rdX = _rd.Next(0, imageRegion.CacheImage.Width);
                    var rdY = _rd.Next(0, imageRegion.CacheImage.Height);

                    var moveX = 100 * (cX - rdX) / imageRegion.CacheImage.Width;
                    var moveY = 100 * (cY - rdY) / imageRegion.CacheImage.Height;

                    logger.LogInformation("失败 随机移动 {DX}, {DY}", moveX, moveY);
                    input.Mouse.MoveMouseBy(moveX, moveY);
                }
                else if (state == 0)
                {
                    // 成功 抛竿
                    input.Mouse.LeftButtonUp();
                    logger.LogInformation("尝试钓取 {Text}", currentFish.FishType.ChineseName);
                    return BehaviourStatus.Succeeded;
                }
                else if (state == 1)
                {
                    // 太近
                    // set a minimum step
                    dx = dx / dl * 30;
                    dy = dy / dl * 30;
                    // _logger.LogInformation("太近 移动 {DX}, {DY}", dx, dy);
                    input.Mouse.MoveMouseBy((int)(-dx / 1.5), (int)(-dy * 1.5));
                }
                else if (state == 2)
                {
                    // 太远
                    // _logger.LogInformation("太远 移动 {DX}, {DY}", dx, dy);
                    input.Mouse.MoveMouseBy((int)(dx / 1.5), (int)(dy * 1.5));
                }
                blackboard.Sleep((int)dl);
                return BehaviourStatus.Running;
            }
        }

        private Rect ScaleMax1080PCaptureRect { get; set; }

        private double NormalizeXTo1024(int x)
        {
            return x * 1.0 / ScaleMax1080PCaptureRect.Width * 1024;
        }

        private double NormalizeYTo576(int y)
        {
            return y * 1.0 / ScaleMax1080PCaptureRect.Height * 576;
        }

    }


    /// <summary>
    /// 检查抛竿结果
    /// </summary>
    /// <param name="imageRegion"></param>
    public class CheckThrowRod : BaseBehaviour<ImageRegion>
    {
        private readonly Blackboard blackboard;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? timeDelay;
        private bool hasChecked;

        /// <summary>
        /// 检查抛竿结果
        /// 如果仍发现选饵按钮则失败
        /// </summary>
        public CheckThrowRod(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminat, TimeProvider? timeProvider = null) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.blackboard = blackboard;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void OnInitialize()
        {
            timeDelay = timeProvider.GetLocalNow().AddSeconds(3);
            hasChecked = false;
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            if (timeProvider.GetLocalNow() < timeDelay || hasChecked)
            {
                return BehaviourStatus.Running;
            }

            using Region btnRectArea = imageRegion.Find(blackboard.AutoFishingAssets.BaitButtonRo);
            if (btnRectArea.IsEmpty())
            {
                hasChecked = true;
                return BehaviourStatus.Running;
            }
            else
            {
                logger.LogInformation("抛竿失败");
                return BehaviourStatus.Failed;
            }
        }
    }

    public class FishBiteTimeout : BaseBehaviour<ImageRegion>
    {
        private readonly IInputSimulator input;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? waitFishBiteTimeout;
        private readonly int seconds;
        public bool leftButtonClicked;

        /// <summary>
        /// 如果未超时返回运行中，超时返回失败并按左键提竿
        /// </summary>
        /// <param name="name"></param>
        /// <param name="seconds"></param>
        public FishBiteTimeout(string name, int seconds, ILogger logger, bool saveScreenshotOnTerminat, IInputSimulator input, TimeProvider? timeProvider = null) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.seconds = seconds;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }
        protected override void OnInitialize()
        {
            waitFishBiteTimeout = timeProvider.GetLocalNow().AddSeconds(seconds);
            leftButtonClicked = false;
        }
        protected override BehaviourStatus Update(ImageRegion context)
        {
            if (timeProvider.GetLocalNow() >= waitFishBiteTimeout)
            {
                if (leftButtonClicked)
                {
                    logger.LogInformation($"收杆成功");

                    return BehaviourStatus.Failed;
                }
                else
                {
                    logger.LogInformation($"{seconds}秒没有咬杆，本次收杆");
                    leftButtonClicked = true;
                    input.Mouse.LeftButtonClick();
                    waitFishBiteTimeout = timeProvider.GetLocalNow().AddSeconds(2);
                    return BehaviourStatus.Running;
                }
            }
            else
            {
                return BehaviourStatus.Running;
            }
        }
    }

    /// <summary>
    /// 检查提竿结果
    /// </summary>
    public class CheckRaiseHook : BaseBehaviour<ImageRegion>
    {
        private readonly Blackboard blackboard;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? timeDelay;
        private bool hasChecked;

        /// <summary>
        /// 检查提竿结果
        /// 如果仍发现提竿按钮则失败
        /// </summary>
        /// <param name="name"></param>
        public CheckRaiseHook(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminat, TimeProvider? timeProvider = null) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.blackboard = blackboard;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void OnInitialize()
        {
            timeDelay = timeProvider.GetLocalNow().AddSeconds(3);
            hasChecked = false;
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            if (timeProvider.GetLocalNow() < timeDelay || hasChecked)
            {
                return BehaviourStatus.Running;
            }

            using Region btnRectArea = imageRegion.Find(blackboard.AutoFishingAssets.WaitBiteButtonRo);
            if (btnRectArea.IsEmpty())
            {
                hasChecked = true;
                return BehaviourStatus.Running;
            }
            else
            {
                logger.LogInformation("提竿失败");
                return BehaviourStatus.Failed;
            }
        }
    }

    /// <summary>
    /// 自动提竿
    /// </summary>
    public class FishBite : BaseBehaviour<ImageRegion>
    {
        private readonly Blackboard blackboard;
        private readonly IInputSimulator input;
        private readonly DrawContent drawContent;
        private readonly IOcrService ocrService;
        private readonly string getABiteLocalizedString;
        public FishBite(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminat, IInputSimulator input, IOcrService ocrService, DrawContent? drawContent = null, CultureInfo? cultureInfo = null, IStringLocalizer<AutoFishingImageRecognition>? stringLocalizer = null) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.blackboard = blackboard;
            this.input = input;
            this.ocrService = ocrService;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
            this.getABiteLocalizedString = stringLocalizer == null ? "上钩" : stringLocalizer.WithCultureGet(cultureInfo, "上钩");
        }

        protected override void OnInitialize()
        {
            logger.LogInformation("提竿识别开始");
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            // 自动识别的钓鱼框向下延伸到屏幕中间
            //var liftingWordsAreaRect = new Rect(fishBoxRect.X, fishBoxRect.Y + fishBoxRect.Height * 2,
            //    fishBoxRect.Width, imageRegion.CaptureRectArea.SrcMat.Height / 2 - fishBoxRect.Y - fishBoxRect.Height * 5);
            // 上半屏幕和中间1/2的区域
            var liftingWordsAreaRect = new Rect(imageRegion.SrcMat.Width / 3, 0, imageRegion.SrcMat.Width / 3,
                imageRegion.SrcMat.Height / 2);
            //VisionContext.Instance().DrawContent.PutRect("liftingWordsAreaRect", liftingWordsAreaRect.ToRectDrawable(new Pen(Color.Cyan, 2)));
            using var wordCaptureMat = new Mat(imageRegion.SrcMat, liftingWordsAreaRect);
            var currentBiteWordsTips = AutoFishingImageRecognition.MatchFishBiteWords(wordCaptureMat, liftingWordsAreaRect);
            if (currentBiteWordsTips != default)
            {
                // VisionContext.Instance().DrawContent.PutRect("FishBiteTips",
                //     currentBiteWordsTips
                //         .ToWindowsRectangleOffset(liftingWordsAreaRect.X, liftingWordsAreaRect.Y)
                //         .ToRectDrawable());
                using var tipsRa = imageRegion.Derive(currentBiteWordsTips + liftingWordsAreaRect.Location);
                tipsRa.DrawSelf("FishBiteTips");

                return RaiseRod("文字块");
            }

            // 图像提竿判断
            using var liftRodButtonRa = imageRegion.Find(blackboard.AutoFishingAssets.LiftRodButtonRo);
            if (!liftRodButtonRa.IsEmpty())
            {
                return RaiseRod("图像识别");
            }

            // OCR 提竿判断
            var text = ocrService.Ocr(wordCaptureMat);

            if (!string.IsNullOrEmpty(text) && StringUtils.RemoveAllSpace(text).Contains(this.getABiteLocalizedString))
            {
                return RaiseRod("OCR");
            }

            return BehaviourStatus.Running;
        }

        private BehaviourStatus RaiseRod(string method)
        {
            input.Mouse.LeftButtonClick();
            logger.LogInformation(@"┌------------------------┐");
            logger.LogInformation("  自动提竿({m})", method);
            drawContent.RemoveRect("FishBiteTips");
            return BehaviourStatus.Succeeded;
        }
    }

    /// <summary>
    /// 进入钓鱼界面先尝试获取钓鱼框的位置
    /// </summary>
    public class GetFishBoxArea : BaseBehaviour<ImageRegion>
    {
        private readonly Blackboard blackboard;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? waitFishBoxAppearEndTime;
        public GetFishBoxArea(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminat, TimeProvider? timeProvider = null) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.blackboard = blackboard;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void OnInitialize()
        {
            logger.LogInformation("钓鱼框识别开始");
            waitFishBoxAppearEndTime = timeProvider.GetLocalNow().AddSeconds(5);
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            if (timeProvider.GetLocalNow() > waitFishBoxAppearEndTime)
            {
                logger.LogInformation("钓鱼框识别失败");
                return BehaviourStatus.Failed;
            }

            using var topMat = new Mat(imageRegion.SrcMat, new Rect(0, 0, imageRegion.Width, imageRegion.Height / 2));

            var rects = AutoFishingImageRecognition.GetFishBarRect(topMat);
            if (rects != null && rects.Count == 2)
            {
                Rect _cur, _right;
                if (Math.Abs(rects[0].Height - rects[1].Height) > 10)
                {
                    if (saveScreenshotOnTerminate)
                    {
                        SaveScreenshot(imageRegion, $"{DateTime.Now:yyyyMMddHHmmssfff}_{this.GetType().Name}_Error.png");
                    }
                    logger.LogError("两个矩形高度差距过大，未识别到钓鱼框");
                    return BehaviourStatus.Running;
                }

                if (rects[0].Width < rects[1].Width)
                {
                    _cur = rects[0];
                    _right = rects[1];
                }
                else
                {
                    _cur = rects[1];
                    _right = rects[0];
                }

                if (_right.X < _cur.X // cur 是游标位置, 在初始状态下，cur 一定在right左边
                    || _cur.Width > _right.Width // right一定比cur宽
                    || _cur.X + _cur.Width > topMat.Width / 2 // cur 一定在屏幕左侧
                    || _cur.X + _cur.Width > _right.X - _right.Width / 2 // cur 一定在right左侧+right的一半宽度
                    || _cur.X + _cur.Width > topMat.Width / 2 - _right.Width // cur 一定在屏幕中轴线减去整个right的宽度的位置左侧
                   )
                {
                    return BehaviourStatus.Running;
                }

                int hExtra = _cur.Height, vExtra = _cur.Height / 4;
                blackboard.fishBoxRect = new Rect(_cur.X - hExtra, _cur.Y - vExtra,
                     (topMat.Width / 2 - _cur.X) * 2 + hExtra * 2, _cur.Height + vExtra * 2);
                // VisionContext.Instance().DrawContent.PutRect("FishBox", _fishBoxRect.ToRectDrawable(new Pen(Color.LightPink, 2)));
                using var boxRa = imageRegion.Derive(blackboard.fishBoxRect);
                boxRa.DrawSelf("FishBox", new Pen(Color.LightPink, 2));
                logger.LogInformation("  识别到钓鱼框");
                return BehaviourStatus.Succeeded;
            }

            return BehaviourStatus.Running;
        }
    }

    /// <summary>
    /// 拉条
    /// </summary>
    public class Fishing : BaseBehaviour<ImageRegion>
    {
        private readonly IInputSimulator input;
        private readonly Blackboard blackboard;
        private readonly TimeProvider timeProvider;
        private readonly DrawContent drawContent;
        private DateTimeOffset? noDetectionDuringTime;
        public Fishing(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminate, IInputSimulator input, TimeProvider? timeProvider = null, DrawContent? drawContent = null) : base(name, logger, saveScreenshotOnTerminate)
        {
            this.blackboard = blackboard;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
        }

        protected override void OnInitialize()
        {
            logger.LogInformation("拉扯开始");
        }

        private MOUSEEVENTF _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            using var fishBarMat = new Mat(imageRegion.SrcMat, blackboard.fishBoxRect);
            var rects = AutoFishingImageRecognition.GetFishBarRect(fishBarMat);
            if (rects != null && rects.Count > 0)
            {
                // 超过3个矩形是异常情况，取高度最高的三个矩形进行识别
                if (rects.Count > 3)
                {
                    if (saveScreenshotOnTerminate)
                    {
                        SaveScreenshot(imageRegion, $"{DateTime.Now:yyyyMMddHHmmssfff}_{this.GetType().Name}_Error.png");
                    }
                    logger.LogError("识别到超过3个矩形，取前三");
                    rects.Sort((a, b) => b.Height.CompareTo(a.Height));
                    rects.RemoveRange(3, rects.Count - 3);
                }

                //Debug.WriteLine($"识别到{rects.Count} 个矩形");
                if (rects.Count == 2)
                {
                    // 游标矩形不在区间内或恰在区间两端时只会检测到两个矩形
                    Rect _cursor, _target;
                    if (rects[0].Width < rects[1].Width)
                    {
                        _cursor = rects[0];
                        _target = rects[1];
                    }
                    else
                    {
                        _cursor = rects[1];
                        _target = rects[0];
                    }
                    if (_target.Width < _cursor.Width * 10) // 异常：当目标矩形明显不够长时视为无效检测，不作为
                    {
                        return BehaviourStatus.Running;
                    }

                    PutRects(imageRegion, _target, _cursor, new Rect());

                    if (_cursor.X < _target.X)
                    {
                        if (_prevMouseEvent != MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            input.Mouse.LeftButtonDown();
                            //input.PostMessage(TaskContext.Instance().GameHandle).LeftButtonDown();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN;
                            //Debug.WriteLine("进度不到 左键按下");
                        }
                    }
                    else
                    {
                        if (_prevMouseEvent == MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            input.Mouse.LeftButtonUp();
                            //input.PostMessage(TaskContext.Instance().GameHandle).LeftButtonUp();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                            //Debug.WriteLine("进度超出 左键松开");
                        }
                    }
                }
                else if (rects.Count == 3)
                {
                    // 游标矩形在区间内会检测到三个矩形，即目标区间被游标分割成左半和右半
                    Rect _cursor, _left, _right;
                    rects.Sort((a, b) => a.X.CompareTo(b.X));
                    _left = rects[0];
                    _cursor = rects[1];
                    _right = rects[2];
                    PutRects(imageRegion, _left, _cursor, _right);

                    if (_right.X + _right.Width - (_cursor.X + _cursor.Width) <= _cursor.X - _left.X)
                    {
                        if (_prevMouseEvent == MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            input.Mouse.LeftButtonUp();
                            //input.PostMessage(TaskContext.Instance().GameHandle).LeftButtonUp();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                            //Debug.WriteLine("进入框内中间 左键松开");
                        }
                    }
                    else
                    {
                        if (_prevMouseEvent != MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            input.Mouse.LeftButtonDown();
                            //input.PostMessage(TaskContext.Instance().GameHandle).LeftButtonDown();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN;
                            //Debug.WriteLine("未到框内中间 左键按下");
                        }
                    }
                }
                else
                {
                    PutRects(imageRegion, new Rect(), new Rect(), new Rect());
                }
            }
            else
            {
                PutRects(imageRegion, new Rect(), new Rect(), new Rect());

                if (noDetectionDuringTime == null)
                {
                    noDetectionDuringTime = timeProvider.GetLocalNow().AddSeconds(1);
                    return BehaviourStatus.Running;
                }
                else if (timeProvider.GetLocalNow() < noDetectionDuringTime)
                {
                    return BehaviourStatus.Running;
                }

                // 没有矩形视为已经完成钓鱼
                drawContent.RemoveRect("FishBox");
                _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                logger.LogInformation("  拉扯结束");
                logger.LogInformation(@"└------------------------┘");

                // 保证鼠标松开
                input.Mouse.LeftButtonUp();


                return BehaviourStatus.Succeeded;
            }


            noDetectionDuringTime = null;
            return BehaviourStatus.Running;
        }

        private readonly Pen _pen = new(Color.Red, 1);
        private void PutRects(ImageRegion imageRegion, Rect left, Rect cur, Rect right)
        {
            //var list = new List<RectDrawable>
            //{
            //    left.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(_pen),
            //    cur.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(_pen),
            //    right.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(_pen)
            //};
            using var fishBoxRa = imageRegion.Derive(blackboard.fishBoxRect);
            var list = new List<RectDrawable>
                {
                    fishBoxRa.ToRectDrawable(left, "left", _pen),
                    fishBoxRa.ToRectDrawable(cur, "cur", _pen),
                    fishBoxRa.ToRectDrawable(right, "right", _pen),
                }.Where(r => r.Rect.Height != 0).ToList();
            drawContent.PutOrRemoveRectList("FishingBarAll", list);
        }
    }

    /// <summary>
    /// 如果视角被其他行为重置过，则调整视角至俯视
    /// </summary>
    public class MoveViewpointDown : BaseBehaviour<ImageRegion>
    {
        private readonly IInputSimulator input;
        private readonly Blackboard blackboard;
        public MoveViewpointDown(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminat, IInputSimulator input) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.blackboard = blackboard;
            this.input = input;
        }

        protected override BehaviourStatus Update(ImageRegion context)
        {
            if (blackboard.pitchReset)
            {
                logger.LogInformation("调整视角至俯视");
                blackboard.pitchReset = false;
                // 下移视角方便看鱼
                input.Mouse.MoveMouseBy(0, 500);
                blackboard.Sleep(100);
                return BehaviourStatus.Running;
            }
            return BehaviourStatus.Succeeded;
        }

    }

    /// <summary>
    /// 检查开始钓一条鱼的初始状态
    /// </summary>
    /// <param name="imageRegion"></param>
    public class CheckInitalState : BaseBehaviour<ImageRegion>
    {
        private readonly Blackboard blackboard;
        private readonly IInputSimulator input;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? moveMouseInterval;

        /// <summary>
        /// 检查开始钓一条鱼的初始状态
        /// 必须能看到换饵按钮，直到看到才能成功
        /// 由于模板匹配召回率低，会转动视角
        /// </summary>
        public CheckInitalState(string name, Blackboard blackboard, ILogger logger, bool saveScreenshotOnTerminat, IInputSimulator input, TimeProvider? timeProvider = null) : base(name, logger, saveScreenshotOnTerminat)
        {
            this.blackboard = blackboard;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void OnInitialize()
        {
            logger.LogInformation("开始寻找换饵图标");
            theta = 0d;
        }

        private double theta;

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            using Region btnRectArea = imageRegion.Find(blackboard.AutoFishingAssets.BaitButtonRo);
            if (btnRectArea.IsEmpty())
            {
                if (moveMouseInterval == null || timeProvider.GetLocalNow() > moveMouseInterval)
                {
                    theta += Math.PI / 10;
                    double rho = 10 + 2 * theta;
                    double x = rho * Math.Cos(theta);
                    double y = rho * Math.Sin(theta);

                    input.Mouse.MoveMouseBy((int)x, (int)y);
                    moveMouseInterval = timeProvider.GetLocalNow().AddSeconds(0.1);
                }
                return BehaviourStatus.Running;
            }
            else
            {
                logger.LogInformation("找到换饵图标");
                return BehaviourStatus.Succeeded;
            }
        }
    }
}
