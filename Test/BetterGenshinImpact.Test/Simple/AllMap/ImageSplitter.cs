using System;
using System.IO;
using OpenCvSharp;

namespace BetterGenshinImpact.Test.Simple.AllMap;

public static class ImageSplitter
{
    // 写死的图片文件路径
    private static readonly string ImagePath = @"E:\HuiTask\更好的原神\地图匹配\拼图结果\5.8\map_1024.jpg";
    
    /// <summary>
    /// 按照1024x1024的大小对图片进行切割
    /// </summary>
    public static void SplitImage()
    {
        if (!File.Exists(ImagePath))
        {
            Console.WriteLine($"图片文件不存在: {ImagePath}");
            return;
        }

        try
        {
            // 使用OpenCV读取图片
            using var originalImage = new Mat(ImagePath, ImreadModes.Color);
            var originalWidth = originalImage.Width;
            var originalHeight = originalImage.Height;
            
            // 获取原始文件的扩展名
            var extension = Path.GetExtension(ImagePath);
            var outputDirectory = Path.Combine(Path.GetDirectoryName(ImagePath) ?? string.Empty, "tiles");
            
            // 确保输出目录存在
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                Console.WriteLine($"创建输出目录: {outputDirectory}");
            }
            
            Console.WriteLine($"原始图片尺寸: {originalWidth}x{originalHeight}");
            
            // 计算需要切割的块数
            var blocksX = (int)Math.Ceiling((double)originalWidth / 1024);
            var blocksY = (int)Math.Ceiling((double)originalHeight / 1024);
            
            Console.WriteLine($"将切割为 {blocksX}x{blocksY} 个块");
            
            // 开始切割
            for (int y = 0; y < blocksY; y++)
            {
                for (int x = 0; x < blocksX; x++)
                {
                    // 计算当前块的位置和大小
                    var startX = x * 1024;
                    var startY = y * 1024;
                    var width = Math.Min(1024, originalWidth - startX);
                    var height = Math.Min(1024, originalHeight - startY);
                    
                    // 使用OpenCV创建ROI(感兴趣区域)进行切割
                    var roi = new Rect(startX, startY, width, height);
                    using var croppedImage = new Mat(originalImage, roi);
                    
                    // 保存切割后的图片
                    var outputFileName = $"{x}_{y}{extension}";
                    var outputPath = Path.Combine(outputDirectory, outputFileName);
                    
                    // 使用OpenCV保存图片
                    Cv2.ImWrite(outputPath, croppedImage);
                    
                    Console.WriteLine($"已保存: {outputFileName} (尺寸: {width}x{height})");
                }
            }
            
            Console.WriteLine("图片切割完成!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"切割图片时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 按照 Leaflet 标准对图片进行切割，原点在左下角
    /// 切片命名格式为 x_y，其中 y 坐标从负数开始（左上角为 0_-12）
    /// </summary>
    public static void SplitImageForLeaflet()
    {
        if (!File.Exists(ImagePath))
        {
            Console.WriteLine($"图片文件不存在: {ImagePath}");
            return;
        }

        try
        {
            // 使用OpenCV读取图片
            using var originalImage = new Mat(ImagePath, ImreadModes.Color);
            var originalWidth = originalImage.Width;
            var originalHeight = originalImage.Height;
            
            // 获取原始文件的扩展名
            var extension = Path.GetExtension(ImagePath);
            var outputDirectory = Path.Combine(Path.GetDirectoryName(ImagePath) ?? string.Empty, "leaflet_tiles");
            
            // 确保输出目录存在
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                Console.WriteLine($"创建输出目录: {outputDirectory}");
            }
            
            Console.WriteLine($"原始图片尺寸: {originalWidth}x{originalHeight}");
            
            // 计算需要切割的块数
            var blocksX = (int)Math.Ceiling((double)originalWidth / 1024);
            var blocksY = (int)Math.Ceiling((double)originalHeight / 1024);
            
            Console.WriteLine($"将切割为 {blocksX}x{blocksY} 个块 (Leaflet 格式)");
            
            // 开始切割 - Leaflet 格式，原点在左下角
            for (int y = 0; y < blocksY; y++)
            {
                for (int x = 0; x < blocksX; x++)
                {
                    // 计算当前块的位置和大小
                    var startX = x * 1024;
                    var startY = y * 1024;
                    var width = Math.Min(1024, originalWidth - startX);
                    var height = Math.Min(1024, originalHeight - startY);
                    
                    // 使用OpenCV创建ROI(感兴趣区域)进行切割
                    var roi = new Rect(startX, startY, width, height);
                    using var croppedImage = new Mat(originalImage, roi);
                    
                    // Leaflet 坐标系：原点在左下角，y 轴向上为正
                    // 图像坐标系：原点在左上角，y 轴向下为正
                    // 需要将图像坐标转换为 Leaflet 坐标
                    var leafletY = -(blocksY - 1 - y);
                    
                    // 保存切割后的图片，使用 Leaflet 坐标命名
                    var outputFileName = $"{x}_{leafletY}{extension}";
                    var outputPath = Path.Combine(outputDirectory, outputFileName);
                    
                    // 使用OpenCV保存图片
                    Cv2.ImWrite(outputPath, croppedImage);
                    
                    Console.WriteLine($"已保存: {outputFileName} (尺寸: {width}x{height}, 图像坐标: {x}_{y})");
                }
            }
            
            Console.WriteLine("Leaflet 格式图片切割完成!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"切割图片时发生错误: {ex.Message}");
        }
    }
}