using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Core.Config;
using OpenCvSharp;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>跨账号共享的 36×36 图标缓存；缺失或损坏时返回空，不影响库存。</summary>
public sealed class InventoryIconCache
{
    /// <summary>串行写入与清理，避免同一进程删除正在使用的缓存。</summary>
    private readonly object _gate = new();
    /// <summary>模型版本变化后使用不同缓存目录，避免复用过期识别图标。</summary>
    private readonly string _root = Global.Absolute(Path.Combine("Cache", "AccountRecords", "icons",
        File.GetLastWriteTimeUtc(Global.Absolute(@"Assets\Model\ItemV2\item.csv")).Ticks.ToString()));

    /// <summary>标准名称生成安全文件名。</summary>
    private string PathFor(string name) => Path.Combine(_root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))) + ".png");

    /// <summary>扫描或用户提供裁剪图；仅保存标准化缓存，不保留上传原图。</summary>
    public void Store(string name, byte[] encodedImage)
    {
        lock (_gate) StoreCore(name, encodedImage);
    }

    /// <summary>删除当前模型下单个物品的共享缓存，等待扫描重建。</summary>
    public void Delete(string name)
    {
        lock (_gate) File.Delete(PathFor(name));
    }

    /// <summary>生成标准尺寸并限制跨模型版本缓存总量。</summary>
    private void StoreCore(string name, byte[] encodedImage)
    {
        if (encodedImage.Length > 4 * 1024 * 1024) throw new ArgumentException("图标数据超过 4 MiB。");
        using var input = Cv2.ImDecode(encodedImage, ImreadModes.Unchanged);
        if (input.Empty() || input.Width > 4096 || input.Height > 4096) throw new ArgumentException("图标尺寸无效。");
        using var color = new Mat();
        switch (input.Channels())
        {
            case 1: Cv2.CvtColor(input, color, ColorConversionCodes.GRAY2BGRA); break;
            case 3: Cv2.CvtColor(input, color, ColorConversionCodes.BGR2BGRA); break;
            case 4: input.CopyTo(color); break;
            default: throw new ArgumentException("不支持的图标颜色格式。");
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
            // 缓存可重建；最多保留 64 MiB，优先淘汰旧版本和最久未更新的图标。
            var files = new DirectoryInfo(Global.Absolute(@"Cache\AccountRecords\icons")).EnumerateFiles("*.png", SearchOption.AllDirectories)
                .OrderBy(file => file.LastWriteTimeUtc).ToArray();
            var total = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (total <= 64L * 1024 * 1024) break;
                var length = file.Length;
                file.Delete(); total -= length;
            }
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

    /// <summary>只清除派生图标缓存，不接触账号数据。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            var root = Global.Absolute(@"Cache\AccountRecords\icons");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
