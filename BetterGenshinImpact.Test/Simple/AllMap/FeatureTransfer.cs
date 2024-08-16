using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System.IO;

namespace BetterGenshinImpact.Test.Simple.AllMap;

public class FeatureTransfer
{
    private static string rootPath = Global.Absolute(@"User\Map\");

    public static void Transfer()
    {
        var kp = LoadKeyPointArrayOld();
        if (kp != null)
        {
            string kpPath = Path.Combine(rootPath, $"mainMap2048Block_SIFT.kp");
            FileStorage fs = new(kpPath, FileStorage.Modes.Write);
            fs.Write("kp", kp);
            fs.Release();
        }
    }

    public static KeyPoint[]? LoadKeyPointArrayOld()
    {
        string kpPath = Path.Combine(rootPath, "mainMap2048Block_SIFT.kp.old");
        if (File.Exists(kpPath))
        {
            return ObjectUtils.Deserialize(File.ReadAllBytes(kpPath)) as KeyPoint[];
        }
        return null;
    }
}
