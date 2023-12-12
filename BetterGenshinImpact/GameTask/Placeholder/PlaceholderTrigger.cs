using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using System;
using System.Drawing;
using System.Linq;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Placeholder;

/// <summary>
/// 一个用于开发测试的识别、或者全局占位触发器
/// 这个触发器启动的时候，直接独占
/// </summary>
public class TestTrigger : ITaskTrigger
{
    public string Name => "自定义占位触发器";
    public bool IsEnabled { get; set; }
    public int Priority => 9999;
    public bool IsExclusive { get; private set; }

    private readonly Pen _pen = new(Color.Coral, 1);

    //private readonly AutoGeniusInvokationAssets _autoGeniusInvokationAssets;

    //private readonly YoloV8 _predictor = new(Global.Absolute("Assets\\Model\\Fish\\bgi_fish.onnx"));

    public TestTrigger()
    {
        var info = TaskContext.Instance().SystemInfo;
        //_autoGeniusInvokationAssets = new AutoGeniusInvokationAssets();
    }

    public void Init()
    {
        IsEnabled = false;
        IsExclusive = true;
    }

    public void OnCapture(CaptureContent content)
    {
        TestArrow(content);
        //Detect(content);


        //var dictionary = GeniusInvokationControl.FindMultiPicFromOneImage2OneByOne(content.CaptureRectArea.SrcGreyMat, _autoGeniusInvokationAssets.RollPhaseDiceMats, 0.7);
        //if (dictionary.Count > 0)
        //{
        //    int i = 0;
        //    foreach (var pair in dictionary)
        //    {
        //        var list = pair.Value;

        //        foreach (var p in list)
        //        {
        //            i++;
        //            VisionContext.Instance().DrawContent.PutRect("i" + i,
        //                new RectDrawable(new Rect(p.X, p.Y,
        //                    _autoGeniusInvokationAssets.RollPhaseDiceMats[pair.Key].Width,
        //                    _autoGeniusInvokationAssets.RollPhaseDiceMats[pair.Key].Height)));
        //        }
        //    }
        //    Debug.WriteLine("找到了" + i + "个");
        //}


        //var foundRectArea = content.CaptureRectArea.Find(_autoGeniusInvokationAssets.ElementalTuningConfirmButtonRo);
        //if (!foundRectArea.IsEmpty())
        //{
        //    Debug.WriteLine("找到了");
        //}
        //else
        //{
        //    Debug.WriteLine("没找到");
        //}
    }

    //private void Detect(CaptureContent content)
    //{
    //    using var memoryStream = new MemoryStream();
    //    content.CaptureRectArea.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
    //    memoryStream.Seek(0, SeekOrigin.Begin);
    //    var result = _predictor.Detect(memoryStream);
    //    Debug.WriteLine(result);
    //    var list = new List<RectDrawable>();
    //    foreach (var box in result.Boxes)
    //    {
    //        var rect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
    //        list.Add(new RectDrawable(rect, _pen));
    //    }

    //    VisionContext.Instance().DrawContent.PutOrRemoveRectList("Box", list);
    //}

    public static void TestArrow(CaptureContent content)
    {
        var mat = new Mat(content.CaptureRectArea.SrcMat, new Rect(0,0,300,240));
        Cv2.GaussianBlur(mat, mat, new Size(3, 3), 0);
        var splitMat = mat.Split();


        // 红蓝通道按位与
        var red = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[0], new Scalar(250), new Scalar(255), red);
        var blue = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[2], new Scalar(0), new Scalar(10), blue);
        var andMat = red & blue;

        Triangle(andMat);
    }

    public static void Rectangle(Mat andMat)
    {
        //腐蚀
        var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        var res = new Mat(andMat.Size(), MatType.CV_8UC1);
        Cv2.Erode(andMat, res, kernel);
        Cv2.ImShow("erode", res);

        Cv2.FindContours(res, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        for (int i = 0; i < contours.Length; i++)
        {
            // 最小外接矩形
            var rect = Cv2.MinAreaRect(contours[i]);
            // 矩形的四个顶点
            var points = Cv2.BoxPoints(rect);
            // 绘制矩形
            for (int j = 0; j < 4; ++j)
            {
                //Cv2.Line(mat, (Point)points[j], (Point)points[(j + 1) % 4], Scalar.Red, 1);
            }
        }
    }

    public static void Triangle(Mat gray)
    {
        Cv2.FindContours(gray, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        Mat dst = Mat.Zeros(gray.Size(), MatType.CV_8UC3);
        for (int i = 0; i < contours.Length; i++)
        {
            Cv2.DrawContours(dst, contours, i, new Scalar(0, 255, 0), 1, LineTypes.Link4, hierarchy);
        }

        // 遍历轮廓
        for (int i = 0; i < contours.Length; i++)
        {
            // 计算轮廓的周长
            double perimeter = Cv2.ArcLength(contours[i], true);

            // 近似多边形拟合
            OpenCvSharp.Point[] approx = Cv2.ApproxPolyDP(contours[i], 0.04 * perimeter, true);

            // 如果拟合的多边形有三个顶点，认为是三角形
            if (approx.Length == 3)
            {
                // 计算三条边的长度
                var sideLengths = new double[3];
                sideLengths[0] = Distance(approx[1], approx[2]);
                sideLengths[1] = Distance(approx[2], approx[0]);
                sideLengths[2] = Distance(approx[0], approx[1]);

                var result = sideLengths
                    .Select((value, index) => new { Value = value, Index = index })
                    .OrderBy(item => item.Value)
                    .First();

                // 计算最短线的中点
                var residue = approx.ToList();
                residue.RemoveAt(result.Index);
                var midPoint = new OpenCvSharp.Point((residue[0].X + residue[1].X) / 2, (residue[0].Y + residue[1].Y) / 2);

                // 在图像上绘制直线
                //Cv2.Line(dst2, approx[result.Index], midPoint, Scalar.Red, 1);

                VisionContext.Instance().DrawContent.PutLine("co", new LineDrawable(midPoint, approx[result.Index] + (approx[result.Index] - midPoint) * 3));
            }
        }

    }

    static double Distance(OpenCvSharp.Point pt1, OpenCvSharp.Point pt2)
    {
        int deltaX = Math.Abs(pt2.X - pt1.X);
        int deltaY = Math.Abs(pt2.Y - pt1.Y);

        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}