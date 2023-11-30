using BetterGenshinImpact.Helpers.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Helpers;

public class ClickOffset
{
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public double AssetScale { get; set; }


    public ClickOffset(int offsetX, int offsetY, double assetScale)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        AssetScale = assetScale;
    }

    public void Click(int x, int y)
    {
        ClickExtension.Click(OffsetX + (int)(x * AssetScale), OffsetY + (int)(y * AssetScale));
    }

    public void ClickWithoutScale(int x, int y)
    {
        ClickExtension.Click(OffsetX + x, OffsetY + y);
    }

    public void Move(int x, int y)
    {
        ClickExtension.Move(OffsetX + (int)(x * AssetScale), OffsetY + (int)(y * AssetScale));
    }
}