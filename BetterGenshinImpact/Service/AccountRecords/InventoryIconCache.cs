using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Infrastructure.Storage;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>跨账号共享的可配置尺寸图标缓存；缺失或损坏时返回空，不影响库存。</summary>
public sealed class InventoryIconCache : IStorageAreaProvider
{
    /// <summary>串行写入与清理，避免同一进程删除正在使用的缓存。</summary>
    private readonly object _gate = new();
    /// <summary>图标只能通过重新采集或上传补齐，独立存放，不参与通用缓存自动淘汰。</summary>
    private readonly string _cacheRoot;

    /// <summary>按标准名称长期复用，不因 CSV 内容或修改时间变化切换目录。</summary>
    private readonly string _root;
    /// <summary>本进程内图标变更版本，界面据此刷新纯图标上报和清理后的显示。</summary>
    private long _revision;

    /// <summary>共享实例内的变更版本；不追踪其他进程直接修改的文件。</summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>使用所有账号共享的物品图标目录。</summary>
    public InventoryIconCache() : this(Path.Combine(AppPaths.Cache, "AccountRecords")) { }

    /// <summary>允许在独立目录中验证图标读写。</summary>
    public InventoryIconCache(string cacheRoot)
    {
        _cacheRoot = cacheRoot;
        _root = Path.Combine(cacheRoot, "icons");
    }

    /// <summary>标准名称生成安全文件名。</summary>
    private string PathFor(string name) => Path.Combine(_root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))) + ".png");

    /// <summary>采集或用户提供裁剪图；仅保存标准化缓存，不保留上传原图。</summary>
    /// <remarks>调用方按 UseImageCache 决定是否写入；本服务只负责图像规范化和存储。</remarks>
    public void Store(string name, byte[] encodedImage, int pixelSize = 36)
    {
        lock (_gate)
        {
            StoreCore(name, encodedImage, pixelSize);
            Interlocked.Increment(ref _revision);
        }
    }

    /// <summary>界面只读查询，不接续旧目录、不等待后台加载锁；旧目录图标需重新采集或上传。</summary>
    public bool Exists(string name) => File.Exists(PathFor(name));

    /// <summary>删除当前模型下单个物品的共享缓存，等待重新采集。</summary>
    public void Delete(string name)
    {
        lock (_gate)
        {
            var path = PathFor(name);
            if (!File.Exists(path)) return;
            File.Delete(path);
            Interlocked.Increment(ref _revision);
        }
    }

    /// <summary>生成标准尺寸；不自动删除旧图标，清理须由用户明确发起。</summary>
    private void StoreCore(string name, byte[] encodedImage, int pixelSize)
    {
        if (pixelSize is not (36 or 72 or 128)) throw new ArgumentOutOfRangeException(nameof(pixelSize), "缓存边长须为 36、72 或 128 像素。");
        if (encodedImage.Length > 4 * 1024 * 1024) throw new ArgumentException("图标文件超过 4 MiB，未保存。");
        using var input = Cv2.ImDecode(encodedImage, ImreadModes.Unchanged);
        if (input.Empty()) throw new ArgumentException("图片无法解码，图标未保存。");
        if (input.Width > 4096 || input.Height > 4096)
            throw new ArgumentException($"图标尺寸为 {input.Width}×{input.Height}，未保存。宽、高均不能超过 4096 像素。");
        if (input.Depth() != MatType.CV_8U && input.Depth() != MatType.CV_16U)
            throw new ArgumentException("图标仅支持 8 位或 16 位无符号整数颜色，未保存。");
        using var color = new Mat();
        switch (input.Channels())
        {
            case 1: Cv2.CvtColor(input, color, ColorConversionCodes.GRAY2BGRA); break;
            case 3: Cv2.CvtColor(input, color, ColorConversionCodes.BGR2BGRA); break;
            case 4: input.CopyTo(color); break;
            default: throw new ArgumentException("图标颜色通道不受支持，未保存。仅支持灰度、RGB 或 RGBA 图像。");
        }
        // 高位深 PNG 先归一为 8 位，避免 CopyTo 因类型不匹配重建 ROI 而留下透明画布。
        if (color.Depth() == MatType.CV_16U) color.ConvertTo(color, MatType.CV_8U, 1d / 257);
        var scale = Math.Min((double)pixelSize / color.Width, (double)pixelSize / color.Height);
        var size = new Size(Math.Max(1, (int)Math.Round(color.Width * scale)), Math.Max(1, (int)Math.Round(color.Height * scale)));
        using var resized = new Mat();
        Cv2.Resize(color, resized, size, 0, 0, InterpolationFlags.Area);
        using var square = new Mat(pixelSize, pixelSize, MatType.CV_8UC4, Scalar.All(0));
        using (var region = new Mat(square, new Rect((pixelSize - size.Width) / 2, (pixelSize - size.Height) / 2, size.Width, size.Height))) resized.CopyTo(region);
        Cv2.ImEncode(".png", square, out var bytes);
        AtomicFile.WriteAllBytes(PathFor(name), bytes);
        // 采集与上传的图标无法在缓存未命中时即时重建，因此不按容量或时间淘汰。
    }

    /// <summary>完整解码到内存后立即释放文件，冻结图像可跨线程展示。</summary>
    public BitmapSource? Load(string name)
    {
        lock (_gate) return LoadCore(name);
    }

    /// <summary>缓存故障退回占位图。</summary>
    private BitmapSource? LoadCore(string name)
    {
        try
        {
            var path = PathFor(name);
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.IO.FileFormatException) { return null; }
    }

    /// <summary>清除 AccountRecords 统一目录下的全部派生图像缓存，不接触账号数据。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_cacheRoot)) return;
            try { Directory.Delete(_cacheRoot, true); }
            // 清理中途失败也可能已经删除部分图片，仍通知界面重新查询。
            finally { Interlocked.Increment(ref _revision); }
        }
    }

    /// <summary>登记需确认的独立图标区域，不参加一键清理；与图标写入使用同一把锁。</summary>
    public IEnumerable<StorageArea> GetAreas()
    {
        yield return new StorageArea("account-records:icons", "狛荷屋物品图标", StorageGroup.Cache, [_cacheRoot])
        {
            Description = "采集或手动上传的物品图标，不自动过期或按容量淘汰",
            CleanupEffect = "删除后需重新采集或手动上传，库存数量和事件记录不受影响",
            RequiresConfirmation = true,
            IncludedInQuickCleanup = false,
            CleanupAsync = ct => Task.Run(() =>
            {
                lock (_gate)
                {
                    try { return StorageFileOperations.Clear(_cacheRoot, ct); }
                    finally { Interlocked.Increment(ref _revision); }
                }
            }, ct),
        };
    }
}
