using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoFight.Model
{
    /// <summary>
    /// 导出数据格式。
    /// </summary>
    public class FeatureScorerExportData
    {
        public List<FeatureScorerItem> Features { get; set; } = new();
    }

    public class FeatureScorerItem
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public string Type { get; set; } = "";  // "F1" 或 "F2"
        public string Channel { get; set; } = "";  // "H", "S", "V"
        public bool IsCircular { get; set; }
        public double Range { get; set; }
        public double RefVal { get; set; }
        public List<double>? RefHist { get; set; }  // F2 8-bin 参考直方图
        public double Weight { get; set; }
        public List<double> ProbTable { get; set; } = new();
    }

    public static class ImageFeatureScorer
    {
        /// <summary>
        /// 对截图进行评估，返回属于目标分组的概率 [0, 1]。
        /// </summary>
        /// <param name="data">特征模型数据</param>
        /// <param name="capture">截图图像（BGR 格式 Mat，1080p 基准）</param>
        public static double Score(FeatureScorerExportData data, Mat capture)
        {
            if (data.Features == null || data.Features.Count == 0)
                return 0.5;

            double weightedSum = 0;
            double totalWeight = 0;

            foreach (var feature in data.Features)
            {
                double prob = ScoreOneFeature(capture, feature);
                weightedSum += prob * feature.Weight;
                totalWeight += feature.Weight;
            }

            return totalWeight > 0
                ? Math.Max(0, Math.Min(1, weightedSum / totalWeight))
                : 0.5;
        }

        /// <summary>
        /// 对单个特征计算概率 [0, 1]。
        /// F1 裁剪 BGR→HSV 提取通道均值；F2 裁剪 BGR→HSV→单通道 Sobel 梯度直方图。
        /// </summary>
        private static double ScoreOneFeature(Mat capture, FeatureScorerItem feature)
        {
            if (feature.W <= 0 || feature.H <= 0) return 0.5;

            int fx = Math.Clamp(feature.X, 0, capture.Width - 1);
            int fy = Math.Clamp(feature.Y, 0, capture.Height - 1);
            int fw = Math.Min(feature.W, capture.Width - fx);
            int fh = Math.Min(feature.H, capture.Height - fy);
            if (fw <= 0 || fh <= 0) return 0.5;

            double match;

            if (feature.Type == "F1")
            {
                // F1: 裁剪特征块 → HSV → 提取通道均值
                using var bgrPatch = new Mat(capture, new OpenCvSharp.Rect(fx, fy, fw, fh));
                using var hsvPatch = new Mat();
                Cv2.CvtColor(bgrPatch, hsvPatch, ColorConversionCodes.BGR2HSV);

                int chanIdx = feature.Channel switch { "H" => 0, "S" => 1, "V" => 2, _ => -1 };
                if (chanIdx < 0) return 0.5;

                var flat = GetChannelPixels(hsvPatch, chanIdx);
                if (flat.Length == 0) return 0.5;

                double val;
                if (feature.Channel == "H")
                {
                    var hVals = flat.Select(v => v * 2.0).ToArray();
                    val = CircularMean(hVals);
                }
                else
                {
                    var svVals = flat.Select(v => v / 255.0).ToArray();
                    val = svVals.Average();
                }
                match = IsCircularSimilarity(val, feature.RefVal, feature.IsCircular, feature.Range);
            }
            else if (feature.Type == "F2")
            {
                // F2: 裁剪特征块 → HSV → 提取单通道 → Sobel 梯度
                if (feature.RefHist == null || feature.RefHist.Count != 8) return 0.5;

                int chanIdx = feature.Channel switch { "H" => 0, "S" => 1, "V" => 2, _ => -1 };
                if (chanIdx < 0) return 0.5;

                int pad = 1;
                int cropX = Math.Max(0, fx - pad);
                int cropY = Math.Max(0, fy - pad);
                int cropW = Math.Min(fw + 2 * pad, capture.Width - cropX);
                int cropH = Math.Min(fh + 2 * pad, capture.Height - cropY);
                int innerX = fx - cropX;
                int innerY = fy - cropY;

                using var crop = new Mat(capture, new OpenCvSharp.Rect(cropX, cropY, cropW, cropH));
                using var hsvCrop = new Mat();
                Cv2.CvtColor(crop, hsvCrop, ColorConversionCodes.BGR2HSV);
                using var singleChan = new Mat();
                Cv2.ExtractChannel(hsvCrop, singleChan, chanIdx);

                using var gradX = new Mat();
                using var gradY = new Mat();
                Cv2.Sobel(singleChan, gradX, MatType.CV_32F, 1, 0, 3);
                Cv2.Sobel(singleChan, gradY, MatType.CV_32F, 0, 1, 3);
                using var mag = new Mat();
                using var ang = new Mat();
                Cv2.CartToPolar(gradX, gradY, mag, ang, angleInDegrees: true);

                var dirs = new List<double>();
                var mags = new List<double>();
                for (int py = innerY; py < innerY + fh; py++)
                    for (int px = innerX; px < innerX + fw; px++)
                    {
                        float a = ang.At<float>(py, px);
                        float m = mag.At<float>(py, px);
                        if (!float.IsNaN(a) && !float.IsInfinity(a) && !float.IsNaN(m))
                        {
                            dirs.Add(a % 180.0f);
                            mags.Add(m);
                        }
                    }
                if (dirs.Count < 4) return 0.5;

                var hist = Compute8BinHistogram(dirs.ToArray(), mags.ToArray());
                match = CosineSimilarity(hist, feature.RefHist.ToArray());
            }
            else
            {
                return 0.5;
            }

            return LookupProbability(match, feature.ProbTable);
        }

        #region ======== 内部方法 ========

        /// <summary>提取 Mat 指定通道的全部像素值（byte）</summary>
        private static byte[] GetChannelPixels(Mat mat, int channelIndex)
        {
            using var single = new Mat();
            Cv2.ExtractChannel(mat, single, channelIndex);
            long total = single.Total();
            var result = new byte[total];
            System.Runtime.InteropServices.Marshal.Copy(single.Data, result, 0, (int)total);
            return result;
        }

        /// <summary>环形均值（角度制）</summary>
        private static double CircularMean(double[] anglesDeg)
        {
            double sumSin = 0, sumCos = 0;
            int n = 0;
            foreach (var a in anglesDeg)
            {
                if (double.IsNaN(a)) continue;
                double rad = a * Math.PI / 180.0;
                sumSin += Math.Sin(rad);
                sumCos += Math.Cos(rad);
                n++;
            }
            if (n == 0) return double.NaN;
            return (Math.Atan2(sumSin / n, sumCos / n) * 180.0 / Math.PI + 360) % 360;
        }

        /// <summary>计算相似度 [0, 1]</summary>
        private static double IsCircularSimilarity(double a, double b, bool isCircular, double range)
        {
            if (isCircular)
            {
                double diff = Math.Abs((a - b + 360) % 360);
                if (diff > 180) diff = 360 - diff;
                return (Math.Cos(diff * Math.PI / 180.0) + 1) / 2;
            }
            else
            {
                double diff = Math.Abs(a - b);
                return Math.Max(0, 1 - diff / range);
            }
        }

        /// <summary>从 probTable 线性插值（步长 0.05，共 21 点）</summary>
        private static double LookupProbability(double match, List<double> probTable)
        {
            if (probTable == null || probTable.Count == 0) return 0.5;
            int n = probTable.Count;
            double index = Math.Clamp(match / 0.05, 0, n - 1);
            int low = (int)index;
            int high = Math.Min(low + 1, n - 1);
            double frac = index - low;
            return probTable[low] * (1 - frac) + probTable[high] * frac;
        }

        /// <summary>8-bin 幅值加权梯度直方图（每 bin 角度范围 22.5°）</summary>
        private static double[] Compute8BinHistogram(double[] anglesDeg, double[] magnitudes)
        {
            var hist = new double[8];
            for (int i = 0; i < anglesDeg.Length; i++)
            {
                double ang = anglesDeg[i];
                double mag = i < magnitudes.Length ? magnitudes[i] : 1.0;
                int bin = (int)(ang / 22.5);
                bin = Math.Clamp(bin, 0, 7);
                hist[bin] += mag;
            }
            double sum = hist.Sum();
            if (sum > 0)
                for (int i = 0; i < 8; i++) hist[i] /= sum;
            return hist;
        }

        /// <summary>余弦相似度 [0, 1]</summary>
        private static double CosineSimilarity(double[] a, double[] b)
        {
            if (a.Length != b.Length || a.Length == 0) return 0;
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            double denom = Math.Sqrt(na) * Math.Sqrt(nb);
            return denom > 1e-10 ? Math.Max(0, Math.Min(1, dot / denom)) : 0;
        }

        #endregion
    }
}
