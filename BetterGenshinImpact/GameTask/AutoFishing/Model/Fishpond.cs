using System;
using System.Collections.Generic;
using System.Linq;
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
}