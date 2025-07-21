using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BetterGenshinImpact.Core.Simulator;
using Compunet.YoloSharp;
using Compunet.YoloSharp.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 战斗场景
/// </summary>
public class CombatScenes : IDisposable
{
    /// <summary>
    /// 当前配队
    /// </summary>
    private Avatar[] Avatars { set; get; } = [];

    public int AvatarCount => Avatars.Length;


    private readonly BgiYoloPredictor _predictor =
        App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiAvatarSide);

    public int ExpectedTeamAvatarNum { get; private set; } = 4;

    /// <summary>
    /// 获取一个只读的Avatars
    /// </summary>
    /// <returns>Avatars</returns>
    public ReadOnlyCollection<Avatar> GetAvatars()
    {
        return Avatars.AsReadOnly();
    }


    /// <summary>
    /// 通过YOLO分类器识别队伍内角色
    /// </summary>
    /// <param name="imageRegion">完整游戏画面的捕获截图</param>
    public CombatScenes InitializeTeam(ImageRegion imageRegion)
    {
        AssertUtils.CheckGameResolution();
        // 优先取配置
        if (!string.IsNullOrEmpty(TaskContext.Instance().Config.AutoFightConfig.TeamNames))
        {
            InitializeTeamFromConfig(TaskContext.Instance().Config.AutoFightConfig.TeamNames);
            return this;
        }

        // 判断当前是否处于联机状态
        List<Rect> avatarSideIconRectList;
        List<Rect> avatarIndexRectList;
        var pRaList = imageRegion.FindMulti(AutoFightAssets.Instance.PRa);
        if (pRaList.Count > 0)
        {
            var num = pRaList.Count + 1;
            if (num > 4)
            {
                throw new Exception("当前处于联机状态，但是队伍人数超过4人，无法识别");
            }

            // 联机状态下判断
            var onePRa = imageRegion.Find(AutoFightAssets.Instance.OnePRa);
            var p = "p";
            if (!onePRa.IsEmpty())
            {
                Logger.LogInformation("当前处于联机状态，且当前账号是房主，联机人数{Num}人", num);
                p = "1p";
            }
            else
            {
                Logger.LogInformation("当前处于联机状态，且在别人世界中，联机人数{Num}人", num);
            }

            avatarSideIconRectList = AutoFightAssets.Instance.AvatarSideIconRectListMap[$"{p}_{num}"];
            avatarIndexRectList = AutoFightAssets.Instance.AvatarIndexRectListMap[$"{p}_{num}"];

            ExpectedTeamAvatarNum = avatarSideIconRectList.Count;
        }
        else
        {
            avatarSideIconRectList = AutoFightAssets.Instance.AvatarSideIconRectList;
            avatarIndexRectList = AutoFightAssets.Instance.AvatarIndexRectList;
        }

        // 识别队伍
        var names = new string[avatarSideIconRectList.Count];
        var displayNames = new string[avatarSideIconRectList.Count];
        try
        {
            for (var i = 0; i < avatarSideIconRectList.Count; i++)
            {
                var ra = imageRegion.DeriveCrop(avatarSideIconRectList[i]);
                var pair = ClassifyAvatarCnName(ra.CacheImage, i + 1);
                names[i] = pair.Item1;
                if (!string.IsNullOrEmpty(pair.Item2))
                {
                    var costumeName = pair.Item2;
                    if (AutoFightAssets.Instance.AvatarCostumeMap.TryGetValue(costumeName, out string? name))
                    {
                        costumeName = name;
                    }

                    displayNames[i] = $"{pair.Item1}({costumeName})";
                }
                else
                {
                    displayNames[i] = pair.Item1;
                }
            }

            Logger.LogInformation("识别到的队伍角色:{Text}", string.Join(",", displayNames));
            Avatars = BuildAvatars([.. names], null, avatarIndexRectList);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e.Message);
        }

        return this;
    }

    public (string, string) ClassifyAvatarCnName(Image<Rgb24> img, int index)
    {
        var className = ClassifyAvatarName(img, index);

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

    public string ClassifyAvatarName(Image<Rgb24> img, int index)
    {
        SpeedTimer speedTimer = new();
        speedTimer.Record("角色侧面头像图像转换");
        var result = _predictor.Predictor.Classify(img);
        speedTimer.Record("角色侧面头像分类识别");
        Debug.WriteLine($"角色侧面头像识别结果：{result}");
        speedTimer.DebugPrint();
        var topClass = result.GetTopClass();
        if (topClass.Name.Name.StartsWith("Qin") || topClass.Name.Name.Contains("Costume"))
        {
            // 降低琴和衣装角色的识别率要求
            if (topClass.Confidence < 0.51)
            {
                img.SaveAsPng(@"log\avatar_side_classify_error.png");
                throw new Exception(
                    $"无法识别第{index}位角色，置信度{topClass.Confidence:F1}，结果：{topClass.Name.Name}。请重新阅读 BetterGI 文档中的《快速上手》！");
            }
        }
        else
        {
            if (topClass.Confidence < 0.7)
            {
                img.SaveAsPng(@"log\avatar_side_classify_error.png");
                throw new Exception(
                    $"无法识别第{index}位角色，置信度{topClass.Confidence:F1}，结果：{topClass.Name.Name}。请重新阅读 BetterGI 文档中的《快速上手》！");
            }
        }

        return topClass.Name.Name;
    }

    private void InitializeTeamFromConfig(string teamNames)
    {
        var names = teamNames.Split(["，", ","], StringSplitOptions.TrimEntries);
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
        Avatars = BuildAvatars([.. names]);
    }

    public bool CheckTeamInitialized()
    {
        if (Avatars.Length != ExpectedTeamAvatarNum)
        {
            return false;
        }

        return true;
    }


    private Avatar[] BuildAvatars(List<string> names, List<Rect>? nameRects = null,
        List<Rect>? avatarIndexRectList = null)
    {
        var cdConfig = TaskContext.Instance().Config.AutoFightConfig.ActionSchedulerByCd;
        if (avatarIndexRectList == null && ExpectedTeamAvatarNum == 4)
        {
            avatarIndexRectList = AutoFightAssets.Instance.AvatarIndexRectList;
        }

        if (avatarIndexRectList == null)
        {
            throw new Exception("联机状态下，此方法必须传入队伍角色编号位置信息");
        }

        var namesCount = names.Count;
        var avatars = new Avatar[namesCount];
        for (var i = 0; i < namesCount; i++)
        {
            var nameRect = nameRects?[i] ?? default;
            // 根据手动写的出招表来优化CD
            var cd = Avatar.ParseActionSchedulerByCd(names[i], cdConfig);
            avatars[i] = new Avatar(this, names[i], i + 1, nameRect, cd ?? -1)
            {
                IndexRect = avatarIndexRectList[i]
            };
        }

        return avatars;
    }

    /// <summary>
    /// 更新角色手动设置的CD
    /// </summary>
    /// <param name="cdConfig">配置字符串</param>
    /// <returns>返回配置中有效的角色名</returns>
    public List<string> UpdateActionSchedulerByCd(string cdConfig)
    {
        if (string.IsNullOrEmpty(cdConfig))
        {
            return [];
        }

        List<string> names = [];
        foreach (var t in Avatars)
        {
            var mCd = Avatar.ParseActionSchedulerByCd(t.Name, cdConfig);
            // 手动cd不为0，不是麦当劳不是0
            if (mCd is null) continue;
            t.ManualSkillCd = (double)mCd;
            names.Add(t.Name);
        }

        return names;
    }

    public void BeforeTask(CancellationToken ct)
    {
        for (var i = 0; i < AvatarCount; i++)
        {
            Avatars[i].Ct = ct;
        }
    }

    public void AfterTask()
    {
        // 释放所有按键
        Simulation.ReleaseAllKey();

        var mwk = SelectAvatar("玛薇卡");
        if (mwk != null)
        {
            foreach (var avatar in Avatars)
            {
                if (avatar.Name != "玛薇卡")
                {
                    avatar.Switch();
                }
            }
        }
    }

    public Avatar? SelectAvatar(string name)
    {
        return Avatars.FirstOrDefault(avatar => avatar.Name.Equals(name));
    }

    /// <summary>
    /// 使用编号切换角色
    /// </summary>
    /// <param name="avatarIndex">从1开始</param>
    /// <returns></returns>
    public Avatar SelectAvatar(int avatarIndex)
    {
        if (avatarIndex < 1 || avatarIndex > AvatarCount)
        {
            Logger.LogError("切换角色编号错误，当前角色数量{Count}，编号{Index}", AvatarCount, avatarIndex);
            throw new Exception("不存在的角色编号");
        }

        return Avatars[avatarIndex - 1];
    }

    /// <summary>
    /// 获取当前出战角色名
    /// </summary>
    /// <param name="force"></param>
    /// <param name="region"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public string? CurrentAvatar(bool force = false, ImageRegion? region = null,
        CancellationToken ct = default)
    {
        if (!force && Avatar.LastActiveAvatar is not null)
        {
            return Avatar.LastActiveAvatar;
        }

        var imageRegion = region ?? CaptureToRectArea();
        string? avatarName = null;

        var notActiveCount = 0;
        foreach (var avatar in GetAvatars())
        {
            if (avatar.IsActive(imageRegion))
            {
                avatarName = avatar.Name;
            }
            else
            {
                notActiveCount++;
            }
        }

        if (notActiveCount != ExpectedTeamAvatarNum - 1) return avatarName;
        Avatar.LastActiveAvatar = avatarName;
        return Avatar.LastActiveAvatar;
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
        var teamRa = content.CaptureRectArea.DeriveCrop(AutoFightAssets.Instance.TeamRectNoIndex);
        // 过滤出白色
        var hsvFilterMat =
            OpenCvCommonHelper.InRangeHsv(teamRa.SrcMat, new Scalar(0, 0, 210), new Scalar(255, 30, 255));

        // 识别队伍内角色
        var result = OcrFactory.Paddle.OcrResult(hsvFilterMat);
        ParseTeamOcrResult(result, teamRa);
        return this;
    }

    [Obsolete]
    private void ParseTeamOcrResult(OcrResult result, ImageRegion rectArea)
    {
        List<string> names = [];
        List<Rect> nameRects = [];
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
            var wanderer = rectArea.Find(AutoFightAssets.Instance.WandererIconRa);
            if (wanderer.IsEmpty())
            {
                wanderer = rectArea.Find(AutoFightAssets.Instance.WandererIconNoActiveRa);
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
                    if (rect.Y > wanderer.Y && wanderer.Y + wanderer.Height > rect.Y + rect.Height &&
                        !names.Contains("流浪者"))
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
