using BetterGenshinImpact.Core.Config;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;

public class FeatureStorage(string name)
{
    private readonly string rootPath = Global.Absolute(@"Assets\Map\");
    public void SetType(Feature2DType type)
    {
        TypeName = type.ToString();
    }

    public string TypeName { get; set; } = "UNKNOWN";

    public KeyPoint[]? LoadKeyPointArray()
    {
        CreateFolder();
        string kpPath = Path.Combine(rootPath, $"{name}_{TypeName}.kp");
        if (File.Exists(kpPath))
        {
            FileStorage fs = new(kpPath, FileStorage.Modes.Read);
            var kpArray = fs["kp"]?.ReadKeyPoints();
            fs.Release();
            return kpArray;
        }
        return null;
    }

    public void SaveKeyPointArray(KeyPoint[] kpArray)
    {
        CreateFolder();
        string kpPath = Path.Combine(rootPath, $"{name}_{TypeName}.kp");
        FileStorage fs = new(kpPath, FileStorage.Modes.Write);
        fs.Write("kp", kpArray);
        fs.Release();
    }

    private void CreateFolder()
    {
        if (Directory.Exists(rootPath) == false)
        {
            Directory.CreateDirectory(rootPath);
        }
    }

    public Mat? LoadDescMat()
    {
        CreateFolder();
        var files = Directory.GetFiles(rootPath, $"{name}_{TypeName}.mat", SearchOption.AllDirectories);
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

    public void SaveDescMat(Mat descMat)
    {
        CreateFolder();
        // 删除旧文件
        var fileName = $"{name}_{TypeName}.mat";
        var files = Directory.GetFiles(rootPath, fileName, SearchOption.AllDirectories);
        foreach (var file in files)
        {
            File.Delete(file);
        }
        var descPath = Path.Combine(rootPath, fileName);
        FileStorage fs = new(descPath, FileStorage.Modes.Write);
        fs.Write("desc", descMat);
        fs.Release();
    }
}
