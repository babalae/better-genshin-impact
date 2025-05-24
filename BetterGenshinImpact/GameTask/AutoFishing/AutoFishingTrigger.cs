using BehaviourTree;
using BehaviourTree.FluentBuilder;
using BehaviourTree.Composites;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Point = OpenCvSharp.Point;
using Fischless.WindowsInput;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.Extensions.Localization;
using BetterGenshinImpact.Core.Recognition.OCR;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTrigger : ITaskTrigger
    {
        private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
        private readonly InputSimulator input = Simulation.SendInput;

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

        private readonly BgiYoloPredictor _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiFish);

        /// <summary>
        /// 辣条（误）
        /// </summary>
        private IBehaviour<ImageRegion> BehaviourTreeLaTiao { get; set; }

        public AutoFishingTrigger()
        {
            AutoFishingTaskParam autoFishingTaskParam =
                AutoFishingTaskParam.BuildFromConfig(TaskContext.Instance().Config.AutoFishingConfig);
            IOcrService ocrService = OcrFactory.Paddle;
            IStringLocalizer<AutoFishingImageRecognition> stringLocalizer =
                App.GetService<IStringLocalizer<AutoFishingImageRecognition>>() ??
                throw new NullReferenceException(nameof(stringLocalizer));
            this.blackboard = new Blackboard(_predictor, this.Sleep, AutoFishingAssets.Instance);

            BehaviourTreeLaTiao = FluentBuilder.Create<ImageRegion>()
                .MySimpleParallel("root", policy: SimpleParallelPolicy.OnlyOneMustSucceed)
                .Do("检查是否在钓鱼界面", CheckFishingUserInterface)
                .UntilSuccess("拉条循环")
                .Sequence("拉条")
                .PushLeaf(() => new FishBite("自动提竿", blackboard, _logger, false, input, ocrService,
                    cultureInfo: autoFishingTaskParam.GameCultureInfo, stringLocalizer: stringLocalizer))
                .PushLeaf(() => new GetFishBoxArea("等待拉条出现", blackboard, _logger, false))
                .PushLeaf(() => new Fishing("钓鱼拉条", blackboard, _logger, false, input))
                .End()
                .End()
                .End()
                .Build();
        }

        public void Init()
        {
            IsEnabled = TaskContext.Instance().Config.AutoFishingConfig.Enabled;
            IsExclusive = false;
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
                CheckFishingUserInterface(content.CaptureRectArea);
            }
            else
            {
                // if (TaskContext.Instance().Config.AutoFishingConfig.AutoThrowRodEnabled)
                // {
                //     BehaviourTree.Tick(content);
                // }
                // else
                // {
                //     BehaviourTreeLaTiao.Tick(content);
                // }
                BehaviourTreeLaTiao.Tick(content.CaptureRectArea);
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
            TaskControl.Sleep(millisecondsTimeout);
        }

        /// <summary>
        /// 检查是否在钓鱼界面
        /// 方法是找右下角的退出钓鱼按钮
        /// 进入钓鱼界面时该触发器进入独占模式
        /// </summary>
        /// <param name="imageRegion"></param>
        private BehaviourStatus CheckFishingUserInterface(ImageRegion imageRegion)
        {
            if (blackboard.chooseBaitUIOpening)
            {
                return BehaviourStatus.Running;
            }

            var prevIsExclusive = IsExclusive;
            IsExclusive = !imageRegion.Find(AutoFishingAssets.Instance.ExitFishingButtonRo).IsEmpty();
            if (IsExclusive)
            {
                if (IsEnabled && !prevIsExclusive)
                {
                    _logger.LogInformation("→ {Text}", "半自动钓鱼，启动！");
                    // _logger.LogInformation("当前自动选饵抛竿状态[{Enabled}]", TaskContext.Instance().Config.AutoFishingConfig.AutoThrowRodEnabled.ToChinese());
                }

                return BehaviourStatus.Running;
            }
            else
            {
                if (prevIsExclusive)
                {
                    _logger.LogInformation("← {Text}", "退出钓鱼界面");
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