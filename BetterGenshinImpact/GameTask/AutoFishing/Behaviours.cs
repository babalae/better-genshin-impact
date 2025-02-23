using BehaviourTree;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
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

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    /// <summary>
    /// 检测鱼群
    /// </summary>
    public class GetFishpond : BaseBehaviour<CaptureContent>
    {
        private readonly ILogger<GetFishpond> _logger = App.GetLogger<GetFishpond>();
        private readonly Blackboard blackboard;
        public GetFishpond(string name, Blackboard blackboard) : base(name)
        {
            this.blackboard = blackboard;
        }

        protected override BehaviourStatus Update(CaptureContent content)
        {
            _logger.LogDebug("GetFishpond");
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
                blackboard.fishpond = fishpond;
                _logger.LogInformation("定位到鱼塘：" + string.Join('、', fishpond.Fishes.GroupBy(f => f.FishType).Select(g => $"{g.Key.ChineseName}{g.Count()}条")));

                return BehaviourStatus.Succeeded;
            }
        }
    }

    /// <summary>
    /// 选择鱼饵
    /// </summary>
    public class ChooseBait : BaseBehaviour<CaptureContent>
    {
        private readonly ILogger<ChooseBait> _logger = App.GetLogger<ChooseBait>();
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
                blackboard.chooseBaitUIOpening = true;
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
                    _logger.LogWarning("没有找到目标鱼饵");
                    Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
                    blackboard.chooseBaitUIOpening = false;
                    _logger.LogInformation("退出换饵界面");
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
                blackboard.chooseBaitUIOpening = false;
                _logger.LogInformation("退出换饵界面");
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
        private readonly ILogger<ApproachFishAndThrowRod> _logger = App.GetLogger<ApproachFishAndThrowRod>();
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
            blackboard.pitchReset = true;
            _logger.LogInformation("长按预抛竿");
            blackboard.Sleep(3000);
        }

        protected override void OnTerminate(BehaviourStatus status)
        {
            if (status != BehaviourStatus.Running)
            {
                VisionContext.Instance().DrawContent.RemoveRect("Target");
                VisionContext.Instance().DrawContent.RemoveRect("Fish");
            }
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
                Debug.WriteLine($"{noPlacementTimes}次未找到鱼饵落点");

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
                //    Simulation.SendInputEx.Mouse.MoveMouseBy(0, 100);
                //}

                if (noTargetFishTimes > 10)
                {
                    // 没有找到目标鱼，重新选择鱼饵
                    blackboard.selectedBaitName = string.Empty;
                    _logger.LogInformation("没有找到目标鱼，1.直接抛竿");
                    Simulation.SendInput.Mouse.LeftButtonUp();
                    blackboard.Sleep(2000);
                    _logger.LogInformation("没有找到目标鱼，2.收杆");
                    Simulation.SendInput.Mouse.LeftButtonClick();
                    blackboard.Sleep(800);

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
    public class CheckThrowRod : BaseBehaviour<CaptureContent>
    {
        private readonly ILogger<CheckThrowRod> _logger = App.GetLogger<CheckThrowRod>();
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

    public class FishBiteTimeout : BaseBehaviour<CaptureContent>
    {
        private readonly ILogger<FishBiteTimeout> _logger = App.GetLogger<FishBiteTimeout>();
        private DateTime? waitFishBiteTimeout;
        private int seconds;

        /// <summary>
        /// 如果未超时返回运行中，超时返回失败
        /// </summary>
        /// <param name="name"></param>
        /// <param name="seconds"></param>
        public FishBiteTimeout(string name, int seconds) : base(name)
        {
            this.seconds = seconds;
        }
        protected override void OnInitialize()
        {
            waitFishBiteTimeout = DateTime.Now.AddSeconds(seconds);
        }
        protected override BehaviourStatus Update(CaptureContent context)
        {
            if (DateTime.Now >= waitFishBiteTimeout)
            {
                _logger.LogInformation($"{seconds}秒没有咬杆，本次收杆");
                Simulation.SendInput.Mouse.LeftButtonClick();
                TaskControl.Sleep(1000);
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
    public class FishBite : BaseBehaviour<CaptureContent>
    {
        private readonly ILogger<FishBite> _logger = App.GetLogger<FishBite>();
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
        private readonly ILogger<GetFishBoxArea> _logger = App.GetLogger<GetFishBoxArea>();
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
                using var boxRa = content.CaptureRectArea.Derive(blackboard.fishBoxRect);
                boxRa.DrawSelf("FishBox", new Pen(Color.LightPink, 2));
                return BehaviourStatus.Succeeded;
            }

            return BehaviourStatus.Running;

            //CheckFishingUserInterface(content);
        }
    }

    /// <summary>
    /// 拉条
    /// </summary>
    public class Fishing : BaseBehaviour<CaptureContent>
    {
        private readonly ILogger<Fishing> _logger = App.GetLogger<Fishing>();
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
                _logger.LogInformation("  拉扯结束");
                _logger.LogInformation(@"└------------------------┘");

                // 保证鼠标松开
                simulator.Mouse.LeftButtonUp();

                blackboard.Sleep(7000);

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
    /// 如果视角被其他行为重置过，则调整视角至俯视
    /// </summary>
    public class MoveViewpointDown : BaseBehaviour<CaptureContent>
    {
        private readonly ILogger<MoveViewpointDown> _logger = App.GetLogger<MoveViewpointDown>();
        private readonly Blackboard blackboard;
        public MoveViewpointDown(string name, Blackboard blackboard) : base(name)
        {
            this.blackboard = blackboard;
        }

        protected override BehaviourStatus Update(CaptureContent context)
        {
            if (blackboard.pitchReset)
            {
                _logger.LogInformation("调整视角至俯视");
                blackboard.pitchReset = false;
                // 下移视角方便看鱼
                Simulation.SendInput.Mouse.MoveMouseBy(0, 400);
                blackboard.Sleep(100);
                return BehaviourStatus.Running;
            }
            return BehaviourStatus.Succeeded;
        }

    }
}
