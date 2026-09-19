using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.View.Drawable;
using System;
using System.Collections.Generic;
using Point = System.Windows.Point;

namespace BetterGenshinImpact.GameTask.SkillCd;

/// <summary>
/// 技能 CD 遮罩渲染器：按 SkillCd 模块的用户配置（坐标、Gap、HideWhenZero）渲染全队 CD 文字
/// 供不同模块复用，显示样式统一跟随 SkillCdConfig
/// </summary>
public static class SkillCdOverlayRenderer
{
    /// <summary>
    /// 渲染/清理 CD 遮罩文字
    /// </summary>
    /// <param name="drawKey">遮罩文字 key，调用方各自持有并负责清理</param>
    /// <param name="slotCds">固定 4 槽的 CD 秒数：null 不绘制；NaN 表示未知，显示"?"；其余显示 F1 格式</param>
    public static void Update(string drawKey, double?[] slotCds)
    {
        var drawContent = VisionContext.Instance().DrawContent;
        var systemInfo = TaskContext.Instance().SystemInfo;
        var captureRect = systemInfo.ScaleMax1080PCaptureRect;
        var sideRects = AutoFightAssets.Get(captureRect.Width, captureRect.Height).AvatarSideIconRectList;
        var config = TaskContext.Instance().Config.SkillCdConfig;

        if (sideRects == null || sideRects.Count < 4)
        {
            drawContent.PutOrRemoveTextList(drawKey, null);
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

        var textList = new List<TextDrawable>();

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
            textList.Add(new TextDrawable(text, new Point(px, py)));
        }

        if (textList.Count == 0) drawContent.PutOrRemoveTextList(drawKey, null);
        else drawContent.PutOrRemoveTextList(drawKey, textList);
    }
}
