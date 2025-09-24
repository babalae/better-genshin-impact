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
public class LargeSiftExtractor
{
    private const int BLOCK_SIZE = 1024;
    private const int OVERLAP_SIZE = BLOCK_SIZE * 3;

    private readonly Feature2D _sift = SIFT.Create();
    
    public static void Gen1024()
    {
        var rootPath = @"E:\HuiTask\更好的原神\地图匹配\拼图结果\6.0";
        var mainMap2048BlockMat = new Mat($@"{rootPath}\map_2048.png", ImreadModes.Color);
        // 缩小 2048/1024 = 2
        var targetFilePath = $@"{rootPath}\1024_map.png";
        // opencv 缩小
        var mainMap1024BlockMat =
            mainMap2048BlockMat.Resize(new Size(mainMap2048BlockMat.Width / 2, mainMap2048BlockMat.Height / 2));
        // 转化为灰度图
        mainMap1024BlockMat.SaveImage(targetFilePath);
        Debug.WriteLine("done!");
    }

    public static void Gen256Sift()
    {
        Environment.SetEnvironmentVariable("OPENCV_IO_MAX_IMAGE_PIXELS", Math.Pow(2, 40).ToString("F0"));

        var rootPath = @"E:\HuiTask\更好的原神\地图匹配\拼图结果\6.0";

        // 缩小 2048/256 = 8
        var targetFilePath = $@"{rootPath}\Teyvat_0_256.png";
        if (!File.Exists(targetFilePath))
        {
            var mainMap2048BlockMat = new Mat($@"{rootPath}\map_2048.png", ImreadModes.Color);
            // opencv 缩小
            var mainMap256BlockMat =
                mainMap2048BlockMat.Resize(new Size(mainMap2048BlockMat.Width / 8, mainMap2048BlockMat.Height / 8));
            // 转化为灰度图
            mainMap256BlockMat = mainMap256BlockMat.CvtColor(ColorConversionCodes.BGR2GRAY);
            mainMap256BlockMat.SaveImage(targetFilePath);
        }

        FeatureMatcher featureMatcher = new(new Mat(targetFilePath, ImreadModes.Grayscale),
            new FeatureStorage("Teyvat_0_256", rootPath));

        Debug.WriteLine("done!");
    }

    public static void GenLargeSift()
    {
        Environment.SetEnvironmentVariable("OPENCV_IO_MAX_IMAGE_PIXELS", Math.Pow(2, 40).ToString("F0"));
        var extractor = new LargeSiftExtractor();
        extractor.ExtractAndSaveSift(@"E:\HuiTask\更好的原神\地图匹配\拼图结果\6.0\map_2048.png",
            @"E:\HuiTask\更好的原神\地图匹配\拼图结果\6.0\");
    }

    public void ExtractAndSaveSift(string imagePath, string outputPath)
    {
        Debug.WriteLine($"开始提取图像的SIFT特征: {imagePath}");
        var img = Cv2.ImRead(imagePath, ImreadModes.Grayscale);
        var allKeypoints = new List<KeyPoint>();
        var allDescriptors = new List<Mat>();

        // 计算需要切分的块数
        int rows = (int)Math.Ceiling(img.Height / (double)BLOCK_SIZE);
        int cols = (int)Math.Ceiling(img.Width / (double)BLOCK_SIZE);
        Debug.WriteLine($"图像被分成 {rows} 行 {cols} 列的块。");

        // 遍历每个块
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                Debug.WriteLine($"处理第 {row} 行，第 {col} 列的块");
                var (keypoints, descriptors) = ProcessBlock(img, row, col);

                // 调整keypoints的坐标
                // 修改这里：使用 for 循环而不是 foreach
                for (int i = 0; i < keypoints.Length; i++)
                {
                    var kp = keypoints[i];
                    kp.Pt.X += col * BLOCK_SIZE;
                    kp.Pt.Y += row * BLOCK_SIZE;
                    keypoints[i] = kp;
                }

                allKeypoints.AddRange(keypoints);
                allDescriptors.Add(descriptors);
            }
        }

        // 合并所有descriptors
        var finalDescriptors = new Mat();
        Cv2.VConcat(allDescriptors.ToArray(), finalDescriptors);

        // 保存结果
        SaveFeatures(allKeypoints, finalDescriptors, outputPath);

        // 释放资源
        foreach (var desc in allDescriptors)
        {
            desc.Dispose();
        }

        Debug.WriteLine("SIFT特征提取和保存完成。");
    }

    private (KeyPoint[] keypoints, Mat descriptors) ProcessBlock(Mat img, int row, int col)
    {
        // 计算当前块的范围
        int startY = row * BLOCK_SIZE;
        int startX = col * BLOCK_SIZE;

        // 处理边缘情况
        bool isEdgeRow = row == 0 || row == img.Height / BLOCK_SIZE;
        bool isEdgeCol = col == 0 || col == img.Width / BLOCK_SIZE;


        if (!isEdgeRow && !isEdgeCol)
        {
            // 非边缘区域，使用重叠区域
            int overlapStartY = Math.Max(0, startY - BLOCK_SIZE);
            int overlapStartX = Math.Max(0, startX - BLOCK_SIZE);

            using var blockRegion = new Mat(img,
                new Rect(overlapStartX, overlapStartY,
                    Math.Min(OVERLAP_SIZE, img.Width - overlapStartX),
                    Math.Min(OVERLAP_SIZE, img.Height - overlapStartY)));

            KeyPoint[] kps;
            var desc = new Mat();
            _sift.DetectAndCompute(blockRegion, null, out kps, desc);
            Debug.WriteLine($"Block at ({row},{col}) - Original keypoints count: {kps.Length}");

            // 找出中心区域关键点的索引
            var centralKeypointIndices = new List<int>();
            var centralKeypoints = new List<KeyPoint>();

            for (int i = 0; i < kps.Length; i++)
            {
                if (kps[i].Pt.X >= BLOCK_SIZE && kps[i].Pt.X < BLOCK_SIZE * 2 &&
                    kps[i].Pt.Y >= BLOCK_SIZE && kps[i].Pt.Y < BLOCK_SIZE * 2)
                {
                    centralKeypointIndices.Add(i);
                    var kp = kps[i];
                    kp.Pt.X -= BLOCK_SIZE;
                    kp.Pt.Y -= BLOCK_SIZE;
                    centralKeypoints.Add(kp);
                }
            }

            // 创建新的描述符矩阵，只包含中心区域关键点的描述符
            var centralDesc = new Mat(centralKeypointIndices.Count, desc.Cols, desc.Type());
            for (int i = 0; i < centralKeypointIndices.Count; i++)
            {
                var rowIndex = centralKeypointIndices[i];
                var row2 = desc.Row(rowIndex);
                row2.CopyTo(centralDesc.Row(i));
                row2.Dispose();
            }

            Debug.WriteLine($"中心区域处理了 {centralKeypoints.Count} 个关键点。");
            return (centralKeypoints.ToArray(), centralDesc);
        }
        else
        {
            // 边缘区域，直接处理
            using var blockRegion = new Mat(img,
                new Rect(startX, startY,
                    Math.Min(BLOCK_SIZE, img.Width - startX),
                    Math.Min(BLOCK_SIZE, img.Height - startY)));

            var desc = new Mat();
            _sift.DetectAndCompute(blockRegion, null, out var kps, desc);

            Debug.WriteLine($"边缘区域处理了 {kps.Length} 个关键点。");
            return (kps, desc);
        }
    }

    // private void SaveFeaturesOld(List<KeyPoint> keypoints, Mat descriptors, string outputPath)
    // {
    //     Debug.WriteLine($"保存 {keypoints.Count} 个关键点和描述符到 {outputPath}");
    //     using var fs1 = new FileStorage(Path.Combine(outputPath, "mainMap2048Block_SIFT.kp"), FileStorage.Modes.Write);
    //     fs1.Write("kp", keypoints.ToArray());
    //     fs1.Release();
    //     using var fs2 = new FileStorage(Path.Combine(outputPath, "mainMap2048Block_SIFT.mat"), FileStorage.Modes.Write);
    //     fs2.Write("desc", descriptors);
    //     fs2.Release();
    //     Debug.WriteLine("特征保存成功。");
    // }

    private void SaveFeatures(List<KeyPoint> keypoints, Mat descriptors, string outputPath)
    {
        Debug.WriteLine($"保存 {keypoints.Count} 个关键点和描述符到 {outputPath}");
        SaveKeyPointArray2(keypoints.ToArray(), Path.Combine(outputPath, "Teyvat_0_2048_SIFT.kp.bin"));
        SaveDescMat2(descriptors, Path.Combine(outputPath, "Teyvat_0_2048_SIFT.mat.png"));
        Debug.WriteLine("特征保存成功。");
    }


    public static void SaveDescMat2(Mat descMat, string outputPath)
    {
        descMat.SaveImage(outputPath);
    }

    public static unsafe void SaveKeyPointArray2(KeyPoint[] kpArray, string outputPath)
    {
        var kpVector = new VectorOfKeyPoint(kpArray);
        var sizeOfKeyPoint = Marshal.SizeOf<KeyPoint>();
        var kpSpan = new ReadOnlySpan<byte>((byte*)kpVector.ElemPtr, kpArray.Length * sizeOfKeyPoint);
        using var fs = new FileStream(outputPath, FileMode.Create);
        fs.Write(kpSpan);
    }
}