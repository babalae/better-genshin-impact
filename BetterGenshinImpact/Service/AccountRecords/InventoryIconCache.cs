using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Storage;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>跨账号共享的 36×36 图标缓存；缺失或损坏时返回空，不影响库存。</summary>
public sealed class InventoryIconCache : IStorageAreaProvider
{
    /// <summary>串行写入与清理，避免同一进程删除正在使用的缓存。</summary>
    private readonly object _gate = new();
    /// <summary>图标只能通过重新采集或上传补齐，独立存放，不参与通用缓存自动淘汰。</summary>
    private readonly string _cacheRoot = Path.Combine(AppPaths.Cache, "AccountRecords");

    /// <summary>模型版本变化后使用不同缓存目录，避免复用过期识别图标。</summary>
    private readonly string _root = Path.Combine(AppPaths.Cache, "AccountRecords", "icons",
        File.GetLastWriteTimeUtc(Global.Absolute(@"Assets\Model\ItemV2\item.csv")).Ticks.ToString());

    /// <summary>标准名称生成安全文件名。</summary>
    private string PathFor(string name) => Path.Combine(_root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))) + ".png");

    /// <summary>采集或用户提供裁剪图；仅保存标准化缓存，不保留上传原图。</summary>
    /// <remarks>TODO：写入入口尚未接入 UseImageCache；外围程序关闭缓存时应忽略建立和更新请求。</remarks>
    public void Store(string name, byte[] encodedImage)
    {
        lock (_gate) StoreCore(name, encodedImage);
    }

    /// <summary>检查当前模型下是否存在物品缓存文件，无需加载或解码图像。</summary>
    public bool Exists(string name)
    {
        lock (_gate) return File.Exists(PathFor(name));
    }

    /// <summary>删除当前模型下单个物品的共享缓存，等待重新采集。</summary>
    public void Delete(string name)
    {
        lock (_gate) File.Delete(PathFor(name));
    }

    /// <summary>生成标准尺寸；不自动删除旧图标，清理须由用户明确发起。</summary>
    private void StoreCore(string name, byte[] encodedImage)
    {
        if (encodedImage.Length > 4 * 1024 * 1024) throw new ArgumentException("图标文件超过 4 MiB，未保存。");
        using var input = Cv2.ImDecode(encodedImage, ImreadModes.Unchanged);
        if (input.Empty()) throw new ArgumentException("图片无法解码，图标未保存。");
        if (input.Width > 4096 || input.Height > 4096)
            throw new ArgumentException($"图标尺寸为 {input.Width}×{input.Height}，未保存。宽、高均不能超过 4096 像素。");
        using var color = new Mat();
        switch (input.Channels())
        {
            case 1: Cv2.CvtColor(input, color, ColorConversionCodes.GRAY2BGRA); break;
            case 3: Cv2.CvtColor(input, color, ColorConversionCodes.BGR2BGRA); break;
            case 4: input.CopyTo(color); break;
            default: throw new ArgumentException("图标颜色通道不受支持，未保存。仅支持灰度、RGB 或 RGBA 图像。");
        }
        var scale = Math.Min(36d / color.Width, 36d / color.Height);
        var size = new Size(Math.Max(1, (int)Math.Round(color.Width * scale)), Math.Max(1, (int)Math.Round(color.Height * scale)));
        using var resized = new Mat();
        Cv2.Resize(color, resized, size, 0, 0, InterpolationFlags.Area);
        using var square = new Mat(36, 36, MatType.CV_8UC4, Scalar.All(0));
        using (var region = new Mat(square, new Rect((36 - size.Width) / 2, (36 - size.Height) / 2, size.Width, size.Height))) resized.CopyTo(region);
        Directory.CreateDirectory(_root);
        var path = PathFor(name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Cv2.ImEncode(".png", square, out var bytes);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
            // 采集与上传的图标无法在缓存未命中时即时重建，因此不按容量或时间淘汰。
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.IO.FileFormatException) { return null; }
    }

    /// <summary>清除 AccountRecords 统一目录下的全部派生图像缓存，不接触账号数据。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true);
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
                lock (_gate) return StorageFileOperations.Clear(_cacheRoot, ct);
            }, ct),
        };
    }
}
