using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// SwitchArea 地区菜单模板匹配判定（纯函数，便于单元测试 / PBT，无 UI / Mat / logger / 全局状态依赖）。
/// 模板匹配优先、OCR 兜底：模板命中（得分达标）才采用模板结果，否则回落现有 OCR 分支。
/// </summary>
public static class SwitchAreaTemplateMatchDecisions
{
    /// <summary>
    /// 模板是否命中（得分 &gt;= 阈值）。阈值默认 0.8。
    /// </summary>
    public static bool IsHit(double score, double threshold = 0.8)
        => score >= threshold;

    /// <summary>
    /// 命中时的点击坐标（模板匹配返回的 Rect 中心）。
    /// </summary>
    public static (double X, double Y) GetClickPoint(double rectX, double rectY, double rectW, double rectH)
        => (rectX + rectW / 2d, rectY + rectH / 2d);

    /// <summary>
    /// 判定本次模板匹配是否应当被采用：模板可用且得分达标即采用，否则回落 OCR。
    /// </summary>
    public static bool ShouldUseTemplateMatch(bool templateAvailable, double score, double threshold = 0.8)
        => templateAvailable && score >= threshold;
}

/// <summary>
/// 地区菜单 2 列 × 8 行网格布局（全屏 1920×1080，绝对屏幕坐标实测标定）。
/// 背景：整块右 1/3 做模板匹配时无关背景太多，CCoeffNormed 最高分被稀释到阈值以下，实际总掉进 OCR。
/// 改为把搜索区缩小到"每个按键那一格"，格内背景干净（就一条 2 字模板），匹配度显著提升。
///
/// 关键前提（用户确认）：16 个格子位置固定不动（2×8 网格、无滚动/翻页），但**模板不绑定格子**——
/// 菜单每次显示的地区数量可变（1~16 个）、顺序可能乱，任意地区模板可能出现在任意一格。
/// 因此本布局只负责给出 16 个格子的坐标（纯几何），"哪个格子用哪个模板匹配"由调用方轮询决定，
/// 不做任何"模板 → 固定格子"的映射。
///
/// 坐标标定（用户 1920×1080 实测，绝对屏幕坐标，RegionOfInterest 使用相对截图左上角坐标）：
/// 起点（蒙德）(1326, 120)，列步长 StepX=300（右列 1626 - 左列 1326），行步长 StepY=105（225 - 120），
/// 格尺寸 66 × 38（模板为地区名前 2 字）。排布：col = index % 2（0 左列 / 1 右列），row = index / 2（0 第一行 … 7 第八行）。
/// 纯函数：无 IO、无副作用、无全局状态，便于单元测试 / PBT。
/// </summary>
public static class SwitchAreaMenuGridLayout
{
    /// <summary>网格总列数（2 列）。</summary>
    public const int Columns = 2;

    /// <summary>网格总行数（8 行）。</summary>
    public const int Rows = 8;

    /// <summary>网格总格数（16）。</summary>
    public const int CellCount = Columns * Rows;

    /// <summary>起点（蒙德）X 绝对坐标。</summary>
    public const double StartX = 1326;

    /// <summary>起点（蒙德）Y 绝对坐标（用户 1920×1080 实测第一行第一个按键 Y=120）。</summary>
    public const double StartY = 120;

    /// <summary>相邻两列（左列→右列）的 X 步长（像素）。</summary>
    public const double StepX = 300;

    /// <summary>相邻两行（上行→下行）的 Y 步长（像素）。</summary>
    public const double StepY = 105;

    /// <summary>单格（按键）搜索框宽度（像素），容纳 2 字模板。</summary>
    public const int CellWidth = 66;

    /// <summary>单格（按键）搜索框高度（像素）。</summary>
    public const int CellHeight = 38;

    /// <summary>
    /// 计算第 <paramref name="index"/> 个格子的搜索框（相对截图左上角的绝对坐标）。
    /// 排布：col = index % Columns（0 左列 / 1 右列），row = index / Columns（0 第一行 … 7 第八行）。
    /// 注意：格子位置固定，但"哪个地区模板位于该格"不固定，由调用方轮询匹配决定。
    /// </summary>
    /// <param name="index">格子序号（0..15）。</param>
    public static Rect GetCellRect(int index)
    {
        var col = index % Columns;
        var row = index / Columns;
        var x = (int)Math.Round(StartX + col * StepX);
        var y = (int)Math.Round(StartY + row * StepY);
        return new Rect(x, y, CellWidth, CellHeight);
    }

    /// <summary>
    /// 返回全部 16 个格子的搜索框（顺序 = index 0..15，col=index%2 左/右列、row=index/2 上/下行）。
    /// 调用方遍历这些格子，用目标地区模板逐格匹配，找出目标当前所在的格子。
    /// </summary>
    public static IReadOnlyList<Rect> GetAllCellRects()
    {
        var cells = new Rect[CellCount];
        for (var i = 0; i < CellCount; i++)
        {
            cells[i] = GetCellRect(i);
        }
        return cells;
    }
}
