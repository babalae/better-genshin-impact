using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameTask.Model.GameUI
{
    public class GridScreen : IAsyncEnumerable<ImageRegion>
    {
        private readonly Rect gridRoi;
        private readonly CancellationToken ct;
        private readonly ILogger logger;
        private readonly InputSimulator input = Simulation.SendInput;
        private readonly int s1Round;
        private readonly int roundMilliseconds;
        private readonly int s2Round;
        private readonly double s3Scale;

        /// <summary>
        /// 对Gird类型界面的操作封装类
        /// 直接对此类对象进行遍历即可获取所有项
        /// 每次的截图是上次滚动后的，如果实时性要求高，应每次迭代自行截图
        /// 在末页可能重复返回GridItem，须自行处理
        /// </summary>
        /// <param name="gridRoi">Grid所在位置</param>
        /// <param name="logger"></param>
        /// <param name="ct"></param>
        public GridScreen(Rect gridRoi, int s1Round, int roundMilliseconds, int s2Round, double s3Scale, ILogger logger, CancellationToken ct)
        {
            this.gridRoi = gridRoi;
            this.ct = ct;
            this.logger = logger;
            this.s1Round = s1Round;
            this.roundMilliseconds = roundMilliseconds;
            this.s2Round = s2Round;
            this.s3Scale = s3Scale;
        }

        public IAsyncEnumerator<ImageRegion> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new GridEnumerator(this.gridRoi, this.s1Round, this.roundMilliseconds, this.s2Round, this.s3Scale, this.logger, this.input, this.ct);
        }

        public class GridEnumerator : IAsyncEnumerator<ImageRegion>
        {
            private readonly Rect roi;
            private readonly CancellationToken ct;
            private readonly ILogger logger;
            private readonly InputSimulator input = Simulation.SendInput;
            private readonly int s1Round;
            private readonly int roundMilliseconds;
            private readonly int s2Round;
            private readonly double s3Scale;

            private record Page(ImageRegion ImageRegion, Stack<Rect> Rects);
            private Page? currentPage;
            private ImageRegion current;
            ImageRegion IAsyncEnumerator<ImageRegion>.Current => current;

            /// <summary>
            /// 滚动操作枚举器
            /// </summary>
            /// <param name="roi"></param>
            /// <param name="s1Round">测试是否能滚动时发出的滚动命令次数</param>
            /// <param name="roundMilliseconds">滚动命令间隔毫秒</param>
            /// <param name="s2Round">滚过一整页时发出的滚动命令次数</param>
            /// <param name="s3Scale">微调滚动时控制首行距离上边界的参数</param>
            /// <param name="logger"></param>
            /// <param name="input"></param>
            /// <param name="ct"></param>
            public GridEnumerator(Rect roi, int s1Round, int roundMilliseconds, int s2Round, double s3Scale, ILogger logger, InputSimulator input, CancellationToken ct)
            {
                this.roi = roi;
                this.ct = ct;
                this.logger = logger;
                this.input = input;
                this.s1Round = s1Round;
                this.roundMilliseconds = roundMilliseconds;
                this.s2Round = s2Round;
                this.s3Scale = s3Scale;
            }

            public async Task<bool> TryVerticalScollDown()
            {
                using var ra = TaskControl.CaptureToRectArea();
                using ImageRegion prevGrid = ra.DeriveCrop(roi);

                for (int i = 0; i < this.s1Round; i++)
                {
                    this.input.Mouse.VerticalScroll(-2);
                    await TaskControl.Delay(this.roundMilliseconds, this.ct);
                }
                await TaskControl.Delay(300, this.ct);
                using var ra2 = TaskControl.CaptureToRectArea();
                using ImageRegion scrolledGrid = ra2.DeriveCrop(this.roi);

                bool isScrolling = IsScrolling(prevGrid.CacheGreyMat, scrolledGrid.CacheGreyMat, out Point2d shift, logger: this.logger);

                return isScrolling;
            }

            /// <summary>
            /// 判断是否还能继续滚动，如果到底了则只能滚动一丝并很快地回弹
            /// </summary>
            /// <param name="prevGray">先前的灰度图</param>
            /// <param name="nextGray">尝试滚动并等待可能的回弹后的灰度图</param>
            /// <param name="shift">估计的位移</param>
            /// <param name="lowerThreshold">低于下限则可能不存在平移</param>
            /// <param name="upperThreshold">上限用于抵消微小的其他差异</param>
            /// <param name="logger"></param>
            /// <returns></returns>
            public static bool IsScrolling(Mat prevGray, Mat nextGray, out Point2d shift, double lowerThreshold = 0.5, double upperThreshold = 0.95, ILogger? logger = null)
            {
                using Mat prev = new Mat();
                prevGray.ConvertTo(prev, MatType.CV_32FC1);
                using Mat next = new Mat();
                nextGray.ConvertTo(next, MatType.CV_32FC1);

                using Mat window = new Mat();
                shift = Cv2.PhaseCorrelate(prev, next, window, out double response);    // 相位相关性
                logger?.LogInformation($"response={response:F3}, shift=({shift.X:F2}, {shift.Y:F2})");
                return response > lowerThreshold && response < upperThreshold;
            }

            public static IEnumerable<Rect> GetGridItems(Mat src)
            {
                using Mat grey = src.CvtColor(ColorConversionCodes.BGR2GRAY);

                using Mat canny = grey.Canny(20, 40);

                Cv2.FindContours(canny, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple, null);

                IEnumerable<Rect> boxes = contours.Where(c => Cv2.MinAreaRect(c).Angle % 90 <= 1)   // 剔除倾斜
                    .Select(Cv2.BoundingRect).Where(r =>
                    {
                        if (r.Height == 0)
                        {
                            return false;
                        }
                        return Math.Abs((float)r.Width / r.Height - 0.8) < 0.05; // 按形状筛选
                    }).ToList();

                //src.DrawContours(contours, -1, Scalar.Red);

                int biggestRectHeight = boxes.Max(b => b.Height);
                boxes = boxes.Where(b => (float)b.Height / biggestRectHeight > 0.88);   // 剔除太小的

                return boxes.ToArray();
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                if (this.currentPage == null || this.currentPage.Rects.Count < 1)
                {
                    if (this.currentPage != null)
                    {
                        //BetterGenshinImpact.View.Drawable.VisionContext.Instance().DrawContent.ClearAll();

                        using var ra4 = TaskControl.CaptureToRectArea();
                        ra4.MoveTo(this.roi.X + this.roi.Width / 2, this.roi.Y + this.roi.Height / 2);
                        await TaskControl.Delay(300, ct);

                        bool canScoll = await TryVerticalScollDown();

                        if (canScoll)
                        {
                            for (int i = 0; i < this.s2Round; i++)    // 再滚动差不多（最多行数-1）行
                            {
                                input.Mouse.VerticalScroll(-2);
                                await TaskControl.Delay(this.roundMilliseconds, ct);
                            }

                            DateTimeOffset rollingEndTime = DateTime.Now.AddSeconds(2);
                            while (DateTime.Now < rollingEndTime)
                            {
                                await TaskControl.Delay(60, ct);
                                using var ra2 = TaskControl.CaptureToRectArea();
                                using ImageRegion grid2 = ra2.DeriveCrop(this.roi);
                                IEnumerable<Rect> gridItems2 = GetGridItems(grid2.SrcMat);
                                if (gridItems2.Min(i => i.Y) > (ra2.Width * this.s3Scale))  // 最后精细滚动，保证完整地显示最多行
                                {
                                    input.Mouse.VerticalScroll(-1);
                                }
                                else
                                {
                                    break;
                                }
                            }
                            using var ra3 = TaskControl.CaptureToRectArea();
                            using ImageRegion grid3 = ra3.DeriveCrop(this.roi);
                            grid3.MoveTo(grid3.Width, grid3.Height);
                            await TaskControl.Delay(300, ct);
                        }
                        else
                        {
                            await TaskControl.Delay(300, ct);
                            this.logger.LogInformation("滚动到底部了");
                            return false;
                        }
                    }

                    using var ra = TaskControl.CaptureToRectArea();
                    var imageRegion = ra.DeriveCrop(this.roi);
                    IEnumerable<Rect> gridItems = GetGridItems(imageRegion.SrcMat);
                    this.currentPage = new Page(imageRegion, new Stack<Rect>(gridItems));

                    //foreach (Rect item in gridItems)
                    //{
                    //    imageRegion.DrawRect(item, item.GetHashCode().ToString(), new System.Drawing.Pen(System.Drawing.Color.Blue));
                    //}
                }

                this.current = this.currentPage.ImageRegion.DeriveCrop(this.currentPage.Rects.Pop());
                return true;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
