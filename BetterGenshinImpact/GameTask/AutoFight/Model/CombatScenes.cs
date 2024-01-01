using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using System;
using System.Collections.Generic;
using System.Threading;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 战斗场景
/// </summary>
public class CombatScenes
{
    /// <summary>
    /// 当前配队
    /// </summary>
    public Avatar[] Avatars { get; set; } = new Avatar[5];

    public int AvatarCount { get; set; }

    /// <summary>
    /// 通过OCR识别队伍内角色
    /// </summary>
    /// <param name="content">完整游戏画面的捕获截图</param>
    public CombatScenes InitializeTeam(CaptureContent content)
    {
        // 剪裁出队伍区域
        var teamRa = content.CaptureRectArea.Crop(AutoFightContext.Instance().FightAssets.TeamRect);
        // 过滤出白色
        var hsvFilterMat = OpenCvCommonHelper.InRangeHsv(teamRa.SrcMat, new Scalar(0, 0, 210),new Scalar(255, 30, 255));

        // 识别队伍内角色
        var result = OcrFactory.Paddle.OcrResult(hsvFilterMat);
        ParseTeamOcrResult(result, teamRa);
        return this;
    }

    private void ParseTeamOcrResult(PaddleOcrResult result, RectArea rectArea)
    {
        List<string> names = new();
        List<Rect> nameRects = new();
        foreach (var item in result.Regions)
        {
            var name = StringUtils.ExtractChinese(item.Text);
            if (IsGenshinAvatarName(name))
            {
                names.Add(name);
                nameRects.Add(item.Rect.BoundingRect());
            }
        }

        if (names.Count < 3 || names.Count > 5)
        {
            Logger.LogWarning("识别到的队伍角色数量不正确，当前识别结果:{Text}", string.Join(",", names));
            throw new Exception("队伍识别失败");
        }


        if (names.Count == 3)
        {
            // 流浪者特殊处理
            // 4人以上的队伍，不支持流浪者的识别
            var wanderer = rectArea.Find(AutoFightContext.Instance().FightAssets.WandererIconRa);
            if (wanderer.IsEmpty())
            {
                Logger.LogWarning("识别到的队伍角色数量不正确，当前识别结果:{Text}", string.Join(",", names));
                throw new Exception("队伍识别失败");
            }
            else
            {
                names.Clear();
                foreach (var item in result.Regions)
                {
                    var name = StringUtils.ExtractChinese(item.Text);
                    if (IsGenshinAvatarName(name))
                    {
                        names.Add(name);
                        nameRects.Add(item.Rect.BoundingRect());
                    }

                    var rect = item.Rect.BoundingRect();
                    if (rect.Y > wanderer.Y && wanderer.Y + wanderer.Height > rect.Y + rect.Height)
                    {
                        names.Add("流浪者");
                        nameRects.Add(item.Rect.BoundingRect());
                    }
                }

                if (names.Count != 4)
                {
                    Logger.LogWarning("尝试识别流浪者失败，请确认流浪者的自定义名称不含有特殊字符");
                }
            }
        }

        Logger.LogInformation("识别到的队伍角色:{Text}", string.Join(",", names));
        Avatars = BuildAvatars(names, nameRects);
    }

    private bool IsGenshinAvatarName(string name)
    {
        if (DefaultAutoFightConfig.CombatAvatarNames.Contains(name))
        {
            return true;
        }
        return false;
    }

    private Avatar[] BuildAvatars(List<string> names, List<Rect> nameRects)
    {
        AvatarCount = names.Count;
        var avatars = new Avatar[5];
        for (var i = 0; i < AvatarCount; i++)
        {
            avatars[i] = new Avatar(names[i], i + 1, nameRects[i]);
        }
       
        return avatars;
    }

    public void BeforeTask(CancellationTokenSource cts)
    {
        for (var i = 0; i < AvatarCount; i++)
        {
            Avatars[i].Cts = cts;
        }

    }
}