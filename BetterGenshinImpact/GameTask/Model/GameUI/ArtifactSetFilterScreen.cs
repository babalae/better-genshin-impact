using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    public class ArtifactSetFilterScreen : IAsyncEnumerable<ImageRegion>
    {
        private readonly GridParams @params;
        private readonly CancellationToken ct;
        private readonly ILogger logger;
        private readonly InputSimulator input = Simulation.SendInput;
        internal Action? OnBeforeScroll { get; set; }

        /// <summary>
        /// 对圣遗物套装筛选界面的操作封装类
        /// 直接对此类对象进行遍历即可获取所有项
        /// 每次的截图是上次滚动后的，如果实时性要求高，应每次迭代自行截图
        /// 在末页可能重复返回GridItem，须自行处理
        /// </summary>
        /// <param name="@params"></param>
        /// <param name="logger"></param>
        /// <param name="ct"></param>
        public ArtifactSetFilterScreen(GridParams @params, ILogger logger, CancellationToken ct)
        {
            this.ct = ct;
            this.logger = logger;
            this.@params = @params;
        }
        public IAsyncEnumerator<ImageRegion> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new GridEnumerator(this, @params.Roi, @params.Columns, new GridScroller(@params, logger, input, ct), ct);
        }

        public class GridEnumerator : IAsyncEnumerator<ImageRegion>
        {
            private readonly ArtifactSetFilterScreen owner;
            private readonly Rect roi;
            private readonly CancellationToken ct;
            private readonly int columns;
            private readonly GridScroller gridScroller;

            private Queue<ImageRegion> imageRegions;
            private ImageRegion? current;
            ImageRegion IAsyncEnumerator<ImageRegion>.Current => current ?? throw new NullReferenceException();

            internal GridEnumerator(ArtifactSetFilterScreen owner, Rect roi, int columns, GridScroller gridScroller, CancellationToken ct)
            {
                this.owner = owner;
                this.roi = roi;
                this.ct = ct;
                this.columns = columns;
                this.gridScroller = gridScroller;

                this.imageRegions = new Queue<ImageRegion>();
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                if (current == null || this.imageRegions.Count < 1)
                {
                    if (current != null)
                    {
                        using var ra4 = TaskControl.CaptureToRectArea();
                        ra4.MoveTo(this.roi.X + this.roi.Width / 2, this.roi.Y + this.roi.Height / 2);
                        await TaskControl.Delay(300, ct);

                        owner.OnBeforeScroll?.Invoke();
                        if (!await this.gridScroller.TryVerticalScollDown(GetGridItems))
                        {
                            return false;
                        }
                    }

                    using ImageRegion ra = TaskControl.CaptureToRectArea();
                    using ImageRegion imageRegion = ra.DeriveCrop(this.roi);
                    IEnumerable<ImageRegion> gridItems = GetGridItems(imageRegion.SrcMat, this.columns).Select(imageRegion.DeriveCrop);

                    this.imageRegions = new Queue<ImageRegion>(gridItems);
                }
                this.current = this.imageRegions.Dequeue();
                return true;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>
        /// 顺便把顺序也从左到右从上到下排好了
        /// </summary>
        /// <param name="src"></param>
        /// <param name="columns"></param>
        /// <returns></returns>
        public static IEnumerable<Rect> GetGridItems(Mat src, int columns)
        {
            using Mat grey = src.CvtColor(ColorConversionCodes.BGR2GRAY);
            using Mat canny = grey.Canny(20, 40);

            using Mat closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            using Mat close = canny.MorphologyEx(MorphTypes.Close, closeKernel);

            //Cv2.ImShow("grey", grey);
            //Cv2.ImShow("close", close);
            //Cv2.WaitKey();

            Cv2.FindContours(close, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple, null);

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
                        return Math.Abs((float)r.Width / r.Height - 8.63) < 0.4; // 按形状筛选
                    }).ToArray();

            IEnumerable<Rect> boxes = contours.Select(Cv2.BoundingRect);

            double avgWidth = boxes.Average(r => r.Width);
            double avgHeight = boxes.Average(r => r.Height);

            List<Cell> cells = ClusterToCells(boxes, 10).ToList();

            double avgColSpacing;
            double avgRowSpace;
            {
                int count = 0;
                int sum = 0;
                foreach (var row in cells.GroupBy(t => t.RowNum))
                {
                    for (int i = 0; i < row.Max(r => r.ColNum); i++)
                    {
                        var x1 = row.SingleOrDefault(r => r.ColNum == i);
                        var x2 = row.SingleOrDefault(r => r.ColNum == i + 1);
                        if (x1 == null || x2 == null)
                        {
                            continue;
                        }
                        sum += x2.Rect.X - x1.Rect.X - x1.Rect.Width;
                        count++;
                    }
                }
                avgColSpacing = Math.Round(((double)sum) / count, MidpointRounding.AwayFromZero);
            }
            {
                int count = 0;
                int sum = 0;
                foreach (var col in cells.GroupBy(t => t.ColNum))
                {
                    for (int i = 0; i < col.Max(r => r.RowNum); i++)
                    {
                        var y1 = col.SingleOrDefault(r => r.RowNum == i);
                        var y2 = col.SingleOrDefault(r => r.RowNum == i + 1);
                        if (y1 == null || y2 == null)
                        {
                            continue;
                        }
                        sum += y2.Rect.Y - y1.Rect.Y - y1.Rect.Height;
                        count++;
                    }
                }
                avgRowSpace = Math.Round(((double)sum) / count, MidpointRounding.AwayFromZero);
            }

            int avgLeft = (int)Math.Round(cells.Average(c => c.Rect.X - (avgWidth + avgColSpacing) * c.ColNum), MidpointRounding.AwayFromZero);
            int avgTop = (int)Math.Round(cells.Average(c => c.Rect.Y - (avgHeight + avgRowSpace) * c.RowNum), MidpointRounding.AwayFromZero);

            // 遍历方阵，补上缺的Cell
            for (int i = 0; i < cells.Max(r => r.ColNum) + 1; i++)
            {
                for (int j = 0; j < cells.Max(r => r.RowNum) + 1; j++)
                {
                    if (cells.Any(c => c.ColNum == i && c.RowNum == j))
                    {
                        continue;
                    }
                    int x = (int)Math.Round(avgLeft + (avgWidth + avgColSpacing) * i, MidpointRounding.AwayFromZero);
                    int y = (int)Math.Round(avgTop + (avgHeight + avgRowSpace) * j, MidpointRounding.AwayFromZero);
                    int width = (int)Math.Round(avgWidth, MidpointRounding.AwayFromZero);
                    int height = (int)Math.Round(avgHeight, MidpointRounding.AwayFromZero);
                    Cell cell = new Cell(new Rect(x, y, width, height));
                    cell.ColNum = i;
                    cell.RowNum = j;
                    cells.Add(cell);
                }
            }

            return cells.OrderBy(c => c.RowNum).ThenBy(c => c.ColNum).Select(c => c.Rect).ToArray();
        }

        /// <summary>
        /// 具有行号列号的单元格
        /// ColNum和RowNum也是0-based的
        /// 不仅方便编程，ClusterToCells方法也需要一个引用类型
        /// </summary>
        /// <param name="rect"></param>
        private class Cell(Rect rect)
        {
            internal Rect Rect = rect;
            internal int ColNum;
            internal int RowNum;
        }

        /// <summary>
        /// 把检出的矩形聚簇成类似Excel的单元格集合
        /// </summary>
        /// <param name="rects"></param>
        /// <param name="threshold"></param>
        /// <returns></returns>
        private static IEnumerable<Cell> ClusterToCells(IEnumerable<Rect> rects, int threshold)
        {
            var result = rects.Select(r => new Cell(r));
            result = result.ToArray();  // 必需，不然引用会丢掉。。

            var orderByX = result.OrderBy(t => t.Rect.Left).ToArray();
            int col = 0;
            int? lastX = null;
            int avgWidth = (int)rects.Average(r => r.Width);
            for (int i = 0; i < orderByX.Length; i++)
            {
                if (lastX != null && orderByX[i].Rect.X - lastX > threshold)
                {
                    col += (int)Math.Round((float)(orderByX[i].Rect.X - lastX.Value) / (avgWidth + threshold));
                }
                orderByX[i].ColNum = col;
                lastX = orderByX[i].Rect.X;
            }

            var orderByY = result.OrderBy(t => t.Rect.Top).ToArray();
            int row = 0;
            int? lastY = null;
            int avgHeight = (int)rects.Average(r => r.Height);
            for (int i = 0; i < orderByY.Length; i++)
            {
                if (lastY != null && orderByY[i].Rect.Y - lastY > threshold)
                {
                    row += (int)Math.Round((float)(orderByY[i].Rect.Y - lastY.Value) / (avgHeight + threshold));    // 估算隔了多少行
                }
                orderByY[i].RowNum = row;
                lastY = orderByY[i].Rect.Y;
            }

            return result;
        }
    }
}
