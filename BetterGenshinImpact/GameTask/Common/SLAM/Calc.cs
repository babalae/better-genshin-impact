using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq; // 如果想用 Linq 的 OrderBy 和 ElementAt (但 Sort 更直接)

public static class Calc
{
    public static float GetMedian(List<float> list)
    {
        if (list == null)
        {
            throw new ArgumentNullException(nameof(list));
        }
        if (list.Count == 0)
        {
            return float.NaN; // Return NaN for empty list as a common practice
        }

        // 创建副本进行排序，以避免修改原始列表 (如果调用者期望原始列表不变)
        // 如果允许修改原始列表，可以直接用 list.Sort()
        var sortedList = new List<float>(list);
        sortedList.Sort();

        int count = sortedList.Count;
        if (count % 2 == 1) // 奇数个
        {
            return sortedList[count / 2];
        }
        else // 偶数个
        {
            return (sortedList[count / 2 - 1] + sortedList[count / 2]) / 2.0f;
        }
    }

    public static unsafe float GetMedian(Mat floatMat)
    {
        if (floatMat == null)
        {
            throw new ArgumentNullException(nameof(floatMat));
        }
        if (floatMat.Empty())
        {
            return float.NaN;
        }
        if (floatMat.Type() != MatType.CV_32FC1)
        {
            throw new ArgumentException($"Mat must be of type CV_32FC1, but was {floatMat.Type()}.", nameof(floatMat));
        }

        // 预估容量，可以稍微提高 List 添加性能
        List<float> floatList = new List<float>((int)floatMat.Total());

        if (floatMat.IsContinuous())
        {
            float* ptr = (float*)floatMat.DataPointer;
            long totalElements = floatMat.Total(); // Mat.Total() 返回 long
            for (long i = 0; i < totalElements; i++)
            {
                floatList.Add(ptr[i]);
            }
        }
        else
        {
            for (int y = 0; y < floatMat.Rows; y++)
            {
                for (int x = 0; x < floatMat.Cols; x++)
                {
                    floatList.Add(floatMat.At<float>(y, x));
                }
            }
        }
        
        // 如果 floatList 仍然可能为空 (理论上经过 IsEmpty 检查后不会，但作为防御性编程)
        if (floatList.Count == 0) return float.NaN;

        return GetMedian(floatList); // 调用上面已修正的列表版本
    }
}
