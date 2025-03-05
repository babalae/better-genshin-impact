using BetterGenshinImpact.Core.Recognition.OpenCv;
using Compunet.YoloV8.Data;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

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
    public Rect? TargetRect { get; set; }

    /// <summary>
    /// 鱼池中的鱼
    /// </summary>
    public List<OneFish> Fishes { get; set; } = [];

    public Fishpond(List<OneFish> fishes)
    {
        Fishes = fishes;

        FishpondRect = CalculateFishpondRect();
    }

    /// <summary>
    /// </summary>
    /// <param name="result"></param>
    /// <param name="includeTarget">是否包含抛竿落点</param>
    /// <param name="ignoreObtained">是否忽略“获得”物品的图标</param>
    public Fishpond(DetectionResult result, bool includeTarget = false, bool ignoreObtained = false)
    {
        foreach (var box in result.Boxes)
        {
            Rect rect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
            if (box.Class.Name == "rod" || box.Class.Name == "err rod")
            {
                TargetRect = rect;
                continue;
            }
            else if (ignoreObtained)
            {
                // todo：特殊鱼的图标以及新获得的图标是不一样的，要特殊处理
                // todo：不是很重要但有机会可以从构造函数里分离逻辑
                // 忽略界面左侧提示的“获得”物品的图标，当上一竿获得鱼时，会对当前竿产生干扰
                // 使用估算大小和位置的方式来判断并剔除
                if (box.Bounds.Width < result.Image.Width * 0.024 && box.Bounds.Height < result.Image.Width * 0.024)
                {
                    Rect huode = new Rect((int)(0.04375 * result.Image.Width), (int)(0.4666 * result.Image.Height), (int)(0.1 * result.Image.Width), (int)(0.1 * result.Image.Width));
                    if (huode.Contains(rect))
                    {
                        continue;
                    }
                }
                // 忽略界面中央提示的“获得”物品的图标
                if (box.Bounds.Width > result.Image.Width * 0.03 && box.Bounds.Width < result.Image.Width * 0.05 &&
                    box.Bounds.Height > result.Image.Width * 0.03 && box.Bounds.Height < result.Image.Width * 0.05)
                {
                    Rect huode = new Rect((int)(0.4 * result.Image.Width), (int)(0.445 * result.Image.Height), (int)(0.2 * result.Image.Width), (int)(0.06125 * result.Image.Width));
                    if (huode.Contains(rect))
                    {
                        continue;
                    }
                }
            }
            if (includeTarget)
            {
                if (box.Class.Name == "koi")    //进入抛竿的时候只看koihead
                {
                    continue;
                }
            }

            var fish = new OneFish(box.Class.Name, rect, box.Confidence);
            Fishes.Add(fish);
        }

        // 可信度最高的鱼放在最前面
        Fishes = [.. Fishes.OrderByDescending(fish => fish.Confidence)];

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
        return [.. Fishes.Where(fish => fish.FishType.BaitName == baitName).OrderByDescending(fish => fish.Confidence)];
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
}
