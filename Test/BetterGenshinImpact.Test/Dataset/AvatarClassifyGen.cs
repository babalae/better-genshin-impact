using System.Diagnostics;
using System.IO;
using OpenCvSharp;

namespace BetterGenshinImpact.Test.Dataset;

public class AvatarClassifyGen
{
    // 基础图像文件夹
    private const string BaseDir = @"E:\HuiTask\更好的原神\数据源\Snap.Static\AvatarIcon";
    
    // 产出文件夹
    private const string OutputDir = @"E:\HuiAi\YOLOv8\3.avatar-side";

    // 背景图像文件夹
    private static readonly string BackgroundDir = @"E:\HuiTask\更好的原神\数据源\background";

    private static readonly Random Rd = new Random();
    
    public static readonly List<string> ImgNames = ["UI_AvatarIcon_Side_Ambor.png","UI_AvatarIcon_Side_AmborCostumeWic.png"];

    public static void GenAll()
    {
        // 读取基础图像
        // List<string> sideImageFiles = Directory.GetFiles(Path.Combine(BaseDir, "side_src"), "*.png", SearchOption.TopDirectoryOnly).ToList();
        // 只用一个图像
        List<string> sideImageFiles = [];
        
        foreach (string imgName in ImgNames)
        {
            sideImageFiles.Add(Path.Combine(BaseDir, imgName));
        }
        // 生成训练集
        GenTo(sideImageFiles, Path.Combine(OutputDir, @"dateset\train"), 200);
        // 生成测试集
        GenTo(sideImageFiles, Path.Combine(OutputDir, @"dateset\test"), 40);
        // GenTo(new List<string> { sideImageFiles[1] }, Path.Combine(BaseDir, @"dateset\test"), 1);
    }

    static void GenTo(List<string> sideImageFiles, string dataFolder, int count)
    {
        // 保留区域 从底边中心为固定点
        var reservedSize = new Size(60, 80);

        Directory.CreateDirectory(dataFolder);
        // 循环生成每个基础图像对应的数据集
        foreach (string sideImageFile in sideImageFiles)
        {
            // 获取基础图像文件名
            string sideImageFileName = Path.GetFileNameWithoutExtension(sideImageFile);
            sideImageFileName = sideImageFileName.Replace("UI_AvatarIcon_Side_", "");
            // 创建基础图像对应的数据集文件夹
            string sideDataFolder = Path.Combine(dataFolder, sideImageFileName);
            Directory.CreateDirectory(sideDataFolder);

            // 读取基础图像
            Mat sideImageSrc = Cv2.ImRead(sideImageFile, ImreadModes.Unchanged);
            var channels = sideImageSrc.Split();
            var alphaChannel = channels[3]; // 透明通道
            for (int i = 0; i < 3; i++)
            {
                Cv2.Multiply(channels[i], alphaChannel, channels[i], 1 / 255.0);
            }

            var sideImage = new Mat();
            Cv2.Merge(channels[..3], sideImage);

            // Cv2.ImShow("avatar", sideImage);

            // 循环生成图像
            for (int i = 0; i < count; i++)
            {
                // 随机挑选一张背景图像
                string backgroundImageFile = Path.Combine(BackgroundDir,
                    Directory.GetFiles(BackgroundDir, "*.png")[
                        Rd.Next(Directory.GetFiles(BackgroundDir, "*.png").Length)]);

                // 从背景图像中随机取一块 128x128 的区域
                Mat backgroundImage = Cv2.ImRead(backgroundImageFile, ImreadModes.Color);
                Rect backgroundRect = new Rect(Rd.Next(backgroundImage.Width - 128),
                    new Random().Next(backgroundImage.Height - 128), 128, 128);
                Mat backgroundImageRegion = backgroundImage[backgroundRect];

                // 随机平移、缩放保留区域
                float scale = (float)(Rd.NextDouble() * (1.6 - 0.7) + 0.7);
                int w = (int)(sideImage.Width * scale);
                int h = (int)(sideImage.Height * scale);

                Debug.WriteLine($"{sideImageFileName} 生成随机缩放{scale}");

                // 把保留区域合成到背景图像上
                Mat backgroundImageRegionClone = backgroundImageRegion.Clone();
                var resizedSideImage = new Mat();
                Cv2.Resize(sideImage, resizedSideImage, new Size(128 * scale, 128 * scale));
                // Cv2.ImShow("resizedSideImage", resizedSideImage);
                var resizedMaskImage = new Mat();
                // Cv2.Threshold(alphaChannel, alphaChannel, 200, 255, ThresholdTypes.Otsu);
                Cv2.Resize(~alphaChannel, resizedMaskImage, new Size(128 * scale, 128 * scale), 0, 0,
                    InterpolationFlags.Cubic);
                var resizedAlphaChannel = new Mat();
                Cv2.Resize(alphaChannel, resizedAlphaChannel, new Size(128 * scale, 128 * scale), 0, 0,
                    InterpolationFlags.Cubic);

                // Cv2.ImShow("resizedMaskImage", resizedMaskImage);
                // generatedImage[transformedRect] = resizedSideImage;
                Mat result;
                if (scale > 1)
                {
                    int xSpace1 = (int)((128 - reservedSize.Width * scale) / 2.0);
                    int ySpace1 = (int)(128 - reservedSize.Height * scale);
                    int xSpace2 = (int)((resizedSideImage.Width - 128) / 2.0);
                    int ySpace2 = resizedSideImage.Height - 128;
                    int xSpace = Math.Min(xSpace1, xSpace2);
                    int ySpace = Math.Min(ySpace1, ySpace2);
                    int offsetX = Rd.Next(-xSpace, xSpace);
                    int offsetY = Rd.Next(-ySpace, 0);
                    Debug.WriteLine($"{sideImageFileName} 缩放{scale}大于1 偏移 ({offsetX},{offsetY})");

                    var roi = new Rect((resizedSideImage.Width - 128) / 2 + offsetX,
                        (resizedSideImage.Height - 128) + offsetY, 128, 128);
                    // result = new Mat();
                    // Cv2.BitwiseAnd(backgroundImageRegionClone, backgroundImageRegionClone, result, resizedMaskImage[roi]);
                    result = Mul(backgroundImageRegionClone, resizedAlphaChannel[roi]);
                    Cv2.Add(result, resizedSideImage[roi], result);
                }
                else
                {
                    int xSpace = (128 - w) / 2;
                    int ySpace = 128 - h;
                    int offsetX = Rd.Next(-xSpace, xSpace);
                    int offsetY = Rd.Next(-ySpace, 0);
                    Debug.WriteLine($"{sideImageFileName} 缩放{scale}小于等于1 偏移 ({offsetX},{offsetY})");

                    var roi = new Rect((128 - resizedSideImage.Width) / 2 + offsetX,
                        (128 - resizedSideImage.Height) + offsetY, resizedSideImage.Width, resizedSideImage.Height);
                    var res = new Mat();
                    // Cv2.BitwiseAnd(backgroundImageRegionClone[roi], backgroundImageRegionClone[roi], res, resizedMaskImage);
                    res = Mul(backgroundImageRegionClone[roi], resizedAlphaChannel);
                    Cv2.Add(res, resizedSideImage, res);
                    backgroundImageRegionClone[roi] = res;
                    result = backgroundImageRegionClone.Clone();
                }

                // Cv2.ImShow("avatarR", result);
                // 保存生成的图像
                Cv2.ImWrite(Path.Combine(sideDataFolder, $"{sideImageFileName}_{i}.png"), result);
            }
        }

        static Mat Mul(Mat background, Mat alphaChannel)
        {
            var channels = background.Split();
            for (int i = 0; i < 3; i++)
            {
                Cv2.Multiply(channels[i], ~ alphaChannel, channels[i], 1 / 255.0);
            }

            Mat result = new Mat();
            Cv2.Merge(channels[..3], result);
            return result;
        }
    }
}