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

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    public class GridScreen : IAsyncEnumerable<Tuple<ImageRegion, Rect>>
    {
        private readonly GridParams @params;
        private readonly CancellationToken ct;
        private readonly ILogger logger;
        private readonly InputSimulator input = Simulation.SendInput;
        internal Action? OnBeforeScroll { get; set; }
        internal Action<Tuple<ImageRegion, IEnumerable<Tuple<Rect, bool>>>>? OnAfterTurnToNewPage { get; set; }

        /// <summary>
        /// 提供一个默认的绘制页面上所有识别出的项目的行为
        /// </summary>
        internal static readonly Action<Tuple<ImageRegion, IEnumerable<Tuple<Rect, bool>>>> DrawItemsAfterTurnToNewPage = data =>
        {
            (ImageRegion page, var items) = data;
            foreach ((Rect rect, bool isPhantom) in items)
            {
                using ImageRegion item = page.DeriveCrop(rect);
                item.DrawSelf($"GridItem{item.GetHashCode()}", isPhantom ? System.Drawing.Pens.Yellow : System.Drawing.Pens.Lime);
            }
        };

        /// <summary>
        /// 对Gird类型界面的操作封装类
        /// 直接对此类对象进行遍历即可获取所有项
        /// 每次的截图是上次滚动后的，如果实时性要求高，应每次迭代自行截图
        /// 在末页可能重复返回GridItem，须自行处理
        /// </summary>
        /// <param name="@params"></param>
        /// <param name="logger"></param>
        /// <param name="ct"></param>
        public GridScreen(GridParams @params, ILogger logger, CancellationToken ct)
        {
            this.ct = ct;
            this.logger = logger;
            if (@params.Columns < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(@params.Columns));
            }
            this.@params = @params;
        }

        public IAsyncEnumerator<Tuple<ImageRegion, Rect>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new GridEnumerator(this, @params.Roi, @params.Columns, input, new GridScroller(@params, logger, input, ct), ct);
        }

        public class GridEnumerator : IAsyncEnumerator<Tuple<ImageRegion, Rect>>
        {
            private readonly GridScreen owner;
            private readonly Rect roi;
            private readonly CancellationToken ct;
            private readonly InputSimulator input = Simulation.SendInput;
            private readonly int columns;
            private readonly GridScroller gridScroller;

            /// <summary>
            /// 单次滚动得到的页面
            /// </summary>
            /// <param name="ImageRegion">供枚举输出的队列</param>
            /// <param name="AntiRecycling">为了防止Grid的页面元素自动回收复用技术导致item高亮干扰，每次滚动后记录靠近下方的一个item，在下次滚动前主动点击该item</param>
            private record Page(ImageRegion PageRegion, Queue<Rect> ItemRects, Rect? AntiRecycling);
            private Page? currentPage;
            private Tuple<ImageRegion, Rect>? current;
            Tuple<ImageRegion, Rect> IAsyncEnumerator<Tuple<ImageRegion, Rect>>.Current => current ?? throw new NullReferenceException();

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
            internal GridEnumerator(GridScreen owner, Rect roi, int columns, InputSimulator input, GridScroller gridScroller, CancellationToken ct)
            {
                this.owner = owner;
                this.roi = roi;
                this.ct = ct;
                this.input = input;
                this.columns = columns;
                this.gridScroller = gridScroller;
            }

            /// <summary>
            /// 纯cv方法获取
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
                        return Math.Abs((float)r.Width / r.Height - 0.81) < 0.03; // 按形状筛选
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

            /*
             * hutaofisher给的划线算法参数，对网格划分效果似乎较好，待应用
                gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
                canny = cv2.Canny(gray, 25, 50)
                hough = cv2.HoughLinesP(canny, 1, np.pi / 180, threshold=500, minLineLength=200, maxLineGap=400)
             */

            /// <summary>
            /// 背包界面的背景是把打开界面之前的画面进行了模糊+黑白渐变滤镜+左上角水印叠加处理
            /// 放任五彩斑斓的输入，并且允许点击高亮的话处理起来就复杂了
            /// <para>所以这个Alpha版方法留在这里只是想说明：
            /// 越是琢磨算法，就越会发现传统算法的能力是有极限的
            /// 既然是游戏画面，不如在输入的时候就尽量获得没有噪声的画面</para>
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

            /// <summary>
            /// 把Rects结果聚簇成Cells，并进行优化
            /// </summary>
            /// <param name="mat"></param>
            /// <param name="rects"></param>
            /// <param name="threshold"></param>
            /// <returns></returns>
            public static IEnumerable<GridCell> PostProcess(Mat mat, IEnumerable<Rect> rects, int threshold)
            {
                if (!rects.Any())
                {
                    return [];
                }
                // 根据聚簇结果补漏……
                List<GridCell> cells = GridCell.ClusterToCells(rects, threshold).ToList();
                GridCell.FillMissingGridCells(ref cells);

                // 在末尾处有可能补多了，把底部颜色不符的丢掉……  // PS：群友有直接用底部颜色进行识别的，效果不错
                var result = cells.ToList();
                foreach (var cell in cells.Where(c => c.IsPhantom))
                {
                    // 幻影格子由插值生成，低分辨率下可能坐标越界，直接丢弃
                    if (cell.Rect.X < 0 || cell.Rect.Y < 0 ||
                        cell.Rect.X + cell.Rect.Width > mat.Cols ||
                        cell.Rect.Y + cell.Rect.Height > mat.Rows)
                    {
                        result.Remove(cell);
                        continue;
                    }
                    using Mat cellMat = mat.SubMat(cell.Rect);
                    using Mat bottom = cellMat.GetGridBottom();
                    if (!IsCorrectBottomColor(bottom))
                    {
                        result.Remove(cell);
                    }
                }

                return result;
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                if (this.currentPage == null || this.currentPage.ItemRects.Count < 1)
                {
                    ImageRegion? imageRegion = null;
                    try
                    {
                        if (this.currentPage != null)   // 当前页遍历完了就向下滚动
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

                            using var ra4 = TaskControl.CaptureToRectArea();
                            ra4.MoveTo(this.roi.X + this.roi.Width / 2, this.roi.Y + this.roi.Height / 2);
                            await TaskControl.Delay(300, ct);

                            owner.OnBeforeScroll?.Invoke();
                            if (!await this.gridScroller.TryVerticalScollDown((src, columns) => GetGridItems(src, columns)))
                            {
                                return false;
                            }

                            using ImageRegion ra = TaskControl.CaptureToRectArea();
                            imageRegion = ra.DeriveCrop(this.roi);
                        }
                        else
                        {
                            // 第一页采集时，主动操作来避免图标高亮
                            Rect rect12 = new Rect(0, 0, (int)(this.roi.Width * 1.5 / this.columns), this.roi.Height);
                            // 双击第三列，采集第一、二列
                            using DesktopRegion desktop = new DesktopRegion(this.input.Mouse);
                            var (gcX, gcY) = (TaskContext.Instance().SystemInfo.CaptureAreaRect.X, TaskContext.Instance().SystemInfo.CaptureAreaRect.Y);
                            desktop.ClickTo(gcX + this.roi.X + this.roi.Width * 2.5 / this.columns, gcY + this.roi.Y + this.roi.Width * 0.5 / this.columns);
                            await TaskControl.Delay(300, ct);
                            desktop.ClickTo(gcX + this.roi.X + this.roi.Width * 2.5 / this.columns, gcY + this.roi.Y + this.roi.Width * 0.5 / this.columns);
                            await TaskControl.Delay(500, ct);

                            using ImageRegion ra12 = TaskControl.CaptureToRectArea();
                            using ImageRegion imageRegion12 = ra12.DeriveCrop(this.roi);
                            using Mat columns12 = new Mat(imageRegion12.SrcMat, rect12);

                            // 双击第一列，采集第二列以后的列
                            desktop.ClickTo(gcX + this.roi.X + this.roi.Width * 0.5 / this.columns, gcY + this.roi.Y + this.roi.Width * 0.5 / this.columns);
                            await TaskControl.Delay(300, ct);
                            desktop.ClickTo(gcX + this.roi.X + this.roi.Width * 0.5 / this.columns, gcY + this.roi.Y + this.roi.Width * 0.5 / this.columns);
                            await TaskControl.Delay(500, ct);

                            using ImageRegion raRest = TaskControl.CaptureToRectArea();
                            imageRegion = raRest.DeriveCrop(this.roi);
                            using Mat subMat12 = imageRegion.SrcMat.SubMat(rect12);
                            columns12.CopyTo(subMat12); // 拼接两次的采集
                        }

                        var rects = GetGridItems(imageRegion.SrcMat, this.columns);
                        var cells = PostProcess(imageRegion.SrcMat, rects, (int)(0.025 * this.roi.Height));

                        if (!cells.Any())
                        {
                            imageRegion.Dispose();
                            return false;
                        }

                        this.currentPage?.PageRegion?.Dispose();
                        this.currentPage = new Page(imageRegion, new Queue<Rect>(cells.OrderBy(c => c.RowNum).ThenBy(c => c.ColNum).Select(c => c.Rect)),
                            cells.GroupBy(c => c.RowNum).OrderByDescending(g => g.Key).Skip(1)?.FirstOrDefault()?.OrderBy(c => c.ColNum)?.FirstOrDefault()?.Rect);

                        owner.OnAfterTurnToNewPage?.Invoke(Tuple.Create(imageRegion, cells.Select(c => Tuple.Create(c.Rect, c.IsPhantom))));
                    }
                    catch
                    {
                        imageRegion?.Dispose();
                        throw;
                    }
                }

                this.current = Tuple.Create(this.currentPage.PageRegion, this.currentPage.ItemRects.Dequeue());
                return true;
            }

            /// <summary>
            /// 使用均值比较颜色
            /// </summary>
            public static bool IsCorrectBottomColor(Mat image, int tolerance = 30)
            {
                if (image.Empty())
                    throw new ArgumentException("输入图像为空");

                Scalar bgrColor = new Scalar(0xdc, 0xe5, 0xe9);

                // 计算区域的平均颜色
                Scalar meanColor = Cv2.Mean(image);

                // 计算平均颜色与目标颜色的差异
                double diff = Math.Abs(meanColor.Val0 - bgrColor.Val0) +
                             Math.Abs(meanColor.Val1 - bgrColor.Val1) +
                             Math.Abs(meanColor.Val2 - bgrColor.Val2);

                return diff <= tolerance * 3;
            }

            public ValueTask DisposeAsync()
            {
                this.currentPage?.PageRegion?.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
