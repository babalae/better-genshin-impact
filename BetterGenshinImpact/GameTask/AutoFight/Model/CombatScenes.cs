using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
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
    public Avatar[] Avatars { get; set; } = new Avatar[1];

    public Dictionary<string, Avatar> AvatarMap { get; set; } = new();

    public int AvatarCount { get; set; }

    /// <summary>
    /// 通过OCR识别队伍内角色
    /// </summary>
    /// <param name="content">完整游戏画面的捕获截图</param>
    public CombatScenes InitializeTeam(CaptureContent content)
    {
        // 优先取配置
        if (!string.IsNullOrEmpty(TaskContext.Instance().Config.AutoFightConfig.TeamNames))
        {
            InitializeTeamFromConfig(TaskContext.Instance().Config.AutoFightConfig.TeamNames);
            return this;
        }

        // 剪裁出队伍区域
        var teamRa = content.CaptureRectArea.Crop(AutoFightContext.Instance().FightAssets.TeamRectNoIndex);
        // 过滤出白色
        var hsvFilterMat = OpenCvCommonHelper.InRangeHsv(teamRa.SrcMat, new Scalar(0, 0, 210), new Scalar(255, 30, 255));

        // 识别队伍内角色
        var result = OcrFactory.Paddle.OcrResult(hsvFilterMat);
        ParseTeamOcrResult(result, teamRa);
        return this;
    }

    private void InitializeTeamFromConfig(string teamNames)
    {
        var names = teamNames.Split(new[] { "，", "," }, StringSplitOptions.TrimEntries);
        if (names.Length != 4)
        {
            throw new Exception($"强制指定队伍角色数量不正确，必须是4个，当前{names.Length}个");
        }
        // 别名转换为标准名称
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = DefaultAutoFightConfig.AvatarAliasToStandardName(names[i]);
        }

        Logger.LogInformation("强制指定队伍角色:{Text}", string.Join(",", names));
        TaskContext.Instance().Config.AutoFightConfig.TeamNames = string.Join(",", names);
        Avatars = BuildAvatars(names.ToList());
        AvatarMap = Avatars.ToDictionary(x => x.Name);
    }

    public bool CheckTeamInitialized()
    {
        if (Avatars.Length < 4)
        {
            return false;
        }

        return true;
    }

    private void ParseTeamOcrResult(PaddleOcrResult result, RectArea rectArea)
    {
        List<string> names = new();
        List<Rect> nameRects = new();
        foreach (var item in result.Regions)
        {
            var name = StringUtils.ExtractChinese(item.Text);
            name = ErrorOcrCorrection(name);
            if (IsGenshinAvatarName(name))
            {
                names.Add(name);
                nameRects.Add(item.Rect.BoundingRect());
            }
        }

        if (names.Count != 4)
        {
            Logger.LogWarning("识别到的队伍角色数量不正确，当前识别结果:{Text}", string.Join(",", names));
        }


        if (names.Count == 3)
        {
            // 流浪者特殊处理
            // 4人以上的队伍，不支持流浪者的识别
            var wanderer = rectArea.Find(AutoFightContext.Instance().FightAssets.WandererIconRa);
            if (wanderer.IsEmpty())
            {
                wanderer = rectArea.Find(AutoFightContext.Instance().FightAssets.WandererIconNoActiveRa);
            }

            if (wanderer.IsEmpty())
            {
                // 补充识别流浪者
                Logger.LogWarning("二次尝试识别失败，当前识别结果:{Text}", string.Join(",", names));
            }
            else
            {
                names.Clear();
                foreach (var item in result.Regions)
                {
                    var name = StringUtils.ExtractChinese(item.Text);
                    name = ErrorOcrCorrection(name);
                    if (IsGenshinAvatarName(name))
                    {
                        names.Add(name);
                        nameRects.Add(item.Rect.BoundingRect());
                    }

                    var rect = item.Rect.BoundingRect();
                    if (rect.Y > wanderer.Y && wanderer.Y + wanderer.Height > rect.Y + rect.Height && !names.Contains("流浪者"))
                    {
                        names.Add("流浪者");
                        nameRects.Add(item.Rect.BoundingRect());
                    }
                }

                if (names.Count != 4)
                {
                    Logger.LogWarning("图像识别到流浪者，但识别队内位置信息失败");
                }
            }
        }

        Logger.LogInformation("识别到的队伍角色:{Text}", string.Join(",", names));
        Avatars = BuildAvatars(names, nameRects);
        AvatarMap = Avatars.ToDictionary(x => x.Name);
    }

    private bool IsGenshinAvatarName(string name)
    {
        if (DefaultAutoFightConfig.CombatAvatarNames.Contains(name))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 对OCR识别结果进行纠错
    /// TODO 还剩下单字名称（魈、琴）无法识别到的问题
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public string ErrorOcrCorrection(string name)
    {
        if (name.Contains("纳西"))
        {
            return "纳西妲";
        }

        return name;
    }

    private Avatar[] BuildAvatars(List<string> names, List<Rect>? nameRects = null)
    {
        AvatarCount = names.Count;
        var avatars = new Avatar[AvatarCount];
        for (var i = 0; i < AvatarCount; i++)
        {
            var nameRect = nameRects?[i] ?? Rect.Empty;
            avatars[i] = new Avatar(this, names[i], i + 1, nameRect)
            {
                IndexRect = AutoFightContext.Instance().FightAssets.AvatarIndexRectList[i]
            };
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

    public Avatar? SelectAvatar(string name)
    {
        return AvatarMap.TryGetValue(name, out var avatar) ? avatar : null;
    }
}