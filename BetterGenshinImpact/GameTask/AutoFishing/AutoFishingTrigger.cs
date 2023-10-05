using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.View.Drawable;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using WindowsInput;
using static Vanara.PInvoke.User32;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTrigger : ITaskTrigger
    {
        private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
        private readonly IOcrService _ocrService = OcrFactory.Create(OcrEngineType.WinRT);

        public string Name => "自动钓鱼";
        public bool IsEnabled { get; set; }
        public int Priority => 15;

        /// <summary>
        /// 钓鱼是要独占模式的
        /// 在钓鱼的时候，不应该有其他任务在执行
        /// 在触发器发现正在钓鱼的时候，启用独占模式
        /// </summary>
        public bool IsExclusive { get; set; }

        private readonly AutoFishingAssets _autoFishingAssets;

        public AutoFishingTrigger()
        {
            _autoFishingAssets = new AutoFishingAssets();
        }

        public void Init()
        {
            IsEnabled = TaskContext.Instance().Config.AutoFishingConfig.Enabled;
            IsExclusive = false;

            // 钓鱼变量初始化
            _findFishBoxTips = false;
        }

        private Rect _fishBoxRect = Rect.Empty;

        public void OnCapture(CaptureContent content)
        {
            // 进入独占的判定
            if (!IsExclusive)
            {
                if (!content.IsReachInterval(TimeSpan.FromMilliseconds(300)))
                {
                    return;
                }

                // 在“开始钓鱼”按钮上方安排一个我们的“开始自动钓鱼”按钮
                // 点击按钮进入独占模式
                DisplayButtonOnStartFishPageForExclusive(content);
            }
            else
            {
                // 自动抛竿
                ThrowRod(content);
                // 上钩判断
                FishBite(content);
                // 进入钓鱼界面先尝试获取钓鱼框的位置
                if (_fishBoxRect.Width == 0)
                {
                    if (!content.IsReachInterval(TimeSpan.FromMilliseconds(200)))
                    {
                        return;
                    }

                    _fishBoxRect = GetFishBoxArea(content.CaptureRectArea.SrcMat);
                }
                else
                {
                    // 钓鱼拉条
                    Fishing(content, new Mat(content.CaptureRectArea.SrcMat, _fishBoxRect));
                }
            }
        }



        /// <summary>
        /// 在“开始钓鱼”按钮上方安排一个我们的“开始自动钓鱼”按钮
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        private void DisplayButtonOnStartFishPageForExclusive(CaptureContent content)
        {
            VisionContext.Instance().DrawContent.RemoveRect("StartFishingButton");
            var info = TaskContext.Instance().SystemInfo;
            var srcMat = content.CaptureRectArea.SrcMat;
            var rightBottomMat = CropHelper.CutRightBottom(srcMat, srcMat.Width / 2, srcMat.Height / 2);
            var list = CommonRecognition.FindGameButton(rightBottomMat);
            if (list.Count > 0)
            {
                foreach (var rect in list)
                {
                    var ro = new RecognitionObject()
                    {
                        Name = "StartFishingText",
                        RecognitionType = RecognitionType.Ocr,
                        RegionOfInterest = new Rect(srcMat.Width / 2, srcMat.Height / 2, srcMat.Width - srcMat.Width / 2,
                            srcMat.Height - srcMat.Height / 2),
                        ContainMatchText = new List<string>
                        {
                            "开始", "钓鱼"
                        },
                        DrawOnWindow = false
                    };
                    var ocrRaRes = content.CaptureRectArea.Find(ro);
                    if (ocrRaRes.IsEmpty())
                    {
                        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RemoveButton", new object(), "开始自动钓鱼"));
                    }
                    else
                    {
                        VisionContext.Instance().DrawContent.PutRect("StartFishingButton", rect.ToWindowsRectangleOffset(srcMat.Width / 2, srcMat.Height / 2).ToRectDrawable());

                        var btnPosition = new Rect(rect.X + srcMat.Width / 2, rect.Y + srcMat.Height / 2 - rect.Height - 10, rect.Width, rect.Height);
                        var maskButton = new MaskButton("开始自动钓鱼", btnPosition, () =>
                        {
                            VisionContext.Instance().DrawContent.RemoveRect("StartFishingButton");
                            _logger.LogInformation("自动钓鱼，启动！");
                            // 点击下面的按钮
                            var rc = info.GameWindowRect;
                            new InputSimulator()
                                .Mouse
                                .MoveMouseTo(
                                    (rc.X + srcMat.Width * 1d / 2 + rect.X + rect.Width * 1d / 2) * 65535 / info.DesktopRectArea.Width,
                                    (rc.Y + srcMat.Height * 1d / 2 + rect.Y + rect.Height * 1d / 2) * 65535 / info.DesktopRectArea.Height)
                                .LeftButtonClick();
                            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RemoveButton", new object(), "开始自动钓鱼"));
                            // 启动要延时一会等待钓鱼界面切换
                            Thread.Sleep(1000);
                            IsExclusive = true;
                            _switchBaitContinuouslyFrameNum = 0;
                            _waitBiteContinuouslyFrameNum = 0;
                            _noFishActionContinuouslyFrameNum = 0;
                            _isThrowRod = false;
                        });
                        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "AddButton", new object(), maskButton));
                    }
                }
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RemoveButton", new object(), "开始自动钓鱼"));
            }
        }


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
        /// 找右下角的 Space 按钮
        /// 用于判断是否进入钓鱼界面
        /// 进入钓鱼界面时该触发器进入独占模式
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        private bool FindSpaceButtonForExclusive(CaptureContent content)
        {
            return !content.CaptureRectArea.Find(_autoFishingAssets.SpaceButtonRo).IsEmpty();
        }


        private int _throwRodWaitFrameNum = 0; // 抛竿等待的时间(帧数)
        private int _switchBaitContinuouslyFrameNum = 0; // 切换鱼饵的持续时间(帧数)
        private int _waitBiteContinuouslyFrameNum = 0; // 等待上钩的持续时间(帧数)
        private int _noFishActionContinuouslyFrameNum = 0; // 无钓鱼三种场景的持续时间(帧数)
        private bool _isThrowRod = false; // 是否已经抛竿

        /// <summary>
        /// 钓鱼有3种场景
        /// 1. 未抛竿 BaitButtonRo存在 && WaitBiteButtonRo不存在
        /// 2. 抛竿后未拉条 WaitBiteButtonRo存在 && BaitButtonRo不存在
        /// 3. 上钩拉条 _isFishingProcess && _biteTipsExitCount > 0
        /// </summary>
        /// <param name="content"></param>
        private void ThrowRod(CaptureContent content)
        {
            // 没有拉条和提竿的时候，自动抛竿
            if (!_isFishingProcess && _biteTipsExitCount == 0 && TaskContext.Instance().Config.AutoFishingConfig.AutoThrowRodEnabled)
            {
                var baitRectArea = content.CaptureRectArea.Find(_autoFishingAssets.BaitButtonRo);
                var waitBiteArea = content.CaptureRectArea.Find(_autoFishingAssets.WaitBiteButtonRo);
                if (!baitRectArea.IsEmpty() && waitBiteArea.IsEmpty())
                {
                    _switchBaitContinuouslyFrameNum++;
                    _waitBiteContinuouslyFrameNum = 0;
                    _noFishActionContinuouslyFrameNum = 0;

                    if (_switchBaitContinuouslyFrameNum >= content.FrameRate)
                    {
                        _isThrowRod = false;
                        _switchBaitContinuouslyFrameNum = 0;
                    }

                    if (!_isThrowRod)
                    {
                        new InputSimulator().Mouse.LeftButtonClick();
                        Debug.WriteLine("自动抛竿");
                        Thread.Sleep(500);
                        _isThrowRod = true;
                    }
                }
                if (baitRectArea.IsEmpty() && !waitBiteArea.IsEmpty() && _isThrowRod)
                {
                    _switchBaitContinuouslyFrameNum = 0;
                    _waitBiteContinuouslyFrameNum ++;
                    _noFishActionContinuouslyFrameNum = 0;
                    _throwRodWaitFrameNum++;


                    if (_waitBiteContinuouslyFrameNum >= content.FrameRate)
                    {
                        _isThrowRod = true;
                        _waitBiteContinuouslyFrameNum = 0;
                    }


                    if (_isThrowRod)
                    {
                        // 30s 没有上钩，重新抛竿
                        if (_throwRodWaitFrameNum >= content.FrameRate * 20)
                        {
                            new InputSimulator().Mouse.LeftButtonClick();
                            _throwRodWaitFrameNum = 0;
                            _waitBiteContinuouslyFrameNum = 0;
                            Debug.WriteLine("超时自动收竿");
                            Thread.Sleep(2000);
                            _isThrowRod = false;
                        }
                    }
                }

                if (baitRectArea.IsEmpty() && waitBiteArea.IsEmpty())
                {
                    _switchBaitContinuouslyFrameNum = 0;
                    _waitBiteContinuouslyFrameNum = 0;
                    _noFishActionContinuouslyFrameNum++;
                    if (_noFishActionContinuouslyFrameNum > content.FrameRate)
                    {
                        CheckFishingInterface(content);
                    }
                }
            }
            else
            {
                _switchBaitContinuouslyFrameNum = 0;
                _waitBiteContinuouslyFrameNum = 0;
                _noFishActionContinuouslyFrameNum = 0;
                _throwRodWaitFrameNum = 0;
                _isThrowRod = false;
            }
        }


        /// <summary>
        /// 获取钓鱼框的位置
        /// </summary>
        private Rect GetFishBoxArea(Mat srcMat)
        {
            srcMat = CropHelper.CutTop(srcMat, srcMat.Height / 2);
            var rects = AutoFishingImageRecognition.GetFishBarRect(srcMat);
            if (rects != null && rects.Count == 2)
            {
                if (Math.Abs(rects[0].Height - rects[1].Height) > 10)
                {
                    Debug.WriteLine("两个矩形高度差距过大，未识别到钓鱼框");
                    VisionContext.Instance().DrawContent.RemoveRect("FishBox");
                    return Rect.Empty;
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
                    || _cur.X + _cur.Width > srcMat.Width / 2 // cur 一定在屏幕左侧
                    || !(_left.X < srcMat.Width / 2 && _left.X + _left.Width > srcMat.Width / 2) // left肯定穿过游戏中轴线
                   )
                {
                    VisionContext.Instance().DrawContent.RemoveRect("FishBox");
                    return Rect.Empty;
                }

                int hExtra = _cur.Height, vExtra = _cur.Height / 4;
                _fishBoxRect = new Rect(_cur.X - hExtra, _cur.Y - vExtra,
                    (_left.X + _left.Width / 2 - _cur.X) * 2 + hExtra * 2, _cur.Height + vExtra * 2);
                VisionContext.Instance().DrawContent
                    .PutRect("FishBox", _fishBoxRect.ToRectDrawable(new Pen(Color.LightPink, 2)));
                return _fishBoxRect;
            }

            VisionContext.Instance().DrawContent.RemoveRect("FishBox");
            return Rect.Empty;
        }


        private bool _isFishingProcess = false; // 提杆后会设置为true
        int _biteTipsExitCount = 0; // 钓鱼提示持续时间
        int _notFishingAfterBiteCount = 0; // 提竿后没有钓鱼的时间
        Rect _baseBiteTips = Rect.Empty;

        /// <summary>
        /// 自动提竿
        /// </summary>
        /// <param name="content"></param>
        private void FishBite(CaptureContent content)
        {
            if (_isFishingProcess)
            {
                return;
            }

            // 自动识别的钓鱼框向下延伸到屏幕中间
            //var liftingWordsAreaRect = new Rect(fishBoxRect.X, fishBoxRect.Y + fishBoxRect.Height * 2,
            //    fishBoxRect.Width, content.CaptureRectArea.SrcMat.Height / 2 - fishBoxRect.Y - fishBoxRect.Height * 5);
            // 上半屏幕和中间1/3的区域
            var liftingWordsAreaRect = new Rect(content.CaptureRectArea.SrcMat.Width / 3, 0, content.CaptureRectArea.SrcMat.Width / 3,
                content.CaptureRectArea.SrcMat.Height / 2);
            //VisionContext.Instance().DrawContent.PutRect("liftingWordsAreaRect", liftingWordsAreaRect.ToRectDrawable(new Pen(Color.Cyan, 2)));
            var wordCaptureMat = new Mat(content.CaptureRectArea.SrcMat, liftingWordsAreaRect);
            var wordCaptureOriginMat = wordCaptureMat.Clone();
            var currentBiteWordsTips =
                AutoFishingImageRecognition.MatchFishBiteWords(wordCaptureMat,
                    liftingWordsAreaRect);
            if (currentBiteWordsTips != Rect.Empty)
            {
                if (_baseBiteTips == Rect.Empty)
                {
                    _baseBiteTips = currentBiteWordsTips;
                }
                else
                {
                    if (Math.Abs(_baseBiteTips.X - currentBiteWordsTips.X) < 10
                        && Math.Abs(_baseBiteTips.Y - currentBiteWordsTips.Y) < 10
                        && Math.Abs(_baseBiteTips.Width - currentBiteWordsTips.Width) < 10
                        && Math.Abs(_baseBiteTips.Height - currentBiteWordsTips.Height) < 10)
                    {
                        _biteTipsExitCount++;
                        VisionContext.Instance().DrawContent.PutRect("FishBiteTips",
                            currentBiteWordsTips
                                .ToWindowsRectangleOffset(liftingWordsAreaRect.X, liftingWordsAreaRect.Y)
                                .ToRectDrawable());

                        if (_biteTipsExitCount >= content.FrameRate / 4d)
                        {
                            var text = _ocrService.Ocr(wordCaptureOriginMat.ToBitmap());
                            if (!string.IsNullOrEmpty(text) && StringUtils.RemoveAllSpace(text).Contains("上钩"))
                            {
                                new InputSimulator().Mouse.LeftButtonClick();
                                _logger.LogInformation(@"┌------------------------┐");
                                _logger.LogInformation("  自动提竿(OCR)");
                                _isFishingProcess = true;
                                _biteTipsExitCount = 0;
                                _baseBiteTips = Rect.Empty;
                                VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
                            }
                        }
                    }
                    else
                    {
                        _biteTipsExitCount = 0;
                        _baseBiteTips = currentBiteWordsTips;
                        VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
                    }

                    if (_biteTipsExitCount >= content.FrameRate / 2d)
                    {
                        new InputSimulator().Mouse.LeftButtonClick();
                        _logger.LogInformation(@"┌------------------------┐");
                        _logger.LogInformation("  自动提竿(文字块)");
                        _isFishingProcess = true;
                        _biteTipsExitCount = 0;
                        _baseBiteTips = Rect.Empty;
                        VisionContext.Instance().DrawContent.RemoveRect("FishBiteTips");
                    }
                }
            }
        }


        private int _noRectsCount = 0;
        private Rect _cur, _left, _right;
        private MOUSEEVENTF _prevMouseEvent = 0x0;
        private bool _findFishBoxTips;


        /// <summary>
        /// 钓鱼拉条
        /// </summary>
        /// <param name="content"></param>
        /// <param name="fishBarMat"></param>
        private void Fishing(CaptureContent content, Mat fishBarMat)
        {
            var rects = AutoFishingImageRecognition.GetFishBarRect(fishBarMat);
            if (rects != null && rects.Count > 0)
            {
                var simulator = new InputSimulator();
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

                    PutRects(_left, _cur, new Rect());

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
                    PutRects(_left, _cur, _right);

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
                    PutRects(new Rect(), new Rect(), new Rect());
                }
            }
            else
            {
                PutRects(new Rect(), new Rect(), new Rect());
                _noRectsCount++;
                // 2s 没有矩形视为已经完成钓鱼
                if (_noRectsCount >= content.FrameRate * 2 && _prevMouseEvent != 0x0)
                {
                    _findFishBoxTips = false;
                    _isFishingProcess = false;
                    _isThrowRod = false;
                    _prevMouseEvent = 0x0;
                    _logger.LogInformation("  钓鱼结束");
                    _logger.LogInformation(@"└------------------------┘");
                    Thread.Sleep(1000);
                }

                CheckFishingInterface(content);
            }

            // 提竿后没有钓鱼的情况
            if (_isFishingProcess && !_findFishBoxTips)
            {
                _notFishingAfterBiteCount++;
                if (_notFishingAfterBiteCount >= decimal.ToDouble(content.FrameRate) * 2)
                {
                    _isFishingProcess = false;
                    _isThrowRod = false;
                    _notFishingAfterBiteCount = 0;
                    _logger.LogInformation("  X 提竿后没有钓鱼，重置!");
                    _logger.LogInformation(@"└------------------------┘");
                }
            }
            else
            {
                _notFishingAfterBiteCount = 0;
            }
        }

        /// <summary>
        /// 检查是否退出钓鱼界面
        /// </summary>
        /// <param name="content"></param>
        private void CheckFishingInterface(CaptureContent content)
        {
            IsExclusive = FindSpaceButtonForExclusive(content);
            if (!IsExclusive)
            {
                _isThrowRod = false;
                _logger.LogInformation("退出钓鱼界面");
                _fishBoxRect = Rect.Empty;
                VisionContext.Instance().DrawContent.RemoveRect("FishBox");
            }
        }

        private void PutRects(Rect left, Rect cur, Rect right)
        {
            Pen pen = new(Color.Red, 1);
            var list = new List<(string, RectDrawable)>
            {
                ("FishingBarLeft", left.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(pen)),
                ("FishingBarCur", cur.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(pen)),
                ("FishingBarRight", right.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(pen))
            };
            VisionContext.Instance().DrawContent.PutOrRemoveRectList(list);
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