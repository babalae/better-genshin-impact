namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 大地图传送点击安全区判定（纯函数，无外部依赖，便于属性化测试）。
///
/// 背景：旧 IsPointInBigMapWindow 用"左上 360×400 + 四周 115 圈"粗糙屏蔽，
/// 把大量可点击的边缘中段误判为不可点击，导致地图边缘传送点反复 MoveMapTo
/// 却因到边界拖不动而重试耗尽失败。本类用五个与真实非地图 UI 一一对应的
/// 精确矩形替换粗糙屏蔽：点击点落入任一矩形 → 危险（不可点击），否则可点击。
/// 详见 .kiro/specs/teleport-drag-corner-ui-safezone-clamp/。
/// </summary>
public static class TeleportClickSafeZone
{
    /// <summary>单个 UI 危险矩形（1080P 基准坐标，左上原点，(X0,Y0,W,H)）。</summary>
    public readonly struct DangerRect
    {
        public readonly double X0;
        public readonly double Y0;
        public readonly double W;
        public readonly double H;

        public DangerRect(double x0, double y0, double w, double h)
        {
            X0 = x0;
            Y0 = y0;
            W = w;
            H = h;
        }
    }

    /// <summary>
    /// 五个非地图 UI 危险矩形（1080P 基准）。判定时每维度 × ratio 适配分辨率。
    /// 右上/右下矩形延伸到屏幕右缘/底缘（1920/1080），等价覆盖旧逻辑需挡住的右下边界。
    /// </summary>
    public static readonly DangerRect[] DangerRects =
    {
        new DangerRect(0,    0,   400, 430),  // 左上：任务/派蒙栏
        new DangerRect(930,  0,   990, 100),  // 右上：树脂显示（延伸到 x=1920）
        new DangerRect(1515, 929, 405, 151),  // 右下：菜单按键（延伸到 1920×1080）
        new DangerRect(0,    960, 105, 120),  // 左下：设置/图例按键
        new DangerRect(1780, 350, 140, 375),  // 右中：图层按键（竖条）
        new DangerRect(0,    0,   1920, 20),  // 顶部：整宽 20px 顶边条
    };

    /// <summary>
    /// 至冬地图专属 UI 危险矩形（1080P 基准）。仅当目标传送点 country == "至冬" 时参与判定。
    /// 至冬地图上这块区域（x=797, y=984, 宽310, 高96）存在不可点击的界面覆盖层，
    /// 传送点落点在此会被误点，故单独屏蔽。判定时每维度 × ratio 适配分辨率。
    /// </summary>
    public static readonly DangerRect[] DangerRectsSnezhnaya =
    {
        new DangerRect(797, 984, 330, 96),  // 至冬：地图 UI 覆盖层不可点击区
    };

    /// <summary>
    /// 点击点是否落在任一 UI 危险矩形内。命中判定用半开区间 [x0, x0+w) × [y0, y0+h)。
    /// </summary>
    /// <param name="clickX">点击点 X（已 × ratio 的实际捕获区坐标）。</param>
    /// <param name="clickY">点击点 Y（已 × ratio 的实际捕获区坐标）。</param>
    /// <param name="ratio">1080P 到实际分辨率的缩放系数 _zoomOutMax1080PRatio。</param>
    public static bool IsInDangerZone(double clickX, double clickY, double ratio, string? country = null)
    {
        foreach (var r in DangerRects)
        {
            if (clickX >= r.X0 * ratio && clickX < (r.X0 + r.W) * ratio
                && clickY >= r.Y0 * ratio && clickY < (r.Y0 + r.H) * ratio)
            {
                return true;
            }
        }

        // 至冬地图专属危险矩形：仅当目标传送点 country == "至冬" 时额外检查
        if (country == "至冬")
        {
            foreach (var r in DangerRectsSnezhnaya)
            {
                if (clickX >= r.X0 * ratio && clickX < (r.X0 + r.W) * ratio
                    && clickY >= r.Y0 * ratio && clickY < (r.Y0 + r.H) * ratio)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 点击点是否落在游戏窗口（屏幕）内（硬边界，无 margin）。
    /// 1080P 基准屏幕 [0,1920]×[0,1080]，判定时每维度 × ratio 适配实际分辨率。
    /// 用途：拦截"计算点击坐标越出游戏窗口"的点——多屏环境下越界点会打到相邻屏幕的其他程序（如 QQ）。
    /// 【关键】只用硬边界、绝不加 margin：加 margin 会把大量贴边合法传送点误判不可点，
    /// 导致每次传送多拖 1~2 轮拖慢。贴边但仍在窗口内的点必须保持可点。
    /// </summary>
    /// <param name="clickX">点击点 X（已 × ratio 的实际捕获区坐标）。</param>
    /// <param name="clickY">点击点 Y（已 × ratio 的实际捕获区坐标）。</param>
    /// <param name="ratio">1080P 到实际分辨率的缩放系数。</param>
    public static bool IsWithinScreen(double clickX, double clickY, double ratio)
        => clickX >= 0 && clickX <= ScreenWidth1080 * ratio
        && clickY >= 0 && clickY <= ScreenHeight1080 * ratio;

    /// <summary>
    /// 点击点是否可安全点击：必须落在游戏窗口内，且不落在任何 UI 危险矩形内。
    /// </summary>
    public static bool IsClickable(double clickX, double clickY, double ratio, string? country = null)
        => IsWithinScreen(clickX, clickY, ratio) && !IsInDangerZone(clickX, clickY, ratio, country);

    // ===== 早停优化（teleport-drag-early-stop-when-clickable spec）=====
    // 快速拖动定位时，传送点一旦落在含 margin 的可点击安全区就提前 break 去点击，
    // 不必拖到屏幕正中心，减少冗余拖动轮数。以下成员为该 spec 新增，不改动上面既有成员。

    /// <summary>屏幕基准宽（1080P）。用于早停判据的屏幕内缘内缩计算。</summary>
    public const double ScreenWidth1080 = 1920;

    /// <summary>屏幕基准高（1080P）。</summary>
    public const double ScreenHeight1080 = 1080;

    /// <summary>
    /// 早停判据默认安全余量（40px @1080P）。取 40 的理由：既能覆盖"中心点识别 + 几何推算"
    /// 相对最终模板匹配点击路径（ConvertToGameRegionPosition + IsPointInBigMapWindow）的几像素误差，
    /// 又不过度牺牲提前量（相对屏幕 1920×1080 仅约 2~4% 收缩）。
    /// </summary>
    public const double DefaultEarlyStopMargin = 40;

    /// <summary>
    /// 含 margin 的保守危险区判定：在裸 IsInDangerZone 基础上，
    /// (1) 五个危险矩形各向四周外扩 margin；
    /// (2) 额外要求点内缩到屏幕内缘 margin 范围内，贴屏幕外缘 margin 内也算危险。
    /// 用于早停判据，吸收几何推算与最终点击路径的误差，避免早停到危险区/屏幕边缘。
    /// </summary>
    /// <param name="clickX">推算点 X（已 × ratio；早停用 ratio=1.0，即 1080P 空间）。</param>
    /// <param name="clickY">推算点 Y（已 × ratio）。</param>
    /// <param name="ratio">1080P 到实际分辨率的缩放系数（早停传 1.0）。</param>
    /// <param name="margin">安全余量（1080P 基准像素）。</param>
    public static bool IsInDangerZoneWithMargin(double clickX, double clickY, double ratio, double margin, string? country = null)
    {
        // (2) 屏幕外缘内缩：贴屏幕四边 margin 内视为危险
        if (clickX < margin * ratio
            || clickY < margin * ratio
            || clickX > (ScreenWidth1080 - margin) * ratio
            || clickY > (ScreenHeight1080 - margin) * ratio)
        {
            return true;
        }

        // (1) 五矩形各外扩 margin
        foreach (var r in DangerRects)
        {
            if (clickX >= (r.X0 - margin) * ratio && clickX < (r.X0 + r.W + margin) * ratio
                && clickY >= (r.Y0 - margin) * ratio && clickY < (r.Y0 + r.H + margin) * ratio)
            {
                return true;
            }
        }

        // (3) 至冬地图专属矩形各外扩 margin（仅当 country == "至冬"）
        if (country == "至冬")
        {
            foreach (var r in DangerRectsSnezhnaya)
            {
                if (clickX >= (r.X0 - margin) * ratio && clickX < (r.X0 + r.W + margin) * ratio
                    && clickY >= (r.Y0 - margin) * ratio && clickY < (r.Y0 + r.H + margin) * ratio)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 点击点是否"可安全点击"（含 margin 保守版）：不落在任何外扩危险矩形内、且不贴屏幕外缘。
    /// </summary>
    public static bool IsSafelyClickable(double clickX, double clickY, double ratio, double margin, string? country = null)
        => !IsInDangerZoneWithMargin(clickX, clickY, ratio, margin, country);

    /// <summary>
    /// 快速拖动模式早停决策（纯函数，便于 PBT）。仅在快速拖动模式生效；
    /// 经典模式或非法缩放一律返回 false（早停分支恒不触发，走原收工逻辑）。
    ///
    /// 坐标正变换（world → 1080P screen，减号式，与 AutoLeyLineOutcropTask 逆变换严格反解一致）：
    ///   clickX = 960 - mapScaleFactor * (x - centerX) / currentZoomLevel
    ///   clickY = 540 - mapScaleFactor * (y - centerY) / currentZoomLevel
    /// 符号方向由真机集成验证最终确认（详见 spec design.md §Overview / §二次守门）。
    /// </summary>
    /// <param name="mapMoveStepDivisor">是否快速拖动模式（茶包快速拖动恒传 true）。</param>
    /// <param name="x">传送点原神世界 X 坐标。</param>
    /// <param name="y">传送点原神世界 Y 坐标。</param>
    /// <param name="centerX">当轮 mapCenterPoint.X（屏幕正中心对应世界坐标）。</param>
    /// <param name="centerY">当轮 mapCenterPoint.Y。</param>
    /// <param name="mapScaleFactor">_tpConfig.MapScaleFactor。</param>
    /// <param name="currentZoomLevel">当前大地图缩放档，必须 > 0。</param>
    /// <param name="margin">安全余量（1080P 基准像素）。</param>
    public static bool ShouldEarlyStopClick(
        bool mapMoveStepDivisor, double x, double y,
        double centerX, double centerY,
        double mapScaleFactor, double currentZoomLevel, double margin, string? country = null)
    {
        if (!mapMoveStepDivisor) return false;      // 经典模式：早停恒不触发，逐字节保留原逻辑
        if (currentZoomLevel <= 0) return false;    // 防除零安全兜底

        double clickX = 960 - mapScaleFactor * (x - centerX) / currentZoomLevel;
        double clickY = 540 - mapScaleFactor * (y - centerY) / currentZoomLevel;

        // 早停在 1080P 空间自洽判定，ratio = 1.0（五矩形本就 1080P 基准）
        return IsSafelyClickable(clickX, clickY, 1.0, margin, country);
    }
}
