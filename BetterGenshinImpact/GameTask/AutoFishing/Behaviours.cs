using BehaviourTree;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Compunet.YoloV8;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using static Vanara.PInvoke.User32;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using System.Linq;
using Fischless.WindowsInput;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    /// <summary>
    /// 检测鱼群
    /// </summary>
    public class GetFishpond : BaseBehaviour<ImageRegion>
    {
        private readonly ILogger logger;
        private readonly Blackboard blackboard;
        private readonly DrawContent drawContent;
        public GetFishpond(string name, Blackboard blackboard, ILogger logger, DrawContent? drawContent = null) : base(name, logger)
        {
            this.blackboard = blackboard;
            this.logger = logger;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
        }

        protected override void OnInitialize()
        {
            logger.LogInformation("开始寻找鱼塘");
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            using var memoryStream = new MemoryStream();
            imageRegion.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
            memoryStream.Seek(0, SeekOrigin.Begin);
            var result = blackboard.predictor.Detect(memoryStream);
            Debug.WriteLine($"YOLOv8识别: {result.Speed}");
            var fishpond = new Fishpond(result, ignoreObtained: true);
            if (fishpond.FishpondRect == Rect.Empty)
            {
                blackboard.Sleep(500);
                return BehaviourStatus.Running;
            }
            else
            {
                blackboard.fishpond = fishpond;
                logger.LogInformation("定位到鱼塘：" + string.Join('、', fishpond.Fishes.GroupBy(f => f.FishType).Select(g => $"{g.Key.ChineseName}{g.Count()}条")));
                int i = 0;
                foreach (var fish in fishpond.Fishes)
                {
                    imageRegion.Derive(fish.Rect).DrawSelf($"{fish.FishType.ChineseName}.{i++}");
                }
                blackboard.Sleep(1000);
                drawContent.ClearAll();

                return BehaviourStatus.Succeeded;
            }
        }
    }

    /// <summary>
    /// 选择鱼饵
    /// </summary>
    public class ChooseBait : BaseBehaviour<ImageRegion>
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly Blackboard blackboard;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? chooseBaitUIOpenWaitEndTime; // 等待选鱼饵界面出现并尝试找鱼饵的结束时间

        /// <summary>
        /// 选择鱼饵
        /// </summary>
        /// <param name="name"></param>
        /// <param name="autoFishingTrigger"></param>
        public ChooseBait(string name, Blackboard blackboard, ILogger logger, IInputSimulator input, TimeProvider? timeProvider = null) : base(name, logger)
        {
            this.blackboard = blackboard;
            this.logger = logger;
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

            blackboard.selectedBaitName = blackboard.fishpond.Fishes.GroupBy(f => f.FishType).OrderByDescending(g => g.Count()).First().First().FishType.BaitName; // 选择最多鱼吃的饵料
            logger.LogInformation("选择鱼饵 {Text}", BaitType.FromName(blackboard.selectedBaitName).ChineseName);

            // 寻找鱼饵
            var ro = new RecognitionObject
            {
                Name = "ChooseBait",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFishing", $"bait\\{blackboard.selectedBaitName}.png", 1920, 1080, 1d),  // todo 改成注入配置的形式，直接用魔法值不好
                Threshold = 0.8,
                Use3Channels = true,
                DrawOnWindow = false
            }.InitTemplate();

            var captureRegion = imageRegion;
            using var resRa = captureRegion.Find(ro);
            if (resRa.IsEmpty())
            {
                if (timeProvider.GetLocalNow() >= chooseBaitUIOpenWaitEndTime)
                {
                    logger.LogWarning("没有找到目标鱼饵");
                    input.Keyboard.KeyPress(VK.VK_ESCAPE);
                    blackboard.chooseBaitUIOpening = false;
                    logger.LogInformation("退出换饵界面");
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
                var ra = captureRegion.Find(new RecognitionObject
                {
                    Name = "BtnWhiteConfirm",
                    RecognitionType = RecognitionTypes.TemplateMatch,
                    TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_white_confirm.png", 1920, 1080, 1d),  // todo 改成注入配置的形式，直接用魔法值不好
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

    /// <summary>
    /// 抛竿
    /// </summary>
    public class ThrowRod : BaseBehaviour<ImageRegion>
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly Blackboard blackboard;
        private readonly DrawContent drawContent;

        private int noPlacementTimes; // 没有落点的次数
        private int noTargetFishTimes; // 没有目标鱼的次数
        public ThrowRod(string name, Blackboard blackboard, ILogger logger, IInputSimulator input, DrawContent? drawContent = null) : base(name, logger)
        {
            this.blackboard = blackboard;
            this.logger = logger;
            this.input = input;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
        }

        protected override void OnInitialize()
        {
            noPlacementTimes = 0;
            noTargetFishTimes = 0;

            input.Mouse.LeftButtonDown();
            blackboard.pitchReset = true;
            logger.LogInformation("长按预抛竿");
            blackboard.Sleep(3000);
            logger.LogInformation("开始抛竿");
        }

        protected override void OnTerminate(BehaviourStatus status)
        {
            if (status != BehaviourStatus.Running)
            {
                drawContent.RemoveRect("Target");
                drawContent.RemoveRect("Fish");
            }
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            blackboard.noTargetFish = false;
            var prevTargetFishRect = Rect.Empty; // 记录上一个目标鱼的位置

            var ra = imageRegion;

            // 找 鱼饵落点
            using var memoryStream = new MemoryStream();
            ra.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
            memoryStream.Seek(0, SeekOrigin.Begin);
            var result = blackboard.predictor.Detect(memoryStream);
            Debug.WriteLine($"YOLOv8识别: {result.Speed}");
            var fishpond = new Fishpond(result, includeTarget: true);
            Random _rd = new();
            if (fishpond.TargetRect == null || fishpond.TargetRect == Rect.Empty)
            {
                noPlacementTimes++;
                blackboard.Sleep(50);
                Debug.WriteLine($"{noPlacementTimes}次未找到鱼饵落点");

                var cX = ra.SrcBitmap.Width / 2;
                var cY = ra.SrcBitmap.Height / 2;
                var rdX = _rd.Next(0, ra.SrcBitmap.Width);
                var rdY = _rd.Next(0, ra.SrcBitmap.Height);

                var moveX = 100 * (cX - rdX) / ra.SrcBitmap.Width;
                var moveY = 100 * (cY - rdY) / ra.SrcBitmap.Height;

                input.Mouse.MoveMouseBy(moveX, moveY);

                if (noPlacementTimes > 25)
                {
                    logger.LogInformation("未找到鱼饵落点，重试");
                    input.Mouse.LeftButtonUp();
                    blackboard.Sleep(2000);
                    input.Mouse.LeftButtonClick();
                    blackboard.Sleep(2000);    //此处需要久一点
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
                //    inputEx.Mouse.MoveMouseBy(0, 100);
                //}

                if (noTargetFishTimes > 10)
                {
                    // 没有找到目标鱼，重新选择鱼饵
                    blackboard.selectedBaitName = string.Empty;
                    logger.LogInformation("没有找到目标鱼，1.直接抛竿");
                    input.Mouse.LeftButtonUp();
                    blackboard.Sleep(2000);
                    logger.LogInformation("没有找到目标鱼，2.收杆");
                    input.Mouse.LeftButtonClick();
                    blackboard.Sleep(800);

                    blackboard.noTargetFish = true;
                    return BehaviourStatus.Succeeded;
                }

                return BehaviourStatus.Running;
            }
            else
            {
                noTargetFishTimes = 0;
                imageRegion.DrawRect(fishpondTargetRect, "Target");
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
                    var cX = imageRegion.SrcBitmap.Width / 2;
                    var cY = imageRegion.SrcBitmap.Height / 2;
                    var rdX = _rd.Next(0, imageRegion.SrcBitmap.Width);
                    var rdY = _rd.Next(0, imageRegion.SrcBitmap.Height);

                    var moveX = 100 * (cX - rdX) / imageRegion.SrcBitmap.Width;
                    var moveY = 100 * (cY - rdY) / imageRegion.SrcBitmap.Height;

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
                    var dl = Math.Sqrt(dx * dx + dy * dy);
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
            }
            blackboard.Sleep(50);
            return BehaviourStatus.Running;
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
    /// 避免往红色靶点抛竿导致失败
    /// </summary>
    /// <param name="imageRegion"></param>
    public class CheckThrowRod : BaseBehaviour<ImageRegion>
    {
        private readonly ILogger logger;
        private DateTime? timeDelay;

        /// <summary>
        /// 检查抛竿结果
        /// </summary>
        /// <param name="name"></param>
        public CheckThrowRod(string name, ILogger logger) : base(name, logger)
        {
            this.logger = logger;
        }

        protected override void OnInitialize()
        {
            timeDelay = DateTime.Now.AddSeconds(3);
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            if (DateTime.Now < timeDelay)
            {
                return BehaviourStatus.Running;
            }

            Region baitRectArea = imageRegion.Find(AutoFishingAssets.Instance.BaitButtonRo);
            if (baitRectArea.IsEmpty())
            {
                return BehaviourStatus.Succeeded;
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
        private readonly Blackboard blackboard;
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private DateTime? waitFishBiteTimeout;
        private readonly int seconds;

        /// <summary>
        /// 如果未超时返回运行中，超时返回失败
        /// </summary>
        /// <param name="name"></param>
        /// <param name="seconds"></param>
        public FishBiteTimeout(string name, int seconds, Blackboard blackboard, ILogger logger, IInputSimulator input) : base(name, logger)
        {
            this.seconds = seconds;
            this.blackboard = blackboard;
            this.logger = logger;
            this.input = input;
        }
        protected override void OnInitialize()
        {
            waitFishBiteTimeout = DateTime.Now.AddSeconds(seconds);
        }
        protected override BehaviourStatus Update(ImageRegion context)
        {
            if (DateTime.Now >= waitFishBiteTimeout)
            {
                logger.LogInformation($"{seconds}秒没有咬杆，本次收杆");
                input.Mouse.LeftButtonClick();
                blackboard.Sleep(2000);
                return BehaviourStatus.Failed;
            }
            else
            {
                return BehaviourStatus.Running;
            }
        }
    }

    /// <summary>
    /// 自动提竿
    /// </summary>
    public class FishBite : BaseBehaviour<ImageRegion>
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly IOcrService ocrService = OcrFactory.Paddle;
        public FishBite(string name, ILogger logger, IInputSimulator input) : base(name, logger)
        {
            this.logger = logger;
            this.input = input;
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
            // 上半屏幕和中间1/3的区域
            var liftingWordsAreaRect = new Rect(imageRegion.SrcMat.Width / 3, 0, imageRegion.SrcMat.Width / 3,
                imageRegion.SrcMat.Height / 2);
            //VisionContext.Instance().DrawContent.PutRect("liftingWordsAreaRect", liftingWordsAreaRect.ToRectDrawable(new Pen(Color.Cyan, 2)));
            var wordCaptureMat = new Mat(imageRegion.SrcMat, liftingWordsAreaRect);
            var currentBiteWordsTips = AutoFishingImageRecognition.MatchFishBiteWords(wordCaptureMat, liftingWordsAreaRect);
            if (currentBiteWordsTips != Rect.Empty)
            {
                // VisionContext.Instance().DrawContent.PutRect("FishBiteTips",
                //     currentBiteWordsTips
                //         .ToWindowsRectangleOffset(liftingWordsAreaRect.X, liftingWordsAreaRect.Y)
                //         .ToRectDrawable());
                using var tipsRa = imageRegion.Derive(currentBiteWordsTips + liftingWordsAreaRect.Location);
                tipsRa.DrawSelf("FishBiteTips");

                // 图像提竿判断
                using var liftRodButtonRa = imageRegion.Find(AutoFishingAssets.Instance.LiftRodButtonRo);
                if (!liftRodButtonRa.IsEmpty())
                {
                    input.Mouse.LeftButtonClick();
                    logger.LogInformation(@"┌------------------------┐");
                    logger.LogInformation("  自动提竿(图像识别)");
                    VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
                    return BehaviourStatus.Succeeded;
                }

                // OCR 提竿判断
                var text = ocrService.Ocr(new Mat(imageRegion.SrcGreyMat,
                    new Rect(currentBiteWordsTips.X + liftingWordsAreaRect.X,
                        currentBiteWordsTips.Y + liftingWordsAreaRect.Y,
                        currentBiteWordsTips.Width, currentBiteWordsTips.Height)));
                if (!string.IsNullOrEmpty(text) && StringUtils.RemoveAllSpace(text).Contains("上钩"))
                {
                    input.Mouse.LeftButtonClick();
                    logger.LogInformation(@"┌------------------------┐");
                    logger.LogInformation("  自动提竿(OCR)");
                    VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
                    return BehaviourStatus.Succeeded;
                }

                input.Mouse.LeftButtonClick();
                logger.LogInformation(@"┌------------------------┐");
                logger.LogInformation("  自动提竿(文字块)");
                VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
                return BehaviourStatus.Succeeded;
            }

            return BehaviourStatus.Running;
        }
    }

    /// <summary>
    /// 进入钓鱼界面先尝试获取钓鱼框的位置
    /// </summary>
    public class GetFishBoxArea : BaseBehaviour<ImageRegion>
    {
        private readonly ILogger logger;
        private readonly Blackboard blackboard;
        public GetFishBoxArea(string name, Blackboard blackboard, ILogger logger) : base(name, logger)
        {
            this.blackboard = blackboard;
            this.logger = logger;
        }

        protected override void OnInitialize()
        {
            logger.LogInformation("钓鱼框识别开始");
        }

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            using var topMat = new Mat(imageRegion.SrcMat, new Rect(0, 0, imageRegion.Width, imageRegion.Height / 2));

            var rects = AutoFishingImageRecognition.GetFishBarRect(topMat);
            if (rects != null && rects.Count == 2)
            {
                Rect _cur, _left;
                if (Math.Abs(rects[0].Height - rects[1].Height) > 10)
                {
                    TaskControl.Logger.LogError("两个矩形高度差距过大，未识别到钓鱼框");
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
                    return BehaviourStatus.Running;
                }

                int hExtra = _cur.Height, vExtra = _cur.Height / 4;
                blackboard.fishBoxRect = new Rect(_cur.X - hExtra, _cur.Y - vExtra,
                     (_left.X + _left.Width / 2 - _cur.X) * 2 + hExtra * 2, _cur.Height + vExtra * 2);
                // VisionContext.Instance().DrawContent.PutRect("FishBox", _fishBoxRect.ToRectDrawable(new Pen(Color.LightPink, 2)));
                using var boxRa = imageRegion.Derive(blackboard.fishBoxRect);
                boxRa.DrawSelf("FishBox", new Pen(Color.LightPink, 2));
                return BehaviourStatus.Succeeded;
            }

            return BehaviourStatus.Running;

            //CheckFishingUserInterface(imageRegion);
        }
    }

    /// <summary>
    /// 拉条
    /// </summary>
    public class Fishing : BaseBehaviour<ImageRegion>
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly Blackboard blackboard;
        public Fishing(string name, Blackboard blackboard, ILogger logger, IInputSimulator input) : base(name, logger)
        {
            this.blackboard = blackboard;
            this.logger = logger;
            this.input = input;
        }

        protected override void OnInitialize()
        {
            logger.LogInformation("拉扯开始");
        }

        private MOUSEEVENTF _prevMouseEvent = 0x0;
        private bool _findFishBoxTips;

        protected override BehaviourStatus Update(ImageRegion imageRegion)
        {
            var fishBarMat = new Mat(imageRegion.SrcMat, blackboard.fishBoxRect);
            var rects = AutoFishingImageRecognition.GetFishBarRect(fishBarMat);
            if (rects != null && rects.Count > 0)
            {
                if (rects.Count >= 2 && _prevMouseEvent == 0x0 && !_findFishBoxTips)
                {
                    _findFishBoxTips = true;
                    logger.LogInformation("  识别到钓鱼框，自动拉扯中...");
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

                    PutRects(imageRegion, _left, _cur, new Rect());

                    if (_cur.X < _left.X)
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
                    rects.Sort((a, b) => a.X.CompareTo(b.X));
                    _left = rects[0];
                    _cur = rects[1];
                    _right = rects[2];
                    PutRects(imageRegion, _left, _cur, _right);

                    if (_right.X + _right.Width - (_cur.X + _cur.Width) <= _cur.X - _left.X)
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
                // 没有矩形视为已经完成钓鱼
                VisionContext.Instance().DrawContent.RemoveRect("FishBox");
                _findFishBoxTips = false;
                _prevMouseEvent = 0x0;
                logger.LogInformation("  拉扯结束");
                logger.LogInformation(@"└------------------------┘");

                // 保证鼠标松开
                input.Mouse.LeftButtonUp();

                blackboard.Sleep(2000);

                return BehaviourStatus.Succeeded;

                //CheckFishingUserInterface(imageRegion);
            }

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
            VisionContext.Instance().DrawContent.PutOrRemoveRectList("FishingBarAll", list);
        }
    }

    /// <summary>
    /// 如果视角被其他行为重置过，则调整视角至俯视
    /// </summary>
    public class MoveViewpointDown : BaseBehaviour<ImageRegion>
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly Blackboard blackboard;
        public MoveViewpointDown(string name, Blackboard blackboard, ILogger logger, IInputSimulator input) : base(name, logger)
        {
            this.blackboard = blackboard;
            this.logger = logger;
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
}
