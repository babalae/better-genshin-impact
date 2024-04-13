using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using Compunet.YoloV8;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Sdcb.PaddleOCR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 战斗场景
/// </summary>
public class CombatScenes : IDisposable
{
    /// <summary>
    /// 当前配队
    /// </summary>
    public Avatar[] Avatars { get; set; } = new Avatar[1];

    public Dictionary<string, Avatar> AvatarMap { get; set; } = new();

    public int AvatarCount { get; set; }

    private readonly YoloV8 _predictor = new(Global.Absolute("Assets\\Model\\Common\\avatar_side_classify_sim.onnx"));

    /// <summary>
    /// 通过YOLO分类器识别队伍内角色
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

        // 识别队伍
        var names = new string[4];
        var displayNames = new string[4];
        try
        {
            for (var i = 0; i < AutoFightAssets.Instance.AvatarSideIconRectList.Count; i++)
            {
                var ra = content.CaptureRectArea.Crop(AutoFightAssets.Instance.AvatarSideIconRectList[i]);
                var pair = ClassifyAvatarCnName(ra.SrcBitmap, i + 1);
                names[i] = pair.Item1;
                if (!string.IsNullOrEmpty(pair.Item2))
                {
                    var costumeName = pair.Item2;
                    if (AutoFightAssets.Instance.AvatarCostumeMap.ContainsKey(costumeName))
                    {
                        costumeName = AutoFightAssets.Instance.AvatarCostumeMap[costumeName];
                    }

                    displayNames[i] = $"{pair.Item1}({costumeName})";
                }
                else
                {
                    displayNames[i] = pair.Item1;
                }
            }
            Logger.LogInformation("识别到的队伍角色:{Text}", string.Join(",", displayNames));
            Avatars = BuildAvatars(names.ToList());
            AvatarMap = Avatars.ToDictionary(x => x.Name);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e.Message);
        }
        return this;
    }

    public (string, string) ClassifyAvatarCnName(Bitmap src, int index)
    {
        var className = ClassifyAvatarName(src, index);

        var nameEn = className;
        var costumeName = "";
        var i = className.IndexOf("Costume", StringComparison.Ordinal);
        if (i > 0)
        {
            nameEn = className[..i];
            costumeName = className[(i + 7)..];
        }

        var avatar = DefaultAutoFightConfig.CombatAvatarNameEnMap[nameEn];
        return (avatar.Name, costumeName);
    }

    public string ClassifyAvatarName(Bitmap src, int index)
    {
        SpeedTimer speedTimer = new();
        using var memoryStream = new MemoryStream();
        src.Save(memoryStream, ImageFormat.Bmp);
        memoryStream.Seek(0, SeekOrigin.Begin);
        speedTimer.Record("角色侧面头像图像转换");
        var result = _predictor.Classify(memoryStream);
        speedTimer.Record("角色侧面头像分类识别");
        Debug.WriteLine($"角色侧面头像识别结果：{result}");
        speedTimer.DebugPrint();
        if (result.Confidence < 0.8)
        {
            Cv2.ImWrite(@"log\avatar_side_classify_error.png", src.ToMat());
            throw new Exception($"无法识别第{index}位角色，置信度{result.Confidence}");
        }

        return result.Class.Name;
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

    private Avatar[] BuildAvatars(List<string> names, List<Rect>? nameRects = null)
    {
        AvatarCount = names.Count;
        var avatars = new Avatar[AvatarCount];
        for (var i = 0; i < AvatarCount; i++)
        {
            var nameRect = nameRects?[i] ?? Rect.Empty;
            avatars[i] = new Avatar(this, names[i], i + 1, nameRect)
            {
                IndexRect = AutoFightContext.Instance.FightAssets.AvatarIndexRectList[i]
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

    #region OCR识别队伍（已弃用）

    /// <summary>
    /// 通过OCR识别队伍内角色
    /// </summary>
    /// <param name="content">完整游戏画面的捕获截图</param>
    [Obsolete]
    public CombatScenes InitializeTeamOldOcr(CaptureContent content)
    {
        // 优先取配置
        if (!string.IsNullOrEmpty(TaskContext.Instance().Config.AutoFightConfig.TeamNames))
        {
            InitializeTeamFromConfig(TaskContext.Instance().Config.AutoFightConfig.TeamNames);
            return this;
        }

        // 剪裁出队伍区域
        var teamRa = content.CaptureRectArea.Crop(AutoFightContext.Instance.FightAssets.TeamRectNoIndex);
        // 过滤出白色
        var hsvFilterMat = OpenCvCommonHelper.InRangeHsv(teamRa.SrcMat, new Scalar(0, 0, 210), new Scalar(255, 30, 255));

        // 识别队伍内角色
        var result = OcrFactory.Paddle.OcrResult(hsvFilterMat);
        ParseTeamOcrResult(result, teamRa);
        return this;
    }

    [Obsolete]
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
            var wanderer = rectArea.Find(AutoFightContext.Instance.FightAssets.WandererIconRa);
            if (wanderer.IsEmpty())
            {
                wanderer = rectArea.Find(AutoFightContext.Instance.FightAssets.WandererIconNoActiveRa);
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

    [Obsolete]
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
    [Obsolete]
    public string ErrorOcrCorrection(string name)
    {
        if (name.Contains("纳西"))
        {
            return "纳西妲";
        }

        return name;
    }

    #endregion OCR识别队伍（已弃用）

    public void Dispose()
    {
        _predictor.Dispose();
    }
}
