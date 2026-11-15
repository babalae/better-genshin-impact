using System.Runtime.InteropServices;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>真实小地图的定位和遮罩范围回归，防止高比例层图的正确定位被错误的固定视口缩放。</summary>
public sealed class MiniMapViewportTests(ITestOutputHelper output)
{
    /// <summary>新基捷城 Scale=3：独立地形配准得到中心约 (24356.2, 4730.1)、完整小地图宽度约 235.5。</summary>
    [Theory]
    [InlineData("minimap-city")]
    [InlineData("minimap-city-4k")]
    public void Match_RealScreenshot_ProjectsNativeViewportAtSelectedLayerScale(string name)
    {
        using var color = MapMaskTestImages.Load(name);
        using var image = new GameCaptureRegion(color.Clone(), 0, 0).DeriveTo1080P();
        var navigation = new NavigationInstance();
        var map = (SceneBaseMapByTemplateMatch)MapManager.GetMap(MapTypes.Teyvat, "TemplateMatch");
        // 冷启动及已有位置的后续帧都走生产导航入口，不预置候选层或已知坐标。
        for (var frame = 0; frame < 2; frame++)
        {
            var center = navigation.GetPositionStable(image, MapTypes.Teyvat.ToString(), "TemplateMatch", out var result);
            output.WriteLine($"{name} frame={frame}: center={center}, layer={map.PrevSuccessResult.Layer?.LayerId}, confidence={map.PrevSuccessResult.Confidence:F6}");
            Assert.InRange(center.X, 24351, 24361);
            Assert.InRange(center.Y, 4725, 4735);
            Assert.Equal("LayeredMap_29_-1", map.PrevSuccessResult.Layer?.LayerId);
            Assert.NotNull(result);
            Assert.Equal(3f, result.LayerScale);
            Assert.Equal(center, result.ImagePosition);
            var viewport = MapMaskTrigger.CreateMiniMapViewport(result, map);
            Assert.InRange(viewport.Width, 234, 237);
            Assert.Equal(viewport.Width, viewport.Height);
            Assert.Equal(center.X, viewport.X + viewport.Width / 2, 3);
            Assert.Equal(center.Y, viewport.Y + viewport.Height / 2, 3);
        }
    }

    /// <summary>成功后的失败帧不能沿用上一次的层比例；全图重试也必须一起替换坐标与元信息。</summary>
    [Fact]
    public void Match_FailedFrame_DoesNotReturnPreviousLayerInfo()
    {
        using var color = MapMaskTestImages.Load("minimap-city");
        using var image = new GameCaptureRegion(color.Clone(), 0, 0).DeriveTo1080P();
        var navigation = new NavigationInstance();
        var center = navigation.GetPositionStable(image, "Teyvat", "TemplateMatch", out var successResult);
        Assert.NotEqual(default, center);
        Assert.NotNull(successResult);
        Assert.Equal(3f, successResult.LayerScale);

        // 固定种子的噪声帧触发局部失败及全图重试，匹配器内部仍保留上一份成功状态。
        using var unmatchedImage = new ImageRegion(new Mat(1080, 1920, MatType.CV_8UC3), 0, 0);
        var pixels = new byte[1080 * 1920 * 3];
        new Random(20261115).NextBytes(pixels);
        Marshal.Copy(pixels, 0, unmatchedImage.SrcMat.Data, pixels.Length);
        var failedPosition = navigation.GetPositionStable(unmatchedImage, "Teyvat", "TemplateMatch", out var failedResult);
        Assert.Equal(default, failedPosition);
        Assert.Null(failedResult);
    }
}
