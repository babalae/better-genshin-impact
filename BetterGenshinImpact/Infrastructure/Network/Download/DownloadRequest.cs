using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Infrastructure.Network.Download;

/// <summary>
/// 下载请求
/// </summary>
/// <param name="Url">下载地址，只支持 http 和 https</param>
/// <param name="DisplayName">在下载中心和错误信息中显示的名称</param>
/// <param name="Category">下载分类，决定下载目录的子目录和存储空间窗口中的分组</param>
public sealed record DownloadRequest(Uri Url, string DisplayName, DownloadCategory Category)
{
    /// <summary>
    /// 完成后的目标路径，必须位于程序目录内。为空时保留在 Data/Downloads 中，并返回该路径
    /// </summary>
    public string? DestinationPath { get; init; }

    /// <summary>
    /// 已知的文件大小，用于校验和提前检查磁盘空间
    /// </summary>
    public long? ExpectedSize { get; init; }

    /// <summary>
    /// 期望的哈希，支持 SHA256、SHA512
    /// </summary>
    public DownloadHash? ExpectedHash { get; init; }

    /// <summary>
    /// 允许的最大字节数，超过时中止
    /// </summary>
    public long MaxBytes { get; init; } = long.MaxValue;

    /// <summary>
    /// 备用地址，主地址失败后依次尝试
    /// </summary>
    public IReadOnlyList<Uri> Mirrors { get; init; } = [];

    /// <summary>
    /// 是否显示在下载中心。后台自动下载的小文件可以设为 false
    /// </summary>
    public bool ShowInDownloadCenter { get; init; } = true;

    /// <summary>
    /// 发起下载的位置，例如“硬件加速设置”。设置后表示下载完成还要由发起方继续处理（解压、安装），
    /// 下载中心不提供重试和打开文件夹，失败时提示用户回到这里重试
    /// </summary>
    public string? Origin { get; init; }
}

/// <summary>
/// 下载文件的期望哈希
/// </summary>
/// <param name="Algorithm">算法名称：SHA256 或 SHA512</param>
/// <param name="Value">哈希值，十六进制或 Base64</param>
public sealed record DownloadHash(string Algorithm, string Value);
