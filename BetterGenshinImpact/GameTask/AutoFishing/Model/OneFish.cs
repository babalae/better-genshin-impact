using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCvSharp;


namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

public class OneFish
{

    public FishType FishType { get; set; }

    public Rect Rect { get; set; }

    public OneFish(string name, Rect rect)
    {
        FishType = FishType.FromName(name);
        Rect = rect;
    }
}