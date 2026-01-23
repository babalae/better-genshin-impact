using System;
using System.Diagnostics;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

public class TemplateMatchStabilityDetector : IDisposable
{
    private Mat previousFrame;
    private readonly double similarityThreshold = 0.98; // 相似度阈值（0-1）
    private readonly int stableFrameCount = 2;
    private int currentStableCount = 0;
    private double lastSimilarity = 0;

    public bool IsStable(Mat currentFrame)
    {
        Mat processed = PreprocessFrame(currentFrame);

        if (previousFrame == null)
        {
            previousFrame = processed;
            return false;
        }

        // 使用归一化相关系数进行匹配
        double similarity = ComputeSimilarity(previousFrame, processed);
        lastSimilarity = similarity;
        
        Debug.WriteLine("Similarity: " + similarity);
        if (similarity >= similarityThreshold)
        {
            currentStableCount++;
            if (currentStableCount >= stableFrameCount)
            {
                previousFrame?.Dispose();
                previousFrame = processed;
                return true;
            }
            else
            {
                processed.Dispose();
            }
        }
        else
        {
            currentStableCount = 0;
            previousFrame?.Dispose();
            previousFrame = processed;
        }

        return false;
    }

    private Mat PreprocessFrame(Mat frame)
    {
        Mat small = new Mat();

        // 降采样加速
        Cv2.Resize(frame, small, new Size(320, 180), 0, 0, InterpolationFlags.Area);

        return small;
    }

    private double ComputeSimilarity(Mat template, Mat image)
    {
        // 确保两帧大小相同
        if (template.Size() != image.Size())
        {
            return 0;
        }

        // 使用归一化相关系数匹配
        Mat result = new Mat();
        Cv2.MatchTemplate(image, template, result, TemplateMatchModes.CCoeffNormed);

        // 获取匹配结果（只有一个值）
        double minVal, maxVal;
        Point minLoc, maxLoc;
        Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

        result.Dispose();

        return maxVal; // 返回相关系数（-1 到 1，1 表示完全相同）
    }

    public double GetLastSimilarity()
    {
        return lastSimilarity;
    }

    public void Reset()
    {
        currentStableCount = 0;
        previousFrame?.Dispose();
        previousFrame = null;
    }

    public void Dispose()
    {
        previousFrame?.Dispose();
        previousFrame = null;
        GC.SuppressFinalize(this);
    }
}
