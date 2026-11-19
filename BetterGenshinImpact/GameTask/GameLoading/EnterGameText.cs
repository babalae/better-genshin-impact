using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Localization;

namespace BetterGenshinImpact.GameTask.GameLoading;

/// <summary>
/// 非中文游戏语言下用 OCR 识别“点击进入”（如葡萄牙语“Pressione para entrar”）。
/// 原有图片模板是中文文字，其他语言无法匹配。
/// </summary>
public static class EnterGameText
{
    /// <summary>
    /// 资源 key：开门界面“点击进入”的文字
    /// </summary>
    private const string EnterGameKey = "点击进入";

    /// <summary>
    /// 按游戏语言创建“点击进入”的匹配规则；中文或没有翻译时返回 null，继续只用图片模板
    /// </summary>
    /// <param name="localizer">自动开门的资源本地化器</param>
    /// <param name="gameCulture">游戏语言</param>
    public static Regex? CreatePattern(IStringLocalizer<GameLoadingTrigger> localizer, CultureInfo gameCulture)
    {
        if (gameCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var text = localizer.WithCultureGet(gameCulture, EnterGameKey);
        if (string.IsNullOrWhiteSpace(text) || text == EnterGameKey)
        {
            return null;
        }

        return new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// 在屏幕下半部中间区域（与原模板识别范围一致）查找“点击进入”文字
    /// </summary>
    /// <param name="regions">整屏 OCR 结果</param>
    /// <param name="width">截图宽度</param>
    /// <param name="height">截图高度</param>
    /// <param name="pattern">匹配规则</param>
    /// <returns>找到的文字区域，没有时为 null</returns>
    public static Region? Find(IEnumerable<Region> regions, int width, int height, Regex pattern)
    {
        return regions.FirstOrDefault(r =>
        {
            var centerX = r.X + r.Width / 2.0;
            var centerY = r.Y + r.Height / 2.0;
            return centerX >= width / 3.0 && centerX <= width * 2 / 3.0
                   && centerY >= height / 2.0
                   && !string.IsNullOrWhiteSpace(r.Text)
                   && pattern.IsMatch(r.Text);
        });
    }
}
