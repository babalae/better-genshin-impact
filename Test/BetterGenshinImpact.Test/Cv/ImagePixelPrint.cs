using System.Diagnostics;
using OpenCvSharp;

namespace BetterGenshinImpact.Test.Cv;

public class ImagePixelPrint
{
    public static void Test()
    {
        PrintColorAndGrayValues(@"E:\HuiTask\更好的原神\自动秘境\自动战斗\队伍识别\111.png");
    }

    /// <summary>
    /// 打印图片的所有颜色值（去重并统计次数），然后灰度化后输出所有灰度值（去重并统计次数）
    /// </summary>
    /// <param name="inputImage">输入图片</param>
    public static void PrintColorAndGrayValues(Mat inputImage)
    {
        if (inputImage == null || inputImage.Empty())
        {
            Debug.WriteLine("输入图片为空或无效");
            return;
        }

        int width = inputImage.Width;
        int height = inputImage.Height;
        int channels = inputImage.Channels();

        Debug.WriteLine($"====== 图片信息 ======");
        Debug.WriteLine($"宽度: {width}, 高度: {height}, 通道数: {channels}");
        Debug.WriteLine($"图片类型: {inputImage.Type()}");
        Debug.WriteLine("");

        // 打印原始图片的颜色值
        Debug.WriteLine("====== 原始图片颜色值（去重并统计） ======");

        if (channels == 3) // BGR 彩色图片
        {
            Dictionary<string, int> colorCount = new Dictionary<string, int>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vec3b color = inputImage.At<Vec3b>(y, x);
                    string colorKey = $"B={color.Item0}, G={color.Item1}, R={color.Item2}";

                    if (colorCount.ContainsKey(colorKey))
                    {
                        colorCount[colorKey]++;
                    }
                    else
                    {
                        colorCount[colorKey] = 1;
                    }
                }
            }

            foreach (var kvp in colorCount.OrderByDescending(x => x.Value))
            {
                Debug.WriteLine($"{kvp.Key} - 出现次数: {kvp.Value}");
            }

            Debug.WriteLine($"唯一颜色数量: {colorCount.Count}");
        }
        else if (channels == 4) // BGRA 彩色图片（带透明度）
        {
            Dictionary<string, int> colorCount = new Dictionary<string, int>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vec4b color = inputImage.At<Vec4b>(y, x);
                    string colorKey = $"B={color.Item0}, G={color.Item1}, R={color.Item2}, A={color.Item3}";

                    if (colorCount.ContainsKey(colorKey))
                    {
                        colorCount[colorKey]++;
                    }
                    else
                    {
                        colorCount[colorKey] = 1;
                    }
                }
            }

            foreach (var kvp in colorCount.OrderByDescending(x => x.Value))
            {
                Debug.WriteLine($"{kvp.Key} - 出现次数: {kvp.Value}");
            }

            Debug.WriteLine($"唯一颜色数量: {colorCount.Count}");
        }
        else if (channels == 1) // 灰度图片
        {
            Dictionary<byte, int> grayCount = new Dictionary<byte, int>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte gray = inputImage.At<byte>(y, x);

                    if (grayCount.ContainsKey(gray))
                    {
                        grayCount[gray]++;
                    }
                    else
                    {
                        grayCount[gray] = 1;
                    }
                }
            }

            foreach (var kvp in grayCount.OrderByDescending(x => x.Value))
            {
                Debug.WriteLine($"Gray={kvp.Key} - 出现次数: {kvp.Value}");
            }

            Debug.WriteLine($"唯一灰度值数量: {grayCount.Count}");
        }

        Debug.WriteLine("");

        // 转换为灰度图
        Mat grayImage = new Mat();
        if (channels == 3 || channels == 4)
        {
            Cv2.CvtColor(inputImage, grayImage, ColorConversionCodes.BGR2GRAY);
        }
        else if (channels == 1)
        {
            grayImage = inputImage.Clone();
        }
        else
        {
            Debug.WriteLine("不支持的图片格式");
            return;
        }

        // 打印灰度化后的值
        Debug.WriteLine("====== 灰度化后的值（去重并统计） ======");
        Dictionary<byte, int> grayValueCount = new Dictionary<byte, int>();

        for (int y = 0; y < grayImage.Height; y++)
        {
            for (int x = 0; x < grayImage.Width; x++)
            {
                byte grayValue = grayImage.At<byte>(y, x);

                if (grayValueCount.ContainsKey(grayValue))
                {
                    grayValueCount[grayValue]++;
                }
                else
                {
                    grayValueCount[grayValue] = 1;
                }
            }
        }

        foreach (var kvp in grayValueCount.OrderByDescending(x => x.Value))
        {
            Debug.WriteLine($"Gray={kvp.Key} - 出现次数: {kvp.Value}");
        }

        Debug.WriteLine($"唯一灰度值数量: {grayValueCount.Count}");

        Debug.WriteLine("====== 完成 ======");

        // 释放资源
        grayImage?.Dispose();
    }

    /// <summary>
    /// 打印图片的所有颜色值，然后灰度化后输出所有灰度值（从文件路径加载）
    /// </summary>
    /// <param name="imagePath">图片文件路径</param>
    public static void PrintColorAndGrayValues(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
        {
            Debug.WriteLine("图片路径为空");
            return;
        }

        Mat image = Cv2.ImRead(imagePath);
        if (image.Empty())
        {
            Debug.WriteLine($"无法加载图片: {imagePath}");
            return;
        }

        Debug.WriteLine($"加载图片: {imagePath}");
        PrintColorAndGrayValues(image);

        image?.Dispose();
    }
}