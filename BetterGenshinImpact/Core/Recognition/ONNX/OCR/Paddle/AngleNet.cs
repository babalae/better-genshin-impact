using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms.VisualStyles;

namespace BetterGenshinImpact.Core.Recognition.ONNX.OCR.Paddle;

class AngleNet
{
    private readonly float[] MeanValues = { 127.5F, 127.5F, 127.5F };
    private readonly float[] NormValues = { 1.0F / 127.5F, 1.0F / 127.5F, 1.0F / 127.5F };
    private const int angleDstWidth = 192;
    private const int angleDstHeight = 48;
    private const int angleCols = 2;
    private InferenceSession angleNet;
    private List<string> inputNames;

    public AngleNet()
    {
    }

    ~AngleNet()
    {
        angleNet.Dispose();
    }

    public void InitModel(string path, int numThread)
    {
        try
        {
            SessionOptions op = BgiSessionOption.Instance.Options;
            op.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
            op.InterOpNumThreads = numThread;
            op.IntraOpNumThreads = numThread;
            angleNet = new InferenceSession(path, op);
            inputNames = angleNet.InputMetadata.Keys.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message + ex.StackTrace);
            throw ex;
        }
    }

    public List<Angle> GetAngles(List<Mat> partImgs, bool doAngle, bool mostAngle)
    {
        List<Angle> angles = new List<Angle>();
        if (doAngle)
        {
            for (int i = 0; i < partImgs.Count; i++)
            {
                var startTicks = DateTime.Now.Ticks;
                var angle = GetAngle(partImgs[i]);
                var endTicks = DateTime.Now.Ticks;
                var angleTime = (endTicks - startTicks) / 10000F;
                angle.Time = angleTime;
                angles.Add(angle);
            }
        }
        else
        {
            for (int i = 0; i < partImgs.Count; i++)
            {
                var angle = new Angle();
                angle.Index = -1;
                angle.Score = 0F;
                angles.Add(angle);
            }
        }
        //Most Possible AngleIndex
        if (doAngle && mostAngle)
        {
            List<int> angleIndexes = new List<int>();
            angles.ForEach(x => angleIndexes.Add(x.Index));

            double sum = angleIndexes.Sum();
            double halfPercent = angles.Count / 2.0f;
            int mostAngleIndex;
            if (sum < halfPercent)
            {//all angle set to 0
                mostAngleIndex = 0;
            }
            else
            {//all angle set to 1
                mostAngleIndex = 1;
            }
            Console.WriteLine($"Set All Angle to mostAngleIndex({mostAngleIndex})");
            for (int i = 0; i < angles.Count; ++i)
            {
                Angle angle = angles[i];
                angle.Index = mostAngleIndex;
                angles[i] = angle;
            }
        }
        return angles;
    }

    private Angle GetAngle(Mat src)
    {
        Angle angle = new Angle();
        Mat angleImg = new Mat();
        Cv2.Resize(src, angleImg, new Size(angleDstWidth, angleDstHeight));
        Tensor<float> inputTensors = OcrUtils.SubstractMeanNormalize(angleImg, MeanValues, NormValues);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputNames[0], inputTensors)
        };
        try
        {
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = angleNet.Run(inputs))
            {
                var resultsArray = results.ToArray();
                Console.WriteLine(resultsArray);
                float[] outputData = resultsArray[0].AsEnumerable<float>().ToArray();
                return ScoreToAngle(outputData, angleCols);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message + ex.StackTrace);
            //throw ex;
        }
        return angle;
    }

    private Angle ScoreToAngle(float[] srcData, int angleCols)
    {
        Angle angle = new Angle();
        int angleIndex = 0;
        float maxValue = -1000.0F;
        for (int i = 0; i < angleCols; i++)
        {
            if (i == 0) maxValue = srcData[i];
            else if (srcData[i] > maxValue)
            {
                angleIndex = i;
                maxValue = srcData[i];
            }
        }
        angle.Index = angleIndex;
        angle.Score = maxValue;
        return angle;
    }

    private Mat AdjustTargetImg(Mat src, int dstWidth, int dstHeight)
    {
        Mat srcResize = new Mat();
        float scale = (float)dstHeight / (float)src.Rows;
        int angleWidth = (int)((float)src.Cols * scale);
        Cv2.Resize(src, srcResize, new Size(angleWidth, dstHeight));
        Mat srcFit = new Mat(dstHeight, dstWidth, MatType.CV_8UC3);
        //srcFit.SetTo(new MCvScalar(255,255,255));
        if (angleWidth < dstWidth)
        {
            Cv2.CopyMakeBorder(srcResize, srcFit, 0, 0, 0, dstWidth - angleWidth, BorderTypes.Isolated, new Scalar(255, 255, 255));
        }
        else
        {
            var rect = new Rect(0, 0, dstWidth, dstHeight);
            Mat partAngle = new Mat(srcResize, rect);
            partAngle.CopyTo(srcFit);
        }
        return srcFit;
    }
}
