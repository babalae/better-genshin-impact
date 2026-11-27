using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.Service.AccountRecords;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>在独立临时目录验证图标规范化、名称缓存与变更版本。</summary>
public sealed class InventoryIconCacheTests : IDisposable
{
    /// <summary>本测试独占的目录，不访问实际用户缓存。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "InventoryIconCacheTests", Guid.NewGuid().ToString("N"));

    /// <summary>图标按名称稳定存储，跨服务实例复用并返回冻结的规范尺寸图像。</summary>
    [Fact]
    public void Store_ReusesStableNamePathAndLoadsFrozenImage()
    {
        using var input = new Mat(20, 40, MatType.CV_8UC3, Scalar.All(128));
        Cv2.ImEncode(".png", input, out var bytes);
        var cache = new InventoryIconCache(_root);
        cache.Store("摩拉", bytes);
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("摩拉"))) + ".png";
        var stored = File.ReadAllBytes(Path.Combine(_root, "icons", fileName));
        var reopened = new InventoryIconCache(_root);
        var image = reopened.Load("摩拉");
        Assert.NotNull(image);
        Assert.True(image.IsFrozen);
        Assert.Equal(36, image.PixelWidth);
        Assert.Equal(36, image.PixelHeight);
        Assert.Equal(stored, File.ReadAllBytes(Path.Combine(_root, "icons", fileName)));
        reopened.Clear();
        Assert.False(reopened.Exists("摩拉"));
    }

    /// <summary>三档规格均写入实际像素尺寸；改变规格不改写其他已有图标。</summary>
    [Theory]
    [InlineData(36)]
    [InlineData(72)]
    [InlineData(128)]
    public void Store_UsesSelectedSize(int size)
    {
        using var input = new Mat(124, 123, MatType.CV_8UC3, Scalar.All(128));
        Cv2.ImEncode(".png", input, out var bytes);
        var cache = new InventoryIconCache(_root);
        cache.Store("旧图标", bytes);
        cache.Store("新图标", bytes, size);
        Assert.Equal(size, cache.Load("新图标")!.PixelWidth);
        Assert.Equal(size, cache.Load("新图标")!.PixelHeight);
        Assert.Equal(36, cache.Load("旧图标")!.PixelWidth);
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Store("无效规格", bytes, 64));
    }

    /// <summary>高位深 PNG 缩放后保留颜色和不透明度，不能成功返回一张透明图片。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void Store_NormalizesSixteenBitPng(int channels)
    {
        using var input = new Mat(16, 16, MatType.CV_16UC(channels), new Scalar(70 * 257, 80 * 257, 90 * 257, 65535));
        var cache = new InventoryIconCache(_root);
        cache.Store("摩拉", input.ToBytes(".png"));
        using var stored = Cv2.ImRead(Assert.Single(Directory.GetFiles(Path.Combine(_root, "icons"))), ImreadModes.Unchanged);
        Assert.Equal(MatType.CV_8UC4, stored.Type());
        var pixel = stored.At<Vec4b>(18, 18);
        Assert.Equal((byte)70, pixel.Item0);
        Assert.Equal((byte)(channels == 1 ? 70 : 80), pixel.Item1);
        Assert.Equal((byte)(channels == 1 ? 70 : 90), pixel.Item2);
        Assert.Equal(byte.MaxValue, pixel.Item3);
    }

    /// <summary>成功写入、单项删除及存储清理均使界面版本失效；失败写入不产生虚假变更。</summary>
    [Fact]
    public async Task Revision_TracksWritesAndBothCleanupEntrances()
    {
        var cache = new InventoryIconCache(_root);
        using var input = new Mat(12, 12, MatType.CV_8UC3, Scalar.White);
        Assert.Equal(0, cache.Revision);
        cache.Store("摩拉", input.ToBytes(".png"));
        Assert.Equal(1, cache.Revision);
        Assert.Throws<ArgumentException>(() => cache.Store("摩拉", [1, 2, 3]));
        Assert.Equal(1, cache.Revision);
        cache.Delete("摩拉");
        Assert.Equal(2, cache.Revision);
        cache.Delete("摩拉");
        Assert.Equal(2, cache.Revision);
        cache.Store("摩拉", input.ToBytes(".png"));
        cache.Clear();
        Assert.Equal(4, cache.Revision);
        cache.Store("摩拉", input.ToBytes(".png"));
        await Assert.Single(cache.GetAreas()).CleanupAsync!(default);
        Assert.Equal(6, cache.Revision);
        Assert.False(cache.Exists("摩拉"));
    }

    /// <summary>释放本测试创建的临时数据。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
