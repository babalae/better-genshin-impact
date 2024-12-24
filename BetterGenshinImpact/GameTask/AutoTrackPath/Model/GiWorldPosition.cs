namespace BetterGenshinImpact.GameTask.AutoTrackPath.Model;

/// <summary>
/// 原神世界坐标
/// https://github.com/babalae/better-genshin-impact/issues/318
/// </summary>
public class GiWorldPosition
{
    /// <summary>
    /// 为这个坐标点命名
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 坐标描述
    /// </summary>
    public string? Description { get; set; }
    public string? Country { get; set; }

    /// <summary>
    /// 坐标 x,y,z 三个值，分别代表纵向、高度、横向，采用原神实际的坐标系
    /// 由于这个坐标系和一般的坐标系不同，所以为了方便理解，设这3个值为a,b,c
    ///     ▲
    ///     │a
    /// ◄───┼────
    ///   c │
    ///
    /// 值的缩放等级和1024区块坐标系的缩放一致
    /// </summary>
    public decimal[] Position { get; set; } = new decimal[3];

    public double X => (double)Position[2]; // c
    public double Y => (double)Position[0]; // a
}
