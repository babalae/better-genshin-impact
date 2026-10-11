using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.Helpers.Ui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace BetterGenshinImpact.GameTask.SkillCd;

/// <summary>
/// 技能 CD 遮罩渲染器：按 SkillCd 模块的用户配置（坐标、Gap、HideWhenZero、颜色、缩放）生成全队 CD 文字。
/// 供不同模块复用，样式在这里算好后写进图形，遮罩窗口的绘制层不需要认识技能 CD。
/// </summary>
public static class SkillCdOverlayRenderer
{
    /// <summary>
    /// 1080P 下技能 CD 文字的字号（捕获像素）
    /// </summary>
    private const double BaseFontSize1080P = 26;

    /// <summary>
    /// CD 小于该值时视为就绪，使用就绪配色
    /// </summary>
    private const double ReadyThresholdSeconds = 0.8;

    private static readonly Color DefaultReadyTextColor = Color.FromRgb(93, 204, 23);
    private static readonly Color DefaultNormalTextColor = Color.FromRgb(218, 74, 35);

    /// <summary>
    /// 技能 CD 文字属于功能提示，不受「在遮罩上显示识别结果」开关影响
    /// </summary>
    public static MaskWindowDrawingGroup CreateGroup(string drawKey)
    {
        return new MaskWindowDrawingGroup(drawKey, MaskWindowDrawingKind.Feature);
    }

    /// <summary>
    /// 渲染/清理 CD 遮罩文字
    /// </summary>
    /// <param name="drawingBoard">遮罩窗口绘制入口</param>
    /// <param name="drawKey">遮罩文字分组名，调用方各自持有并负责清理</param>
    /// <param name="slotCds">固定 4 槽的 CD 秒数：null 不绘制；NaN 表示未知，显示"?"；其余显示 F1 格式</param>
    public static void Update(IMaskWindowDrawingBoard drawingBoard, string drawKey, double?[] slotCds)
    {
        var group = CreateGroup(drawKey);
        var systemInfo = TaskContext.Instance().SystemInfo;
        var captureRect = systemInfo.ScaleMax1080PCaptureRect;
        var sideRects = AutoFightAssets.Get(captureRect.Width, captureRect.Height).AvatarSideIconRectList;
        var config = TaskContext.Instance().Config.SkillCdConfig;

        if (sideRects == null || sideRects.Count < 4)
        {
            drawingBoard.Clear(group);
            return;
        }

        double factor = (double)systemInfo.GameScreenSize.Width / systemInfo.ScaleMax1080PCaptureRect.Width;

        // 使用配置中的坐标（保留一位小数）
        double userPX = Math.Round(config.PX, 1);
        double userPY = Math.Round(config.PY, 1);
        double userGap = Math.Round(config.Gap, 1);

        double basePx = userPX * factor;
        double basePy = userPY * factor;
        double intervalY = userGap * factor;

        var fontSize = BaseFontSize1080P * systemInfo.ScaleTo1080PRatio * config.Scale;
        var readyStyle = new MaskWindowDrawingTextStyle(
            OverlayStyleHelper.ParseRgbaHexColor(config.TextReadyColor) ?? DefaultReadyTextColor,
            fontSize,
            OverlayStyleHelper.ParseRgbaHexColor(config.BackgroundReadyColor) ?? Colors.White,
            Numeric: true);
        var normalStyle = new MaskWindowDrawingTextStyle(
            OverlayStyleHelper.ParseRgbaHexColor(config.TextNormalColor) ?? DefaultNormalTextColor,
            fontSize,
            OverlayStyleHelper.ParseRgbaHexColor(config.BackgroundNormalColor) ?? Colors.White,
            Numeric: true);

        var texts = new List<MaskWindowDrawingShape>(4);

        for (int i = 0; i < 4 && i < slotCds.Length; i++)
        {
            var cd = slotCds[i];
            if (cd == null)
            {
                continue;
            }

            // 如果启用了"冷却为0时隐藏"，且CD为0，则跳过
            if (config.HideWhenZero && cd.Value <= 0)
            {
                continue;
            }

            var px = basePx;
            var py = basePy + intervalY * i;

            var text = double.IsNaN(cd.Value) ? "?" : cd.Value.ToString("F1");
            // 按显示出来的数值判断是否就绪，与文字保持一致
            var isReady = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var shown)
                          && Math.Abs(shown) < ReadyThresholdSeconds;
            texts.Add(new MaskWindowDrawingText(text, new Point(px, py), isReady ? readyStyle : normalStyle));
        }

        drawingBoard.Set(group, texts);
    }
}
