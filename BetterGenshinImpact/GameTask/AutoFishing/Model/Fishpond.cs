using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Compunet.YoloSharp.Data;

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
    public Fishpond(YoloResult<Detection> result, bool includeTarget = false, bool ignoreObtained = false)
    {
        Print(result);
        foreach (var box in result)
        {
            // 可信度太低的直接放弃
            if (box.Confidence < 0.4)
            {
                continue;
            }
            
            Rect rect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
            if (box.Name.Name == "rod" || box.Name.Name == "err rod")
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
                if (box.Bounds.Width < result.ImageSize.Width * 0.036 && box.Bounds.Height < result.ImageSize.Width * 0.036)
                {
                    Rect huode = new Rect((int)(0.04375 * result.ImageSize.Width), (int)(0.4666 * result.ImageSize.Height), (int)(0.1 * result.ImageSize.Width), (int)(0.1 * result.ImageSize.Width));
                    if (huode.Contains(rect))
                    {
                        continue;
                    }
                }
                // 忽略界面中央提示的“获得”物品的图标
                if (box.Bounds.Width > result.ImageSize.Width * 0.03 && box.Bounds.Width < result.ImageSize.Width * 0.06 &&
                    box.Bounds.Height > result.ImageSize.Width * 0.03 && box.Bounds.Height < result.ImageSize.Width * 0.06)
                {
                    Rect huode = new Rect((int)(0.4 * result.ImageSize.Width), (int)(0.445 * result.ImageSize.Height), (int)(0.2 * result.ImageSize.Width), (int)(0.06125 * result.ImageSize.Width));
                    if (huode.Contains(rect))
                    {
                        continue;
                    }
                }
            }
            if (includeTarget)
            {
                if (box.Name.Name == "koi")    //进入抛竿的时候只看koihead
                {
                    continue;
                }
            }

            var fish = new OneFish(box.Name.Name, rect, box.Confidence);
            Fishes.Add(fish);
        }

        // 可信度最高的鱼放在最前面
        Fishes = [.. Fishes.OrderByDescending(fish => fish.Confidence)];

        FishpondRect = CalculateFishpondRect();
    }
    
    private void Print(YoloResult<Detection> result)
    {
        Debug.Write("鱼塘YOLO识别结果：");
        foreach (var box in result)
        {
            Debug.Write(box.ToString());
        }
        Debug.WriteLine("");
    }

    /// <summary>
    /// 计算鱼塘位置
    /// </summary>
    /// <returns></returns>
    public Rect CalculateFishpondRect()
    {
        if (Fishes.Count == 0)
        {
            return default;
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
}
