using System.Collections.Generic;

namespace BetterGenshinImpact.Infrastructure.Network.Download;

/// <summary>
/// 下载分类，决定 Data/Downloads 下的子目录名和在存储空间窗口中的名称
/// </summary>
/// <param name="Id">子目录名，只使用小写字母、数字和短横线</param>
/// <param name="DisplayName">展示名称</param>
/// <param name="Description">存储空间窗口中的说明</param>
public sealed record DownloadCategory(string Id, string DisplayName, string Description);

/// <summary>
/// 已定义的下载分类。新功能在这里新增一项并加入 <see cref="All"/>，即可在存储空间窗口中看到并清理
/// </summary>
public static class DownloadCategories
{
    /// <summary>
    /// 硬件加速运行库（ONNX Runtime、CUDA、cuDNN、TensorRT）的安装包
    /// </summary>
    public static readonly DownloadCategory OnnxRuntime = new("onnxruntime", "运行库安装包",
        "硬件加速运行库的安装包，安装成功后自动删除，留下的通常是未完成或安装失败的下载");

    /// <summary>
    /// 主页网络背景图片，下载完成后移入 User/Images
    /// </summary>
    public static readonly DownloadCategory BannerImage = new("banner-image", "网络背景图片",
        "主页网络背景图片的下载，完成后移入用户目录，留下的通常是未完成的下载");

    /// <summary>
    /// 全部分类，供存储区域登记
    /// </summary>
    public static IReadOnlyList<DownloadCategory> All { get; } = [OnnxRuntime, BannerImage];
}
