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
using TorchSharp.Modules;

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    public class ArtifactSetFilterScreen : IAsyncEnumerable<Tuple<ImageRegion, Rect>>
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
        public IAsyncEnumerator<Tuple<ImageRegion, Rect>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new GridEnumerator(this, @params.Roi, @params.Columns, new GridScroller(@params, logger, input, ct), ct);
        }

        public class GridEnumerator : IAsyncEnumerator<Tuple<ImageRegion, Rect>>
        {
            private readonly ArtifactSetFilterScreen owner;
            private readonly Rect roi;
            private readonly CancellationToken ct;
            private readonly int columns;
            private readonly GridScroller gridScroller;
            private record Page(ImageRegion PageRegion, Queue<Rect> ItemRects);
            private Page? currentPage;
            private Tuple<ImageRegion, Rect>? current;
            Tuple<ImageRegion, Rect> IAsyncEnumerator<Tuple<ImageRegion, Rect>>.Current => current ?? throw new NullReferenceException();

            internal GridEnumerator(ArtifactSetFilterScreen owner, Rect roi, int columns, GridScroller gridScroller, CancellationToken ct)
            {
                this.owner = owner;
                this.roi = roi;
                this.ct = ct;
                this.columns = columns;
                this.gridScroller = gridScroller;
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                if (this.currentPage == null || this.currentPage.ItemRects.Count < 1)
                {
                    if (this.currentPage != null)
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

                    ImageRegion imageRegion = ra.DeriveCrop(this.roi);
                    try
                    {
                        IEnumerable<Rect> gridRects = GetGridItems(imageRegion.SrcMat, this.columns);

                        if (!gridRects.Any())
                        {
                            imageRegion.Dispose();
                            return false;
                        }

                        this.currentPage?.PageRegion?.Dispose();
                        this.currentPage = new Page(imageRegion, new Queue<Rect>(gridRects));
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

            public ValueTask DisposeAsync()
            {
                this.currentPage?.PageRegion?.Dispose();
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

            List<GridCell> cells = GridCell.ClusterToCells(boxes, 10).ToList();

            GridCell.FillMissingGridCells(ref cells);

            return cells.OrderBy(c => c.RowNum).ThenBy(c => c.ColNum).Select(c => c.Rect).ToArray();
        }
    }
}
