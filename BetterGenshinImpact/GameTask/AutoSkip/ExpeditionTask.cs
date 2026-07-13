using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoSkip;

/// <summary>
/// 重新探索派遣
///
/// 必须在已经有探索派遣完成的情况下使用
///
/// 于 4.3 版本废弃
/// </summary>
[Obsolete]
public class ExpeditionTask
{
    private static readonly List<string> ExpeditionCharacterList = [];

    private int _expeditionCount = 0;

    public void Run(CaptureContent content)
    {
        InitConfig();
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        ReExplorationGameArea(content);
        for (var i = 0; i <= 4; i++)
        {
            if (_expeditionCount >= 5)
            {
                // 最多派遣5人
                break;
            }
            else
            {
                content.CaptureRectArea
                    .Derive(new Rect((int)(110 * assetScale), (int)((145 + 70 * i) * assetScale),
                        (int)(60 * assetScale), (int)(33 * assetScale)))
                    .Click();
                TaskControl.Sleep(500);
                ReExplorationGameArea(content);
            }
        }

        TaskControl.Logger.LogInformation("探索派遣：{Text}", "重新派遣完成");
        VisionContext.Instance().DrawContent.ClearAll();
    }

    private void InitConfig()
    {
        var str = TaskContext.Instance().Config.AutoSkipConfig.AutoReExploreCharacter;
        if (!string.IsNullOrEmpty(str))
        {
            ExpeditionCharacterList.Clear();
            str = str.Replace("，", ",");
            str.Split(',').ToList().ForEach(x => ExpeditionCharacterList.Add(x.Trim()));
            TaskContext.Instance().Config.AutoSkipConfig.AutoReExploreCharacter =
                string.Join(",", ExpeditionCharacterList);
        }
    }

    private void ReExplorationGameArea(CaptureContent content)
    {
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        for (var i = 0; i < 5; i++)
        {
            var result = CaptureAndOcr(content,
                new Rect(0, 0, captureRect.Width - (int)(480 * assetScale), captureRect.Height));
            var rect = result.FindRectByText("探险完成");
            // TODO i>1 的时候,可以通过关键词“探索派遣限制 4 / 5 ”判断是否已经派遣完成？
            if (rect != default)
            {
                // 点击探险完成下方的人物头像
                content.CaptureRectArea
                    .Derive(new Rect(rect.X, rect.Y + (int)(50 * assetScale), rect.Width, (int)(80 * assetScale)))
                    .Click();
                TaskControl.Sleep(100);
                // 重新截图 找领取
                result = CaptureAndOcr(content);
                rect = result.FindRectByText("领取");
                if (rect != default)
                {
                    using var ra = content.CaptureRectArea.Derive(rect);
                    ra.Click();
                    //TaskControl.Logger.LogInformation("探索派遣：点击{Text}", "领取");
                    TaskControl.Sleep(200);
                    // 点击空白区域继续
                    ra.Click();
                    TaskControl.Sleep(250);

                    // 选择角色
                    result = CaptureAndOcr(content);
                    rect = result.FindRectByText("选择角色");
                    if (rect != default)
                    {
                        content.CaptureRectArea.Derive(rect).Click();
                        TaskControl.Sleep(400); // 等待动画
                        var success = SelectCharacter(content);
                        if (success)
                        {
                            _expeditionCount++;
                        }
                    }
                }
                else
                {
                    TaskControl.Logger.LogWarning("探索派遣：找不到 {Text} 文字", "领取");
                }
            }
            else
            {
                break;
            }
        }
    }

    private bool SelectCharacter(CaptureContent content)
    {
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var result = CaptureAndOcr(content, new Rect(0, 0, captureRect.Width / 2, captureRect.Height));
        if (result.RegionHasText("角色选择"))
        {
            var cards = GetCharacterCards(result);
            if (cards.Count > 0)
            {
                var card = cards.FirstOrDefault(c =>
                    c.Idle && c.Name != null && ExpeditionCharacterList.Contains(c.Name)) ?? cards.First(c => c.Idle);
                var rect = card.Rects.First();

                using var ra = content.CaptureRectArea.Derive(rect);
                ra.Click();
                TaskControl.Logger.LogInformation("探索派遣：派遣 {Name}", card.Name);
                TaskControl.Sleep(500);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 根据文字识别结果 获取所有角色选项
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    private List<ExpeditionCharacterCard> GetCharacterCards(OcrResult result)
    {
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        var ocrResultRects = result.Regions
            .Select(x => x.ToOcrResultRect())
            .Where(r => r.Rect.X + r.Rect.Width < captureRect.Width / 2)
            .OrderBy(r => r.Rect.Y)
            .ThenBy(r => r.Rect.X)
            .ToList();

        var cards = new List<ExpeditionCharacterCard>();
        foreach (var ocrResultRect in ocrResultRects)
        {
            if (ocrResultRect.Text.Contains("时间缩短") || ocrResultRect.Text.Contains("奖励增加") ||
                ocrResultRect.Text.Contains("暂无加成"))
            {
                var card = new ExpeditionCharacterCard();
                card.Rects.Add(ocrResultRect.Rect);
                card.Addition = ocrResultRect.Text;
                foreach (var ocrResultRect2 in ocrResultRects)
                {
                    if (ocrResultRect2.Rect.Y > ocrResultRect.Rect.Y - 50 * assetScale
                        && ocrResultRect2.Rect.Y + ocrResultRect2.Rect.Height <
                        ocrResultRect.Rect.Y + ocrResultRect.Rect.Height)
                    {
                        if (ocrResultRect2.Text.Contains("探险完成") || ocrResultRect2.Text.Contains("探险中"))
                        {
                            card.Idle = false;
                            var name = ocrResultRect2.Text.Replace("探险完成", "").Replace("探险中", "").Replace("/", "")
                                .Trim();
                            if (!string.IsNullOrEmpty(name))
                            {
                                card.Name = name;
                            }
                        }
                        else if (!ocrResultRect2.Text.Contains("时间缩短") && !ocrResultRect2.Text.Contains("奖励增加") &&
                                 !ocrResultRect2.Text.Contains("暂无加成"))
                        {
                            card.Name = ocrResultRect2.Text;
                        }

                        card.Rects.Add(ocrResultRect2.Rect);
                    }
                }

                if (!string.IsNullOrEmpty(card.Name))
                {
                    cards.Add(card);
                }
                else
                {
                    TaskControl.Logger.LogWarning("探索派遣：存在未找到角色命的识别内容");
                }
            }
        }

        return cards;
    }

    private readonly Pen _pen = new(Color.Red, 1);

    private OcrResult CaptureAndOcr(CaptureContent content)
    {
        using var ra = TaskControl.CaptureToRectArea();
        var result = OcrFactory.Paddle.OcrResult(ra.CacheGreyMat);
        //VisionContext.Instance().DrawContent.PutOrRemoveRectList("OcrResultRects", result.ToRectDrawableList(_pen));
        return result;
    }

    private OcrResult CaptureAndOcr(CaptureContent content, Rect rect)
    {
        using var ra = TaskControl.CaptureToRectArea();
        var result = OcrFactory.Paddle.OcrResult(ra.CacheGreyMat);
        //VisionContext.Instance().DrawContent.PutOrRemoveRectList("OcrResultRects", result.ToRectDrawableList(_pen));
        return result;
    }
}