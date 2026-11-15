using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>加载随测试复制的脱敏截图，缺失样本必须失败，不能静默当作测试成功。</summary>
internal static class MapMaskTestImages
{
    /// <summary>读取指定的无损 WebP 真实截图。</summary>
    public static Mat Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "MapMask", name + ".webp");
        Assert.True(File.Exists(path),
            $"缺少地图测试样本：{path}。请在仓库根目录执行 git submodule update --init -- Test/BetterGenshinImpact.UnitTest/Assets，然后重新编译测试项目。");
        var image = new Mat(path, ImreadModes.Color);
        Assert.False(image.Empty());
        return image;
    }
}
