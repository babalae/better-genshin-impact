using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleOCR;
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
    /// <summary>
    /// find资源
    /// </summary>
    private readonly AutoFightAssets _assets = new();

    /// <summary>
    /// 通过OCR识别队伍内角色
    /// </summary>
    /// <param name="content">完整游戏画面的捕获截图</param>
    public void InitializeTeam(CaptureContent content)
    {
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        // 剪裁出队伍区域
        var teamRa = content.CaptureRectArea.Crop(new Rect(captureRect.Width - (int)(355 * assetScale), (int)(220 * assetScale),
            (int)(355 * assetScale), (int)(465 * assetScale)));

        // 识别队伍内角色
        var result = OcrFactory.Paddle.OcrResult(teamRa.SrcGreyMat);
        ParseTeamOcrResult(result, teamRa);
    }

    private void ParseTeamOcrResult(PaddleOcrResult result, RectArea rectArea)
    {
        List<string> names = (from item in result.Regions where DefaultAutoFightConfig.CombatAvatarNames.Contains(item.Text) select item.Text).ToList();

        if (names.Count < 3 || names.Count > 5)
        {
            Logger.LogWarning("识别到的队伍角色数量不正确，当前识别结果:{Text}", string.Join(",", names));
        }


        if (names.Count == 3)
        {
            // 4人以上的队伍，不支持流浪者的识别
            var wanderer = rectArea.Find(_assets.WandererIconRa);
            if (wanderer.IsEmpty())
            {
                Logger.LogWarning("识别到的队伍角色数量不正确，当前识别结果:{Text}", string.Join(",", names));
            }
            else
            {
                names.Clear();
                foreach (var item in result.Regions)
                {
                    if (DefaultAutoFightConfig.CombatAvatarNames.Contains(item.Text))
                    {
                        names.Add(item.Text);
                    }

                    var rect = item.Rect.BoundingRect();
                    if (rect.Y > wanderer.Y && wanderer.Y + wanderer.Height > rect.Y+ rect.Height && StringUtils.IsChinese(item.Text))
                    {
                        names.Add(item.Text);
                    }
                }

                if (names.Count != 4)
                {
                    Logger.LogWarning("尝试识别流浪者失败，请确认流浪者的自定义名称不含有特殊字符");
                }
            }
        }
        Logger.LogInformation("识别到的队伍角色:{Text}", string.Join(",", names));
        Avatars = BuildAvatars(names);
    }

    private Avatar[] BuildAvatars(List<string> names)
    {
        var avatars = new Avatar[5];
        for (var i = 0; i < names.Count; i++)
        {
            avatars[i] = new Avatar(names[i], i);
        }

        return avatars;
    }
}