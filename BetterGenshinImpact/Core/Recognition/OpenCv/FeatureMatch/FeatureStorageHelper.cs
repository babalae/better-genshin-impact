using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Internal.Vectors;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;

public class FeatureStorageHelper
{
    public static unsafe KeyPoint[]? LoadKeyPointArray(string kpPath)
    {
        if (File.Exists(kpPath))
        {
            using var fs = File.Open(kpPath, FileMode.Open);
            var sizeOfKeyPoint = Marshal.SizeOf<KeyPoint>();
            if (fs.Length % sizeOfKeyPoint != 0)
            {
                throw new FileFormatException("无法识别的KeyPoint格式");
            }

            using var kpVector = new VectorOfKeyPoint((nuint)(fs.Length / sizeOfKeyPoint));
            using var ms = new UnmanagedMemoryStream((byte*)kpVector.ElemPtr, fs.Length, fs.Length, FileAccess.Write);
            fs.CopyTo(ms);
            return kpVector.ToArray();
        }

        return null;
    }

    public static unsafe void SaveKeyPointArray(KeyPoint[] kpArray, string kpPath)
    {
        var kpVector = new VectorOfKeyPoint(kpArray);
        var sizeOfKeyPoint = Marshal.SizeOf<KeyPoint>();
        var kpSpan = new ReadOnlySpan<byte>((byte*)kpVector.ElemPtr, kpArray.Length * sizeOfKeyPoint);
        using var fs = new FileStream(kpPath, FileMode.Create);
        fs.Write(kpSpan);
    }

    public static Mat? LoadDescriptorMat(string descriptorPath)
    {
        if (File.Exists(descriptorPath))
        {
            using var img = new Mat(descriptorPath, ImreadModes.Grayscale);
            var mat = new Mat(img.Size(), MatType.CV_32FC1);
            img.ConvertTo(mat, MatType.CV_32FC1);
            return mat;
        }

        return null;
    }

    public static void SaveDescMat(Mat descMat, string descriptorPath)
    {
        descMat.SaveImage(descriptorPath);
    }
}