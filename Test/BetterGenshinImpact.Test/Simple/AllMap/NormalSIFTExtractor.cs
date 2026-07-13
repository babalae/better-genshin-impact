using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using OpenCvSharp.Internal.Vectors;

namespace BetterGenshinImpact.Test.Simple.AllMap;

/// <summary>
/// 运行完毕MapPuzzle就来运行这个
/// 每次地图更新都需要
/// </summary>
public class NormalSiftExtractor
{
    private static readonly Feature2D Sift = SIFT.Create();
    
    public static void GenNormalSift()
    {
        var imagePath = @"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.2\TheChasm_1024_2x2.png";
        var outputPath =  @"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.2\";
        
        Mat trainDescriptors = new();
        var img = Cv2.ImRead(imagePath, ImreadModes.Grayscale);

        Sift.DetectAndCompute(img, null, out var trainKeyPoints,trainDescriptors);
        
        SaveFeatures(trainKeyPoints, trainDescriptors, outputPath);
    }
    
    
    private static void SaveFeatures(KeyPoint[] keyPoints, Mat descriptors, string outputPath)
    {
        Debug.WriteLine($"保存 {keyPoints.Length} 个关键点和描述符到 {outputPath}");
        FeatureStorageHelper.SaveKeyPointArray(keyPoints, Path.Combine(outputPath, "TheChasm_0_1024_SIFT.kp.bin"));
        FeatureStorageHelper.SaveDescMat(descriptors, Path.Combine(outputPath, "TheChasm_0_1024_SIFT.mat.png"));
        Debug.WriteLine("特征保存成功。");
    }
}