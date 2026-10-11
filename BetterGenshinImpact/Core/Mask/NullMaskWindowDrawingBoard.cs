using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 空实现，用于单测以及不挂在截图区域下的 Region
/// </summary>
public sealed class NullMaskWindowDrawingBoard : IMaskWindowDrawingBoard
{
    public static NullMaskWindowDrawingBoard Instance { get; } = new();

    private NullMaskWindowDrawingBoard()
    {
    }

    public void Set(MaskWindowDrawingGroup group, IReadOnlyList<MaskWindowDrawingShape>? shapes)
    {
    }

    public void Clear(MaskWindowDrawingGroup group)
    {
    }

    public void ClearAll()
    {
    }
}
