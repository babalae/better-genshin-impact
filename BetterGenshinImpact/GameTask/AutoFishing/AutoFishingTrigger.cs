using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using OpenCvSharp;
using Vision.Recognition;
using Vision.Recognition.Helper.OpenCv;
using Vision.Recognition.Task;
using WindowsInput;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.User32;
using WinRT;
using Windows.ApplicationModel.Contacts;

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

        public void Init()
        {
            IsEnabled = true;
            IsExclusive = true;

            // 钓鱼变量初始化
            _findFishBoxTips = false;
        }

        private Rect _fishBoxRect = new(0, 0, 0, 0);

        public void OnCapture(CaptureContent content)
        {
            // TODO 进入独占的判定

            if (_fishBoxRect.Width == 0)
            {
                GetFishBoxArea(content.SrcMat);
            }
            else
            {
                Fishing(content, new Mat(content.SrcMat, _fishBoxRect));
            }
        }


        /// <summary>
        /// 获取钓鱼框的位置
        /// </summary>
        private void GetFishBoxArea(Mat srcMat)
        {
            var rects = AutoFishingImageRecognition.GetFishBarRect(srcMat);
            if (rects != null && rects.Count == 2)
            {
                if (Math.Abs(rects[0].Height - rects[1].Height) > 10)
                {
                    Debug.WriteLine("两个矩形高度差距过大，未识别到钓鱼框");
                    return;
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

                // cur 是游标位置, 在初始状态下，cur 一定在left左边
                if (_left.X < _cur.X)
                {
                    return;
                }

                int hExtra = _cur.Height, vExtra = _cur.Height / 4;
                _fishBoxRect = new Rect(_cur.X - hExtra, _cur.Y - vExtra,
                    (_left.X + _left.Width / 2 - _cur.X) * 2 + hExtra * 2, _cur.Height + vExtra * 2);
                VisionContext.Instance().DrawContent.PutRect("FishBox", _fishBoxRect.ToWindowsRectangle());
            }
        }


        private int _noRectsCount = 0;
        private bool _isFishingProcess = false; // 提杆后会设置为true
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

                Debug.WriteLine($"识别到{rects.Count} 个矩形");
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
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN;
                            Debug.WriteLine("进度不到 左键按下");
                        }
                    }
                    else
                    {
                        if (_prevMouseEvent == MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            simulator.Mouse.LeftButtonUp();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                            Debug.WriteLine("进度超出 左键松开");
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
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                            Debug.WriteLine("进入框内中间 左键松开");
                        }
                    }
                    else
                    {
                        if (_prevMouseEvent != MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            simulator.Mouse.LeftButtonDown();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN;
                            Debug.WriteLine("未到框内中间 左键按下");
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
                    //_logger.LogInformation(@"└------------------------┘");
                }
            }
        }

        private void PutRects(Rect left, Rect cur, Rect right)
        {
            var list = new List<(string, System.Windows.Rect)>
            {
                ("FishingBarLeft", left.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y)),
                ("FishingBarCur", cur.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y)),
                ("FishingBarRight", right.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y))
            };
            VisionContext.Instance().DrawContent.PutOrRemoveRectList(list);
        }
    }
}