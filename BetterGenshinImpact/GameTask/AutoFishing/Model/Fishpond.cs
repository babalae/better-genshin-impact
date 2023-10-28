using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using Compunet.YoloV8.Data;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

public class Fishpond
{
    /// <summary>
    /// 鱼池位置
    /// </summary>
    public Rect FishpondRect { get; set; }

    /// <summary>
    /// 抛竿落点位置
    /// </summary>
    public Rect TargetRect { get; set; }

    /// <summary>
    /// 鱼池中的鱼
    /// </summary>
    public List<OneFish> Fishes { get; set; } = new();


    public Fishpond(IDetectionResult result)
    {
        foreach (var box in result.Boxes)
        {
            if (box.Class.Name == "moonfin")
            {
                continue;
            }
            else if (box.Class.Name == "target")
            {
                TargetRect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
                continue;
            }

            var fish = new OneFish(box.Class.Name, new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height));
            Fishes.Add(fish);
        }

        FishpondRect = CalculateFishpondRect();
    }

    /// <summary>
    /// 计算鱼塘位置
    /// </summary>
    /// <returns></returns>
    public Rect CalculateFishpondRect()
    {
        if (Fishes.Count == 0)
        {
            return Rect.Empty;
        }

        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        foreach (var fish in Fishes)
        {
            if (fish.Rect.Left < left)
            {
                left = fish.Rect.Left;
            }

            if (fish.Rect.Top < top)
            {
                top = fish.Rect.Top;
            }

            if (fish.Rect.Right > right)
            {
                right = fish.Rect.Right;
            }

            if (fish.Rect.Bottom > bottom)
            {
                bottom = fish.Rect.Bottom;
            }
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 通过鱼饵名称过滤鱼
    /// </summary>
    /// <param name="baitName"></param>
    /// <returns></returns>
    public List<OneFish> FilterByBaitName(string baitName)
    {
        return Fishes.Where(fish => fish.FishType.BaitName == baitName).ToList();
    }

    public OneFish? FilterByBaitNameAndRecently(string baitName, Rect prevTargetFishRect)
    {
        var fishes = FilterByBaitName(baitName);
        if (fishes.Count == 0)
        {
            return null;
        }

        var min = double.MaxValue;
        var c1 = prevTargetFishRect.GetCenterPoint();
        OneFish? result = null;
        foreach (var fish in fishes)
        {
            var c2 = fish.Rect.GetCenterPoint();
            var distance = Math.Sqrt(Math.Pow(c1.X - c2.X, 2) + Math.Pow(c1.Y - c2.Y, 2));
            if (distance < min)
            {
                min = distance;
                result = fish;
            }
        }
        return result;
    }

    /// <summary>
    /// 最多的鱼吃的鱼饵名称
    /// </summary>
    /// <returns></returns>
    public string MostMatchBait()
    {
        Dictionary<string, int> dict = new();
        foreach (var fish in Fishes)
        {
            if (dict.ContainsKey(fish.FishType.BaitName))
            {
                dict[fish.FishType.BaitName]++;
            }
            else
            {
                dict[fish.FishType.BaitName] = 1;
            }
        }

        var max = 0;
        var result = "";
        foreach (var (key, value) in dict)
        {
            if (value > max)
            {
                max = value;
                result = key;
            }
        }
        return result;
    }
}