using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Vision.Recognition;
using Vision.Recognition.Helper.OpenCv;
using Vision.Recognition.Task;
using WindowsInput;
using Windows.Win32.UI.Input.KeyboardAndMouse;

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
        /// 在触发器发现正在钓鱼的时候，启用独占模式(通过右下角的 Space 判断)
        /// </summary>
        public bool IsExclusive { get; set; }

        public void Init()
        {
            IsEnabled = true;
            IsExclusive = false;

            // 钓鱼变量初始化
            _findFishBoxTips = false;
        }

        private Rect _fishBoxRect = new(0, 0, 0, 0);

        public void OnCapture(CaptureContent content)
        {
            // 进入独占的判定 通过右下角的 Space 判断
            if (!IsExclusive)
            {
                if (!content.IsReachInterval(TimeSpan.FromSeconds(1)))
                {
                    return;
                }

                // 找右下角的 Space 按钮
                IsExclusive = FindSpaceButtonForExclusive(content);
                if (IsExclusive)
                {
                    _logger.LogInformation("进入钓鱼界面");
                    _fishBoxRect = new(0, 0, 0, 0);
                }
            }
            else
            {
                // 进入钓鱼界面先尝试获取钓鱼框的位置
                if (_fishBoxRect.Width == 0)
                {
                    if (!content.IsReachInterval(TimeSpan.FromMilliseconds(200)))
                    {
                        return;
                    }

                    _fishBoxRect = GetFishBoxArea(content.SrcMat);
                }
                else
                {
                    // 上钩判断
                    FishBite(content, _fishBoxRect);
                    // 钓鱼拉条
                    Fishing(content, new Mat(content.SrcMat, _fishBoxRect));
                }
            }
        }

        /// <summary>
        /// 找右下角的 Space 按钮
        /// 用于判断是否进入钓鱼界面
        /// 进入钓鱼界面时该触发器进入独占模式
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        private bool FindSpaceButtonForExclusive(CaptureContent content)
        {
            var grayMat = content.SrcGreyMat;
            var grayRightBottomMat = CutHelper.CutRightBottom(grayMat, grayMat.Width / 3, grayMat.Height / 5);
            var p = MatchTemplateHelper.FindSingleTarget(grayRightBottomMat, AutoFishingAssets.SpaceButtonMat);
            return p is { X: > 0, Y: > 0 };
        }


        /// <summary>
        /// 获取钓鱼框的位置
        /// </summary>
        private Rect GetFishBoxArea(Mat srcMat)
        {
            srcMat = CutHelper.CutTop(srcMat, srcMat.Height / 2);
            var rects = AutoFishingImageRecognition.GetFishBarRect(srcMat);
            if (rects != null && rects.Count == 2)
            {
                if (Math.Abs(rects[0].Height - rects[1].Height) > 10)
                {
                    Debug.WriteLine("两个矩形高度差距过大，未识别到钓鱼框");
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
        /// <param name="fishBoxRect"></param>
        private void FishBite(CaptureContent content, Rect fishBoxRect)
        {
            if (_isFishingProcess || fishBoxRect == Rect.Empty)
            {
                return;
            }
            // 自动识别的钓鱼框向下延伸到屏幕中间
            var liftingWordsAreaRect = new Rect(fishBoxRect.X, fishBoxRect.Y + fishBoxRect.Height * 2,
                fishBoxRect.Width, content.SrcMat.Height / 2 - fishBoxRect.Y - fishBoxRect.Height * 5);
            //VisionContext.Instance().DrawContent.PutRect("liftingWordsAreaRect", liftingWordsAreaRect.ToRectDrawable(new Pen(Color.Cyan, 2)));
            var currentBiteWordsTips =
                AutoFishingImageRecognition.MatchFishBiteWords(new Mat(content.SrcMat, liftingWordsAreaRect),
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
                    }
                    else
                    {
                        _biteTipsExitCount = 0;
                        _baseBiteTips = currentBiteWordsTips;
                    }

                    if (_biteTipsExitCount >= content.FrameRate / 2d)
                    {
                        new InputSimulator().Mouse.LeftButtonClick();
                        _logger.LogInformation(@"┌------------------------┐");
                        _logger.LogInformation("  自动提竿");
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
        private MOUSE_EVENT_FLAGS _prevMouseEvent = 0x0;
        private bool _findFishBoxTips;


        /// <summary>
        /// 钓鱼拉条
        /// </summary>
        /// <param name="content"></param>
        /// <param name="fishBarMat"></param>
        private void Fishing(CaptureContent content, Mat fishBarMat)
        {
            List<Rect>? rects = AutoFishingImageRecognition.GetFishBarRect(fishBarMat);
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
                        if (_prevMouseEvent != MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN)
                        {
                            simulator.Mouse.LeftButtonDown();
                            //Simulator.PostMessage(TaskContext.Instance().GameHandle).LeftButtonDown();
                            _prevMouseEvent = MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN;
                            //Debug.WriteLine("进度不到 左键按下");
                        }
                    }
                    else
                    {
                        if (_prevMouseEvent == MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN)
                        {
                            simulator.Mouse.LeftButtonUp();
                            //Simulator.PostMessage(TaskContext.Instance().GameHandle).LeftButtonUp();
                            _prevMouseEvent = MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP;
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
                        if (_prevMouseEvent == MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN)
                        {
                            simulator.Mouse.LeftButtonUp();
                            //Simulator.PostMessage(TaskContext.Instance().GameHandle).LeftButtonUp();
                            _prevMouseEvent = MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP;
                            //Debug.WriteLine("进入框内中间 左键松开");
                        }
                    }
                    else
                    {
                        if (_prevMouseEvent != MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN)
                        {
                            simulator.Mouse.LeftButtonDown();
                            //Simulator.PostMessage(TaskContext.Instance().GameHandle).LeftButtonDown();
                            _prevMouseEvent = MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN;
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
                    _prevMouseEvent = 0x0;
                    _logger.LogInformation("  钓鱼结束");
                    _logger.LogInformation(@"└------------------------┘");
                }

                IsExclusive = FindSpaceButtonForExclusive(content);
                if (!IsExclusive)
                {
                    _logger.LogInformation("退出钓鱼界面");
                    //_fishBoxRect = new(0, 0, 0, 0);
                }
            }

            // 提竿后没有钓鱼的情况
            if (_isFishingProcess && !_findFishBoxTips)
            {
                _notFishingAfterBiteCount++;
                if (_notFishingAfterBiteCount >= decimal.ToDouble(content.FrameRate) * 2)
                {
                    _isFishingProcess = false;
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
    }
}