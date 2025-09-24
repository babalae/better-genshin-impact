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
    /// <summary>
    /// Grid界面垂直滚动服务类
    /// </summary>
    public class GridScroller
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

        internal GridScroller(GridParams @params, ILogger logger, InputSimulator input, CancellationToken ct)
        {
            this.roi = @params.Roi;
            this.ct = ct;
            this.logger = logger;
            this.input = input;
            this.columns = @params.Columns;
            this.s1Round = @params.S1Round;
            this.roundMilliseconds = @params.RoundMilliseconds;
            this.s2Round = @params.S2Round;
            this.s3Scale = @params.S3Scale;
        }

        internal async Task<bool> TryVerticalScollDown(Func<Mat, int, IEnumerable<Rect>> GetGridItems)
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

            if (isScrolling)
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
                    using var ra4 = TaskControl.CaptureToRectArea();
                    using ImageRegion grid2 = ra4.DeriveCrop(this.roi);
                    IEnumerable<Rect> gridItems2 = GetGridItems(grid2.SrcMat, this.columns);
                    if (gridItems2.Min(i => i.Y) > (ra4.Width * this.s3Scale))  // 最后精细滚动，保证完整地显示最多行
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
                return true;
            }
            else
            {
                await TaskControl.Delay(300, ct);
                this.logger.LogInformation("滚动到底部了");
                return false;
            }
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
            shift = Cv2.PhaseCorrelate(prev, next, window, out double response);
            // 相位相关性
            //logger?.LogInformation($"response={response:F3}, shift=({shift.X:F2}, {shift.Y:F2})");
            return response > lowerThreshold && response < upperThreshold;
        }
    }
}
