using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 存储空间设置
/// </summary>
[Serializable]
public partial class StorageConfig : ObservableObject
{
    /// <summary>
    /// 文件缓存（Data/Cache/Files）的总量上限（MB），超过后自动删除最早的缓存，降到上限的 80%。
    /// 推理编译缓存和下载目录不计入
    /// </summary>
    [ObservableProperty]
    private int _fileCacheLimitMb = 1024;
}
