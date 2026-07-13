using System.Diagnostics;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using OpenCvSharp.Internal.Vectors;

namespace BetterGenshinImpact.Test.Simple.AllMap;

public class FeatureTransfer
{
    // private static string rootPath = Global.Absolute(@"User\Map\");
    //
    // public static void Transfer()
    // {
    //     var kp = LoadKeyPointArrayOld();
    //     if (kp != null)
    //     {
    //         string kpPath = Path.Combine(rootPath, $"mainMap2048Block_SIFT.kp");
    //         FileStorage fs = new(kpPath, FileStorage.Modes.Write);
    //         fs.Write("kp", kp);
    //         fs.Release();
    //     }
    // }
    //
    // public static KeyPoint[]? LoadKeyPointArrayOld()
    // {
    //     string kpPath = Path.Combine(rootPath, "mainMap2048Block_SIFT.kp.old");
    //     if (File.Exists(kpPath))
    //     {
    //         return ObjectUtils.Deserialize(File.ReadAllBytes(kpPath)) as KeyPoint[];
    //     }
    //     return null;
    // }
    
    private static readonly string _rootPath = @"E:\HuiPrograming\MainProject\better-genshin-impact\BetterGenshinImpact\bin\x64\Debug\net8.0-windows10.0.22621.0\Assets\Map";
    private static readonly string _name = "mainMap256Block";
    
    private static string TypeName { get; set; } = nameof(Feature2DType.SIFT);
    
    
    public static void Transfer()
    {
        var kp = LoadKeyPointArray1();
        SaveKeyPointArray(kp);
        var desc = LoadDescMat1();
        SaveDescMat(desc);
    }
    
    
    public static KeyPoint[]? LoadKeyPointArray1()
    {
        string kpPath = Path.Combine(_rootPath, $"{_name}_{TypeName}.kp");
        if (File.Exists(kpPath))
        {
            FileStorage fs = new(kpPath, FileStorage.Modes.Read);
            var kpArray = fs["kp"]?.ReadKeyPoints();
            fs.Release();
            return kpArray;
        }
        return null;
    }
    
    public static Mat? LoadDescMat1()
    {
        var files = Directory.GetFiles(_rootPath, $"{_name}_{TypeName}.mat", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            return null;
        }
        else if (files.Length > 1)
        {
            Debug.WriteLine($"[FeatureSerializer] Found multiple files: {string.Join(", ", files)}");
        }
        FileStorage fs = new(files[0], FileStorage.Modes.Read);
        var mat = fs["desc"]?.ReadMat();
        fs.Release();
        return mat;
    }
    
    public static void SaveDescMat(Mat descMat)
    {
        // 删除旧文件
        var fileName = $"{_name}_{TypeName}.mat.png";
        var files = Directory.GetFiles(_rootPath, fileName, SearchOption.AllDirectories);
        foreach (var file in files)
        {
            File.Delete(file);
        }
        var descPath = Path.Combine(_rootPath, fileName);
        descMat.SaveImage(descPath);
    }
    
    public static unsafe void SaveKeyPointArray(KeyPoint[] kpArray)
    {
        var kpPath = Path.Combine(_rootPath, $"{_name}_{TypeName}.kp.bin");
        var kpVector = new VectorOfKeyPoint(kpArray);
        var sizeOfKeyPoint = Marshal.SizeOf<KeyPoint>();
        var kpSpan = new ReadOnlySpan<byte>((byte*)kpVector.ElemPtr, kpArray.Length * sizeOfKeyPoint);
        using var fs = new FileStream(kpPath, FileMode.Create);
        fs.Write(kpSpan);
    }
}
