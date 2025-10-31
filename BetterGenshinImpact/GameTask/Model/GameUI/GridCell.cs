using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    /// <summary>
    /// 具有行号列号的单元格
    /// ColNum和RowNum也是0-based的
    /// 不仅方便编程，ClusterToCells方法也需要一个引用类型
    /// </summary>
    /// <param name="rect"></param>
    public class GridCell(Rect rect)
    {
        public Rect Rect = rect;
        public int ColNum;
        public int RowNum;
        /// <summary>
        /// 表示该单元格并非CV方法识别得到，而是通过算法推测出的
        /// </summary>
        public bool IsPhantom;

        /// <summary>
        /// 把检出的矩形聚簇成类似Excel的单元格集合
        /// </summary>
        /// <param name="rects"></param>
        /// <param name="threshold"></param>
        /// <returns></returns>
        public static IEnumerable<GridCell> ClusterToCells(IEnumerable<Rect> rects, int threshold)
        {
            return ClusterToCells(rects.Select(r => Tuple.Create(0, r)), threshold).Select(t => t.Item2).ToArray();
        }

        /// <summary>
        /// 把检出的矩形聚簇成类似Excel的单元格集合
        /// </summary>
        /// <param name="rects"></param>
        /// <param name="threshold"></param>
        /// <returns></returns>
        public static IEnumerable<Tuple<T, GridCell>> ClusterToCells<T>(IEnumerable<Tuple<T, Rect>> rects, int threshold)
        {
            if (!rects.Any())
            {
                return [];
            }

            var result = rects.Select(r => new Tuple<T, GridCell>(r.Item1, new GridCell(r.Item2)));
            result = result.ToArray();  // 必需，不然引用会丢掉。。

            var orderByX = result.OrderBy(t => t.Item2.Rect.Left).ToArray();
            int col = 0;
            int? lastX = null;
            int avgWidth = (int)rects.Average(r => r.Item2.Width);
            for (int i = 0; i < orderByX.Length; i++)
            {
                if (lastX != null && orderByX[i].Item2.Rect.X - lastX > threshold)
                {
                    col += (int)Math.Round((float)(orderByX[i].Item2.Rect.X - lastX.Value) / (avgWidth + threshold));
                }
                orderByX[i].Item2.ColNum = col;
                lastX = orderByX[i].Item2.Rect.X;
            }

            var orderByY = result.OrderBy(t => t.Item2.Rect.Top).ToArray();
            int row = 0;
            int? lastY = null;
            int avgHeight = (int)rects.Average(r => r.Item2.Height);
            for (int i = 0; i < orderByY.Length; i++)
            {
                if (lastY != null && orderByY[i].Item2.Rect.Y - lastY > threshold)
                {
                    row += (int)Math.Round((float)(orderByY[i].Item2.Rect.Y - lastY.Value) / (avgHeight + threshold));    // 估算隔了多少行
                }
                orderByY[i].Item2.RowNum = row;
                lastY = orderByY[i].Item2.Rect.Y;
            }

            return result;
        }

        /// <summary>
        /// 遍历方阵，补上缺的Cell
        /// </summary>
        /// <param name="cells"></param>
        public static void FillMissingGridCells(ref List<GridCell> cells)
        {
            if (cells.Count <= 0)
            {
                return;
            }

            double avgWidth = cells.Average(c => c.Rect.Width);
            double avgHeight = cells.Average(c => c.Rect.Height);
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
                avgColSpacing = count == 0 ? 0 : Math.Round(((double)sum) / count, MidpointRounding.AwayFromZero);
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
                avgRowSpace = count == 0 ? 0 : Math.Round(((double)sum) / count, MidpointRounding.AwayFromZero);
            }

            int avgLeft = (int)Math.Round(cells.Average(c => c.Rect.X - (avgWidth + avgColSpacing) * c.ColNum), MidpointRounding.AwayFromZero);
            int avgTop = (int)Math.Round(cells.Average(c => c.Rect.Y - (avgHeight + avgRowSpace) * c.RowNum), MidpointRounding.AwayFromZero);

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
                    GridCell cell = new GridCell(new Rect(x, y, width, height))
                    {
                        ColNum = i,
                        RowNum = j,
                        IsPhantom = true
                    };
                    cells.Add(cell);
                }
            }
        }
    }
}
