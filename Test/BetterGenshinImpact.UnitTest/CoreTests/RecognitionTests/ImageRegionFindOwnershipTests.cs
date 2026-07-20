using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
/// PR1 所有权契约回归测试。
/// 覆盖：Find() 不返回输入区域、ToImageRegionView() 零拷贝视图、ToOwnedImageRegion() 深拷贝、
/// DeriveTo1080P() 的消费语义与异常安全、缓存 Mat 的归属。
/// </summary>
public class ImageRegionFindOwnershipTests
{
    private static GameCaptureRegion CreateCapture(int width = 320, int height = 180, Scalar? fill = null)
    {
        return new GameCaptureRegion(new Mat(height, width, MatType.CV_8UC3, fill ?? Scalar.Black), 0, 0,
            drawContent: new FakeDrawContent());
    }

    private static Mat CreateTemplate()
    {
        var template = new Mat(20, 20, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(template, new Rect(2, 2, 8, 8), Scalar.White, -1);
        Cv2.Circle(template, new Point(14, 6), 3, new Scalar(160, 160, 160), -1);
        Cv2.Line(template, new Point(2, 17), new Point(17, 13), new Scalar(220, 220, 220), 2);
        return template;
    }

    [Fact]
    public void Find_TemplateMatchSuccess_DoesNotReturnInputRegion()
    {
        var region = CreateCapture();
        try
        {
            using var template = CreateTemplate();
            using (var target = new Mat(region.SrcMat, new Rect(40, 30, 20, 20)))
            {
                template.CopyTo(target);
            }

            var ro = new RecognitionObject
            {
                Name = "OwnershipTemplate",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = template.Clone(),
                Threshold = 0.8
            }.InitTemplate();

            var result = region.Find(ro);

            Assert.True(result.IsExist());
            Assert.NotSame(region, result);

            // 识别结果不拥有像素，Dispose 它不能影响输入区域
            result.Dispose();
            Assert.False(region.SrcMat.IsDisposed);
            Assert.Equal(320, region.SrcMat.Width);
        }
        finally
        {
            region.Dispose();
        }
    }

    [Fact]
    public void Find_TemplateMatchFail_ReturnsEmptyRegionAndKeepsInput()
    {
        var region = CreateCapture();
        try
        {
            using var template = CreateTemplate();
            var ro = new RecognitionObject
            {
                Name = "OwnershipTemplateMiss",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = template.Clone(),
                Threshold = 0.99
            }.InitTemplate();

            var result = region.Find(ro);

            Assert.True(result.IsEmpty());
            Assert.NotSame(region, result);
            result.Dispose();
            Assert.False(region.SrcMat.IsDisposed);
        }
        finally
        {
            region.Dispose();
        }
    }

    [Fact]
    public void ToImageRegionView_OnImageRegion_ReturnsNewObject()
    {
        var capture = CreateCapture();
        try
        {
            var view = capture.ToImageRegionView();
            try
            {
                Assert.NotSame(capture, view);
                Assert.NotSame(capture.SrcMat, view.SrcMat);
                Assert.Equal(capture.Width, view.Width);
                Assert.Equal(capture.Height, view.Height);
            }
            finally
            {
                view.Dispose();
            }

            // 先释放子视图后，父区域仍然可用
            Assert.False(capture.SrcMat.IsDisposed);
            Assert.Equal(320, capture.SrcMat.Width);
        }
        finally
        {
            capture.Dispose();
        }
    }

    [Fact]
    public void ToImageRegionView_OnDerivedRegion_KeepsCoordinateChain()
    {
        var capture = CreateCapture();
        try
        {
            var child = capture.Derive(100, 60, 40, 30);
            var expected = child.ConvertSelfPositionToGameCaptureRegion();

            var view = child.ToImageRegionView();
            try
            {
                Assert.NotSame(child, view);
                Assert.Equal(40, view.Width);
                Assert.Equal(30, view.Height);
                Assert.Equal(expected, view.ConvertSelfPositionToGameCaptureRegion());
                Assert.Equal(expected, view.ConvertPositionToGameCaptureRegion(view.X, view.Y, view.Width, view.Height));
            }
            finally
            {
                view.Dispose();
            }

            Assert.False(capture.SrcMat.IsDisposed);
        }
        finally
        {
            capture.Dispose();
        }
    }

    [Fact]
    public void ToImageRegionView_BorrowsParentPixels()
    {
        var capture = CreateCapture(64, 48);
        try
        {
            var view = capture.ToImageRegionView();
            try
            {
                capture.SrcMat.SetTo(new Scalar(10, 20, 30));
                // 零拷贝视图共享父区域像素
                Assert.Equal(new Vec3b(10, 20, 30), view.SrcMat.At<Vec3b>(0, 0));
            }
            finally
            {
                view.Dispose();
            }
        }
        finally
        {
            capture.Dispose();
        }
    }

    [Fact]
    public void ToOwnedImageRegion_SurvivesSourceDispose()
    {
        var capture = CreateCapture(64, 48, new Scalar(10, 20, 30));
        var child = capture.Derive(8, 8, 16, 16);
        var owned = child.ToOwnedImageRegion();
        try
        {
            Assert.NotSame(child, owned);
            Assert.Equal(16, owned.Width);

            capture.Dispose();

            // 深拷贝与源区域生命周期无关
            Assert.False(owned.SrcMat.IsDisposed);
            Assert.Equal(new Vec3b(10, 20, 30), owned.SrcMat.At<Vec3b>(0, 0));
        }
        finally
        {
            owned.Dispose();
        }
    }

    [Fact]
    public void DeriveTo1080P_WithinLimit_ReturnsSelf()
    {
        var region = CreateCapture(1920, 1080);
        try
        {
            Assert.Same(region, region.DeriveTo1080P());
        }
        finally
        {
            region.Dispose();
        }
    }

    [Fact]
    public void DeriveTo1080P_AboveLimit_ConsumesSelfAndReturnsNewRegion()
    {
        var region = CreateCapture(2560, 1600);
        var srcMat = region.SrcMat;

        var result = region.DeriveTo1080P();
        try
        {
            Assert.NotSame(region, result);
            Assert.Equal(1920, result.Width);
            // 大于 1080P 时原始帧被本方法消费掉，只保留坐标换算用的 Prev 节点
            Assert.True(srcMat.IsDisposed);
            Assert.Same(region, result.Prev);
        }
        finally
        {
            result.Dispose();
        }
    }

    [Fact]
    public void DeriveTo1080P_WhenResizeFails_DoesNotDisposeSelf()
    {
        // Height / scale 归零，Cv2.Resize 的目标尺寸非法，用于触发 Resize 失败路径
        var region = CreateCapture(2560, 1);
        try
        {
            Assert.ThrowsAny<Exception>(() => region.DeriveTo1080P());

            // Resize 失败时尚未消费 this，原始帧的所有权仍在调用者手里
            Assert.False(region.SrcMat.IsDisposed);
            Assert.Equal(2560, region.SrcMat.Width);
        }
        finally
        {
            region.Dispose();
        }
    }

    [Fact]
    public void CacheMats_AreOwnedByRegion_AndMustNotBeDisposedByCallers()
    {
        var region = CreateCapture(64, 48);
        var grey = region.CacheGreyMat;

        // 同一个区域反复取缓存拿到的是同一个实例（借用值，不是每次新建）
        Assert.Same(grey, region.CacheGreyMat);
        Assert.False(grey.IsDisposed);

        region.Dispose();

        // 缓存归区域所有，随区域一起释放；调用方额外 Dispose 会破坏唯一所有者规则
        Assert.True(grey.IsDisposed);
    }
}
