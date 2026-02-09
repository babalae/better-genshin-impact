using System;
using System.Diagnostics;
using OpenCvSharp;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

public class ImageDifferenceDetector
{
    /// <summary>
    /// 找出四张灰度图中与其他三张差异最大的图片
    /// 投票机制：每张图片投票给与它差异最大的图片，如果某张图片获得3票则返回其索引
    /// </summary>
    /// <param name="images">包含4张Mat对象的数组</param>
    /// <returns>差异最大的图片索引（0-3），如果没有图片获得3票则返回-1</returns>
    public static int FindMostDifferentImage(Mat[] images)
    {
        try
        {
            if (images is not { Length: 4 })
            {
                throw new ArgumentException(Lang.S["Gen_10079_ff92d5"]);
            }

            // 验证所有图片尺寸相同
            Size firstSize = images[0].Size();
            for (int i = 1; i < 4; i++)
            {
                if (images[i].Size() != firstSize)
                {
                    throw new ArgumentException(Lang.S["Gen_10078_631d08"]);
                }
            }

            // 存储每张图片获得的投票数
            int[] votes = new int[4];

            // 每张图片投票给与它差异最大的图片
            for (int i = 0; i < 4; i++)
            {
                int maxDiffImageIndex = -1;
                double maxDifference = 0;

                // 找出与图片i差异最大的图片
                for (int j = 0; j < 4; j++)
                {
                    if (i != j)
                    {
                        double difference = CalculateDifference(images[i], images[j]);
                        Debug.WriteLine($"{Lang.S["Gen_10077_a7bd11"]});

                        if (difference > maxDifference)
                        {
                            maxDifference = difference;
                            maxDiffImageIndex = j;
                        }
                    }
                }

                // 图片i投票给差异最大的图片
                if (maxDiffImageIndex != -1)
                {
                    votes[maxDiffImageIndex]++;
                    Debug.WriteLine($"{Lang.S["Gen_10076_4f9463"]});
                }
            }

            // 输出投票结果
            Debug.WriteLine(Lang.S["Gen_10075_418c7e"]);
            for (int i = 0; i < 4; i++)
            {
                Debug.WriteLine($"{Lang.S["Gen_10074_4ea1af"]});
            }

            // 检查是否有图片获得3票
            for (int i = 0; i < 4; i++)
            {
                if (votes[i] >= 3)
                {
                    return i;
                }
            }

            return -1;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{Lang.S["Gen_10073_b50a5f"]});
            return -1;
        }
    }

    /// <summary>
    /// 计算差值
    /// </summary>
    private static double CalculateDifference(Mat img1, Mat img2)
    {
        using var diff = new Mat();
        // 计算差值的绝对值
        Cv2.Absdiff(img1, img2, diff);
        // 统计非零像素点（即颜色不同的像素点）
        return Cv2.CountNonZero(diff);
    }
}