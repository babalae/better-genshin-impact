using BetterGenshinImpact.Core.Config;
using OpenCvSharp;
using OpenCvSharp.Internal.Vectors;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;

public class FeatureStorage
{
    private readonly string _rootPath;
    private readonly string _name;
    public void SetType(Feature2DType type)
    {
        TypeName = type.ToString();
    }

    public FeatureStorage(string name)
    {
        _name = name;
        _rootPath = Global.Absolute(@"Assets\Map\");
    }
    public FeatureStorage(string name, string rootPath)
    {
        _name = name;
        _rootPath = rootPath;
    }

    public string TypeName { get; set; } = "UNKNOWN";

    public unsafe KeyPoint[]? LoadKeyPointArray()
    {
        CreateFolder();
        var kpPath = Path.Combine(_rootPath, $"{_name}_{TypeName}.kp.bin");
        if (File.Exists(kpPath))
        {
            using var fs = File.Open(kpPath, FileMode.Open);
            var sizeOfKeyPoint = Marshal.SizeOf<KeyPoint>();
            if (fs.Length % sizeOfKeyPoint != 0) throw new FileFormatException("无法识别的KeyPoint格式");
            using var kpVector = new VectorOfKeyPoint((nuint)(fs.Length / sizeOfKeyPoint));
            using var ms = new UnmanagedMemoryStream((byte*)kpVector.ElemPtr, fs.Length, fs.Length, FileAccess.Write);
            fs.CopyTo(ms);
            return kpVector.ToArray();
        }
        return null;
    }

    public unsafe void SaveKeyPointArray(KeyPoint[] kpArray)
    {
        CreateFolder();
        var kpPath = Path.Combine(_rootPath, $"{_name}_{TypeName}.kp.bin");
        var kpVector = new VectorOfKeyPoint(kpArray);
        var sizeOfKeyPoint = Marshal.SizeOf<KeyPoint>();
        var kpSpan = new ReadOnlySpan<byte>((byte*)kpVector.ElemPtr, kpArray.Length * sizeOfKeyPoint);
        using var fs = new FileStream(kpPath, FileMode.Create);
        fs.Write(kpSpan);
    }

    private void CreateFolder()
    {
        if (Directory.Exists(_rootPath) == false)
        {
            Directory.CreateDirectory(_rootPath);
        }
    }

    public Mat? LoadDescMat()
    {
        CreateFolder();
        var files = Directory.GetFiles(_rootPath, $"{_name}_{TypeName}.mat.png", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            return null;
        }
        else if (files.Length > 1)
        {
            Debug.WriteLine($"[FeatureSerializer] Found multiple files: {string.Join(", ", files)}");
        }
        using var img = new Mat(files[0], ImreadModes.Grayscale);
        var mat = new Mat(img.Size(), MatType.CV_32FC1);
        img.ConvertTo(mat, MatType.CV_32FC1);
        return mat;
    }

    public void SaveDescMat(Mat descMat)
    {
        CreateFolder();
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
}
