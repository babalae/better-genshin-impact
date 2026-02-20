using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public class RectClampTests
{
    [Theory]
    [InlineData(10, 10, 50, 50, 200, 200, 10, 10, 50, 50)]   // 完全在范围内
    [InlineData(-10, 20, 100, 50, 200, 200, 0, 20, 90, 50)]   // 左侧越界
    [InlineData(20, -15, 50, 100, 200, 200, 20, 0, 50, 85)]   // 上方越界
    [InlineData(150, 10, 100, 50, 200, 200, 150, 10, 50, 50)] // 右侧越界
    [InlineData(10, 150, 50, 100, 200, 200, 10, 150, 50, 50)] // 下方越界
    [InlineData(-10, -20, 300, 400, 100, 100, 0, 0, 100, 100)] // 四边都越界
    [InlineData(-100, 10, 50, 50, 200, 200, 0, 10, 0, 50)]    // 完全在外（左），宽为0
    [InlineData(0, 0, 10, 10, 0, 0, 0, 0, 0, 0)]              // 零尺寸图像
    public void ClampTo_IntOverload_ReturnsExpected(
        int x, int y, int w, int h,
        int maxW, int maxH,
        int ex, int ey, int ew, int eh)
    {
        var rect = new Rect(x, y, w, h);
        var result = rect.ClampTo(maxW, maxH);
        Assert.Equal(new Rect(ex, ey, ew, eh), result);
    }

    [Fact]
    public void ClampTo_MatOverload_MatchesIntOverload()
    {
        var rect = new Rect(-10, -5, 100, 80);
        using var mat = new Mat(200, 200, MatType.CV_8UC3);

        var result = rect.ClampTo(mat);

        Assert.Equal(rect.ClampTo(mat.Cols, mat.Rows), result);
        Assert.Equal(new Rect(0, 0, 90, 75), result);
    }
}
