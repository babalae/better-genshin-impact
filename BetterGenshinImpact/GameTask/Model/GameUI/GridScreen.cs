using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
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

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    public class GridScreen : IAsyncEnumerable<ImageRegion>
    {
        private readonly Rect gridRoi;
        private readonly CancellationToken ct;
        private readonly ILogger logger;
        private readonly InputSimulator input = Simulation.SendInput;
        private readonly int columns;
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
        public GridScreen(Rect gridRoi, GridScreenParams @params, ILogger logger, CancellationToken ct)
        {
            this.gridRoi = gridRoi;
            this.ct = ct;
            this.logger = logger;
            if (@params.Columns < 4)
            {
                throw new ArgumentOutOfRangeException(nameof(@params.Columns));
            }
            this.columns = @params.Columns;
            this.s1Round = @params.S1Round;
            this.roundMilliseconds = @params.RoundMilliseconds;
            this.s2Round = @params.S2Round;
            this.s3Scale = @params.S3Scale;
        }

        public IAsyncEnumerator<ImageRegion> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new GridEnumerator(gridRoi, columns, s1Round, roundMilliseconds, s2Round, s3Scale, logger, input, ct);
        }

        public class GridEnumerator : IAsyncEnumerator<ImageRegion>
        {
            private readonly Rect roi;
            private readonly CancellationToken ct;
            private readonly ILogger logger;
            private readonly InputSimulator input = Simulation.SendInput;
            private readonly int columns;
            private readonly int s1Round;
            private readonly int roundMilliseconds;
            private readonly int s2Round;
            private readonly double s3Scale;

            /// <summary>
            /// 单次滚动得到的页面
            /// </summary>
            /// <param name="ImageRegion">供枚举输出的队列</param>
            /// <param name="AntiRecycling">为了防止Grid的页面元素自动回收复用技术导致item高亮干扰，每次滚动后记录靠近下方的一个item，在下次滚动前主动点击该item</param>
            private record Page(Queue<ImageRegion> ImageRegions, Rect? AntiRecycling);
            private Page? currentPage;
            private ImageRegion current;
            ImageRegion IAsyncEnumerator<ImageRegion>.Current => current;

            /// <summary>
            /// 滚动操作枚举器
            /// </summary>
            /// <param name="roi"></param>
            /// <param name="columns">有几列</param>
            /// <param name="s1Round">测试是否能滚动时发出的滚动命令次数</param>
            /// <param name="roundMilliseconds">滚动命令间隔毫秒</param>
            /// <param name="s2Round">滚过一整页时发出的滚动命令次数</param>
            /// <param name="s3Scale">微调滚动时控制首行距离上边界的参数</param>
            /// <param name="logger"></param>
            /// <param name="input"></param>
            /// <param name="ct"></param>
            public GridEnumerator(Rect roi, int columns, int s1Round, int roundMilliseconds, int s2Round, double s3Scale, ILogger logger, InputSimulator input, CancellationToken ct)
            {
                this.roi = roi;
                this.ct = ct;
                this.logger = logger;
                this.input = input;
                this.columns = columns;
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
            /// <param name="upperThreshold">上限用于抵消微小的其他差异，比如高亮图标的呼吸闪烁</param>
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
                //logger?.LogInformation($"response={response:F3}, shift=({shift.X:F2}, {shift.Y:F2})");
                return response > lowerThreshold && response < upperThreshold;
            }

            /// <summary>
            /// 将图标按Y轴高度简单地进行聚簇，避免因微小差异而乱序
            /// 已知每行的图标之间的Y不会差得太多
            /// </summary>
            /// <param name="numbers">传入的Y列表</param>
            /// <param name="threshold"></param>
            /// <returns>外层是各行从上到下，内层是一行从左到右</returns>
            static List<List<ImageRegion>> ClusterRows(IEnumerable<ImageRegion> regions, int threshold)
            {
                // 先对Y排序，便于聚簇
                var sortedRegions = regions.OrderBy(r => r.Y).ToList();

                List<List<ImageRegion>> clusters = new List<List<ImageRegion>>();

                if (sortedRegions.Count == 0)
                    return clusters;

                // 初始化第一个聚簇
                List<ImageRegion> currentCluster = new List<ImageRegion> { };

                foreach (ImageRegion r in sortedRegions)
                {
                    if (currentCluster.Count <= 0)
                    {
                        currentCluster.Add(r);
                        continue;
                    }

                    ImageRegion lastInCluster = currentCluster.Last();

                    // 如果当前数字与聚簇中最后一个数字的差值小于阈值，则加入当前聚簇
                    if (r.Y - lastInCluster.Y <= threshold)
                    {
                        currentCluster.Add(r);
                    }
                    else
                    {
                        // 否则，创建一个新的聚簇
                        clusters.Add(currentCluster.OrderBy(r => r.X).ToList());
                        currentCluster = new List<ImageRegion> { r };
                    }
                }

                // 添加最后一个聚簇
                clusters.Add(currentCluster.OrderBy(r => r.X).ToList());

                return clusters;
            }

            /// <summary>
            /// 返回未经排序的所有GridItem
            /// </summary>
            /// <param name="src"></param>
            /// <param name="columns"></param>
            /// <param name="findContoursAlpha"></param>
            /// <returns></returns>
            public static IEnumerable<Rect> GetGridItems(Mat src, int columns, bool findContoursAlpha = false)
            {
                Point[][] contours = findContoursAlpha ? FindContoursAlpha(src) : FindContours(src);

                IEnumerable<Point[]> Crop()
                {
                    foreach (var contour in contours)
                    {
                        Rect rect = Cv2.BoundingRect(contour);

                        // 把右上角的点去掉
                        var topRightPoints = contour.Where(p => (p.X - rect.X) > (rect.Width * 0.60) && (p.Y - rect.Y) < (rect.Height * 0.28));

                        yield return contour.Except(topRightPoints).ToArray();
                    }
                }

                contours = Crop().ToArray();

                //foreach (var c in contours)
                //{
                //    RotatedRect rect = Cv2.MinAreaRect(c);
                //    Point2f[] rectPoints = rect.Points();
                //    Point[] rectPointsInt = Array.ConvertAll(rectPoints, p => new Point((int)p.X, (int)p.Y));
                //    // 在图像上绘制最小外接旋转矩形
                //    for (int i = 0; i < 4; i++)
                //    {
                //        Cv2.Line(src, rectPointsInt[i], rectPointsInt[(i + 1) % 4], Scalar.Pink, 2);
                //    }
                //}

                contours = contours
                    .Where(c =>
                    {
                        Rect r = Cv2.BoundingRect(c);
                        if (r.Width < src.Width / columns * 0.66)   // 剔除太小的
                        {
                            return false;
                        }
                        if (r.Height == 0)
                        {
                            return false;
                        }
                        return Math.Abs((float)r.Width / r.Height - 0.8) < 0.05; // 按形状筛选
                    }).ToArray();

                IEnumerable<Rect> boxes = contours.Select(Cv2.BoundingRect);

                //if (boxes.Count() != 32)
                //{
                //    src.DrawContours(contours, -1, Scalar.Red);
                //    foreach (Rect box in boxes)
                //    {
                //        Cv2.Rectangle(src, box.TopLeft, box.BottomRight, Scalar.AliceBlue);
                //    }
                //    Cv2.ImShow("src", src);
                //    Cv2.WaitKey();
                //    Cv2.DestroyAllWindows();
                //}

                return boxes;
            }

            /// <summary>
            /// 像“分解圣遗物”界面的背景是纯色的，用简单的算法就能提取轮廓
            /// </summary>
            /// <param name="src"></param>
            /// <returns></returns>
            public static Point[][] FindContours(Mat src)
            {
                using Mat grey = src.CvtColor(ColorConversionCodes.BGR2GRAY);
                //Cv2.ImShow("grey", grey);

                using Mat canny = grey.Canny(20, 40);
                //Cv2.ImShow("canny", canny);
                //Cv2.WaitKey();

                //闭运算把一些断裂的边缘粘合一下
                //局限：提纳里的耳朵太长了一直连到了正上方的另一个图标，这里闭运算就会把最后一丝空隙也连起来，仅凭亮度边缘无法分隔轮廓……
                //todo：使用头像识别，先行去掉头像
                using Mat closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
                using Mat close = canny.MorphologyEx(MorphTypes.Close, closeKernel);
                //Cv2.ImShow("close", close);
                //Cv2.WaitKey();

                Cv2.FindContours(close, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone, null);
                return contours;
            }

            /// <summary>
            /// 背包界面的背景是把打开界面之前的画面进行了模糊+黑白渐变滤镜+左上角水印叠加处理
            /// 放任五彩斑斓的输入，并且允许点击高亮的话处理起来就复杂了
            /// 所以这个Alpha版方法留在这里只是想说明：
            /// 越是琢磨算法，就越会发现传统算法的能力是有极限的
            /// 既然是游戏画面，不如在输入的时候就尽量获得没有噪声的画面
            /// </summary>
            /// <param name="src"></param>
            /// <returns></returns>
            public static Point[][] FindContoursAlpha(Mat src)
            {
                Point[][] contours;
                void getLine(Mat edge, Scalar color)
                {
                    using Mat threshold = edge.Threshold(30, 255, ThresholdTypes.Binary);
                    LineSegmentPoint[] lines = threshold.HoughLinesP(1, (Cv2.PI / 180) / 4, 100, maxLineGap: 3);
                    lines = lines.Where(l => (Math.Abs(l.P1.X - l.P2.X) == 0) || (Math.Abs(l.P1.Y - l.P2.Y) == 0)).ToArray();
                    foreach (var line in lines)
                    {
                        Cv2.Line(src, line.P1, line.P2, color, 1);
                    }
                }

                Mat Laplacian(Mat src)
                {
                    //拉普拉斯算子
                    //Canny的sobel算子太偏向于横平竖直，而在噪声干扰太厉害的地方会产生分叉
                    using Mat laplacian = src.Laplacian(MatType.CV_64F, ksize: 3);
                    Mat result = laplacian.ConvertScaleAbs();
                    return result;
                }

                using Mat edge = new Mat(src.Size(), MatType.CV_8UC1);
                edge.SetTo(0);

                //除了明度，饱和度也纳入考虑
                //另外色度带来的噪声实在太多了所以不用，但其实有些地方色度的边缘比另两个维度的边缘好得多
                using Mat hsv = src.CvtColor(ColorConversionCodes.BGR2HSV);

                using Mat satChannel = hsv.ExtractChannel(1);
                //Cv2.ImShow("satChannel", satChannel);
                using Mat satEdge = Laplacian(satChannel);
                Cv2.BitwiseOr(satEdge, edge, edge);
                //Cv2.ImShow("satEdge", satEdge);
                //getLine(satEdge, Scalar.Red);

                using Mat valChannel = hsv.ExtractChannel(2);
                //Cv2.ImShow("valChannel", valChannel);
                using Mat valEdge = Laplacian(valChannel);
                Cv2.BitwiseOr(valEdge, edge, edge);
                //Cv2.ImShow("valEdge", valEdge);
                //getLine(valEdge, Scalar.Lime);

                //Cv2.WaitKey();
                //Cv2.ImShow("edge", edge);
                //Cv2.WaitKey();

                //高斯模糊方便去噪点
                //但毕竟是模糊，轮廓会被扩大，并且很难说是均匀的扩大
                using Mat blurred = edge.GaussianBlur(new Size(3, 3), 0.5);
                //Cv2.ImShow("blurred", blurred);
                //Cv2.WaitKey();

                //合并明度饱和度的边缘后再二值化
                Mat threshold = blurred.Threshold(50, 255, ThresholdTypes.Binary);
                //Cv2.ImShow("threshold", threshold);
                //Cv2.WaitKey();

                /*
                 * 如果不用高斯模糊去噪点，自己搞一些形态学操作也行
                 * 有些边缘会比高斯效果好
                //把太小的轮廓丢掉
                //Cv2.FindContours(threshold, out contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple, null);
                contours = contours.Where(c =>
                {
                    Rect rect = Cv2.BoundingRect(c);
                    if ((rect.Width > 10) || (rect.Height > 10))
                    {
                        return true;
                    }
                    return false;
                }).ToArray();
                threshold.SetTo(0);
                threshold.DrawContours(contours, -1, Scalar.White, thickness: 1);
                Cv2.ImShow("threshold", threshold);
                Cv2.WaitKey();

                //闭运算把一些断裂的边缘粘合一下
                using Mat closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
                using Mat close = threshold.MorphologyEx(MorphTypes.Close, closeKernel);
                Cv2.ImShow("close", close);
                Cv2.WaitKey();

                //因为后面要做的开运算会把毛刺给去掉，但太细的边缘会被一起腐蚀掉，所以查找并填充一下轮廓
                Cv2.FindContours(close, out contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple, null);
                close.DrawContours(contours, -1, Scalar.White, thickness: -1);
                Cv2.ImShow("close", close);
                Cv2.WaitKey();

                //开运算去毛刺
                using Mat openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
                using Mat open = close.MorphologyEx(MorphTypes.Open, openKernel);
                Cv2.ImShow("open", open);
                Cv2.WaitKey();
                */

                //得到有噪点的边缘
                Cv2.FindContours(threshold, out contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone, null);

                return contours;
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                if (this.currentPage == null || this.currentPage.ImageRegions.Count < 1)
                {
                    if (this.currentPage != null)
                    {
                        if (this.currentPage.AntiRecycling.HasValue)
                        {
                            using DesktopRegion desktop = new DesktopRegion(this.input.Mouse);
                            var (x, y, w, h) = (this.currentPage.AntiRecycling.Value.X, this.currentPage.AntiRecycling.Value.Y, this.currentPage.AntiRecycling.Value.Width, this.currentPage.AntiRecycling.Value.Height);
                            var (gcX, gcY) = (TaskContext.Instance().SystemInfo.CaptureAreaRect.X, TaskContext.Instance().SystemInfo.CaptureAreaRect.Y);
                            desktop.ClickTo(gcX + this.roi.X + x + (w / 2d), gcY + this.roi.Y + y + (h / 2d));
                            await TaskControl.Delay(500, ct);
                            desktop.ClickTo(gcX + this.roi.X + x + (w / 2d), gcY + this.roi.Y + y + (h / 2d));
                            await TaskControl.Delay(500, ct);
                        }

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
                                IEnumerable<Rect> gridItems2 = GetGridItems(grid2.SrcMat, this.columns);
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

                    IEnumerable<ImageRegion> gridItems;
                    if (this.currentPage == null)
                    {
                        // 第一页采集时，主动操作来避免图标高亮
                        // 双击第四列，采集第一、二列
                        using DesktopRegion desktop = new DesktopRegion(this.input.Mouse);
                        var (gcX, gcY) = (TaskContext.Instance().SystemInfo.CaptureAreaRect.X, TaskContext.Instance().SystemInfo.CaptureAreaRect.Y);
                        desktop.ClickTo(gcX + this.roi.X + this.roi.Width * 3.5 / this.columns, gcY + this.roi.Y + this.roi.Width * 0.5 / this.columns);
                        await TaskControl.Delay(500, ct);
                        desktop.ClickTo(gcX + this.roi.X + this.roi.Width * 3.5 / this.columns, gcY + this.roi.Y + this.roi.Width * 0.5 / this.columns);
                        await TaskControl.Delay(500, ct);

                        using ImageRegion ra12 = TaskControl.CaptureToRectArea();
                        using ImageRegion imageRegion12 = ra12.DeriveCrop(this.roi);
                        using Mat columns12 = new Mat(imageRegion12.SrcMat, new Rect(0, 0, (int)(this.roi.Width * 2.5 / this.columns), this.roi.Height));
                        IEnumerable<Rect> columns12Items = GetGridItems(columns12, 2);
                        // 双击第一列，采集第三列以后的列
                        desktop.ClickTo(gcX + this.roi.X + this.roi.Width * 0.5 / this.columns, gcY + this.roi.Y + this.roi.Width * 0.5 / this.columns);
                        await TaskControl.Delay(500, ct);
                        desktop.ClickTo(gcX + this.roi.X + this.roi.Width * 0.5 / this.columns, gcY + this.roi.Y + this.roi.Width * 0.5 / this.columns);
                        await TaskControl.Delay(500, ct);

                        using ImageRegion raRest = TaskControl.CaptureToRectArea();
                        using ImageRegion imageRegionRest = raRest.DeriveCrop(this.roi);
                        int restStartX = (int)(this.roi.Width * 1.5 / this.columns);
                        using Mat columnsRest = new Mat(imageRegionRest.SrcMat, new Rect(restStartX, 0, this.roi.Width - restStartX, this.roi.Height));
                        IEnumerable<Rect> columnsRestItems = GetGridItems(columnsRest, this.columns - 2).Select(r => new Rect(r.X + restStartX, r.Y, r.Width, r.Height));

                        gridItems = columns12Items.Select(imageRegion12.DeriveCrop).Union(columnsRestItems.Select(imageRegionRest.DeriveCrop)).ToArray();
                    }
                    else
                    {
                        using ImageRegion ra = TaskControl.CaptureToRectArea();
                        using ImageRegion imageRegion = ra.DeriveCrop(this.roi);
                        gridItems = GetGridItems(imageRegion.SrcMat, this.columns).Select(imageRegion.DeriveCrop);
                    }

                    List<List<ImageRegion>> clusterRows = ClusterRows(gridItems, (int)(0.025 * this.roi.Height));
                    this.currentPage = new Page(new Queue<ImageRegion>(clusterRows.SelectMany(r => r)), clusterRows.Reverse<List<ImageRegion>>().Skip(1)?.FirstOrDefault()?.FirstOrDefault()?.ToRect());

                    //foreach (Rect item in gridItems.Select(r => r.ToRect()))
                    //{
                    //    imageRegion.DrawRect(item, item.GetHashCode().ToString(), new System.Drawing.Pen(System.Drawing.Color.Lime));
                    //}
                }

                this.current = this.currentPage.ImageRegions.Dequeue();
                return true;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
