using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>共享外部资源包中的背包页图标；缺失资源时保留文字展示。</summary>
internal static class AccountRecordsPageIcons
{
    /// <summary>只允许已知分页名称，避免将字典内容当作文件路径。</summary>
    private static readonly HashSet<string> _pages = ["武器", "圣遗物", "养成道具", "食物", "材料", "小道具", "任务", "贵重道具", "摆设"];
    /// <summary>仅缓存有限的内置分页图标。</summary>
    private static readonly ConcurrentDictionary<string, Lazy<BitmapImage?>> _images = new();

    /// <summary>每个背包页只解码一次；资源包更新后重启生效。</summary>
    public static BitmapImage? Get(string page) => _pages.Contains(page) ? _images.GetOrAdd("default:" + page, _ => new Lazy<BitmapImage?>(() => Load(page))).Value : null;

    /// <summary>加载原始 PNG，显示尺寸由视图控制，不修改素材。</summary>
    private static BitmapImage? Load(string page)
    {
        var path = Global.Absolute(Path.Combine("Assets", "Other", "InventoryPages", page + ".png"));
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (FileFormatException) { return null; }
    }
}
