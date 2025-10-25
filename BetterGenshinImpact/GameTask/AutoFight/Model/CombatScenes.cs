using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Compunet.YoloSharp;
using Compunet.YoloSharp.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;

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

    /// <summary>
    /// 最近一次识别出的出战角色编号，从1开始，-1表示未识别
    /// </summary>
    public int LastActiveAvatarIndex { get; set; } = -1;

    public MultiGameStatus? CurrentMultiGameStatus { set; get; }

    private readonly BgiYoloPredictor _predictor;
    private readonly bool _ownsPredictor;

    private readonly AutoFightAssets _autoFightAssets;

    private readonly ElementAssets _elementAssets;

    private readonly ILogger _logger;

    private readonly ISystemInfo _systemInfo;

    public CombatScenes(BgiYoloPredictor? predictor = null, AutoFightAssets? autoFightAssets = null, ILogger? logger = null, ElementAssets? elementAssets = null, ISystemInfo? systemInfo = null)
    {
        if (predictor == null)
        {
            _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiAvatarSide);
            _ownsPredictor = true;
        }
        else
        {
            _predictor = predictor;
            _ownsPredictor = false;
        }
        if (autoFightAssets == null)
        {
            _autoFightAssets = AutoFightAssets.Instance;    // todo BaseAssets重构后直接由systemInfo构建，省去传入？
        }
        else
        {
            _autoFightAssets = autoFightAssets;
        }
        if (logger == null)
        {
            _logger = TaskControl.Logger;
        }
        else
        {
            _logger = logger;
        }
        if (elementAssets == null)
        {
            _elementAssets = ElementAssets.Instance;
        }
        else
        {
            _elementAssets = elementAssets;
        }
        if (systemInfo == null)
        {
            _systemInfo = TaskContext.Instance().SystemInfo;
        }
        else
        {
            _systemInfo = systemInfo;
        }
    }

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
    public CombatScenes InitializeTeam(ImageRegion imageRegion, AutoFightConfig? autoFightConfig = null)
    {
        if (autoFightConfig == null)
        {
            autoFightConfig = TaskContext.Instance().Config.AutoFightConfig;
        }

        AssertUtils.CheckGameResolution();
        // 优先取配置
        if (!string.IsNullOrEmpty(autoFightConfig.TeamNames))
        {
            InitializeTeamFromConfig(autoFightConfig.TeamNames, autoFightConfig);
            return this;
        }


        // 判断联机状态
        CurrentMultiGameStatus = PartyAvatarSideIndexHelper.DetectedMultiGameStatus(imageRegion, _autoFightAssets, _logger);
        // 队伍角色编号和侧面头像位置
        var (avatarIndexRectList, avatarSideIconRectList) = PartyAvatarSideIndexHelper.GetAllIndexRects(imageRegion, CurrentMultiGameStatus, _logger, _elementAssets, _systemInfo);
        ExpectedTeamAvatarNum = avatarIndexRectList.Count;

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
                    if (_autoFightAssets.AvatarCostumeMap.TryGetValue(costumeName, out string? name))
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

            _logger.LogInformation("识别到的队伍角色:{Text}", string.Join(",", displayNames));
            Avatars = BuildAvatars([.. names], null, avatarIndexRectList, autoFightConfig);
        }
        catch (Exception e) // todo 此处catch把错误吞了不便排查
        {
            _logger.LogWarning(e.Message);
        }

        return this;
    }


    /// <summary>
    /// 这个个方法主要用于在切人判断有误的情况下，且能够找到预期数量的角色编号框。此时只有两种情况
    /// 1. A草露进度条导致角色编号框偏移，B退队后偏移不变，C独立地图传送后偏移还原
    /// 2. 地图边缘环境，导致角色编号框切人判断失效
    /// 此方法必须在判定一定存在 ExpectedTeamAvatarNum 数量的 IndexRectList 的情况下才能使用
    /// </summary>
    /// <param name="imageRegion"></param>
    /// <returns>false:存在 IndexRectList 的情况下使用此方法，返回false的时候很有可能处于地图边缘环境下</returns>
    public bool RefreshTeamAvatarIndexRectList(ImageRegion imageRegion)
    {
        // 只用新方法判断
        try
        {
            var (avatarIndexRectList, _) = PartyAvatarSideIndexHelper.GetAllIndexRectsNew(imageRegion, CurrentMultiGameStatus!, _logger, _elementAssets, _systemInfo);
            if (avatarIndexRectList.Count != ExpectedTeamAvatarNum)
            {
                _logger.LogWarning("重新识别到的队伍角色数量与之前不一致，之前{Old}个，现在{New}个", ExpectedTeamAvatarNum, avatarIndexRectList.Count);
                return false;
            }

            for (var i = 0; i < ExpectedTeamAvatarNum; i++)
            {
                Avatars[i].IndexRect = avatarIndexRectList[i];
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "使用新方法获取角色编号位置失败");
            _logger.LogWarning("[重新识别角色编号位置]使用新方法获取角色编号位置失败，原因：" + ex.Message);
            return false;
        }
    }

    // public static List<Rect> FindAvatarIndexRectList(ImageRegion imageRegion)
    // {
    //     var i1 = imageRegion.Find(ElementAssets.Instance.Index1);
    //     var i2 = imageRegion.Find(ElementAssets.Instance.Index2);
    //     var i3 = imageRegion.Find(ElementAssets.Instance.Index3);
    //     var i4 = imageRegion.Find(ElementAssets.Instance.Index4);
    //     var curr = imageRegion.Find(ElementAssets.Instance.CurrentAvatarThreshold);
    //     // Debug.WriteLine($"i1:{i1.X},{i1.Y},{i1.Width},{i1.Height}; i2:{i2.X},{i2.Y},{i2.Width},{i2.Height}; i3:{i3.X},{i3.Y},{i3.Width},{i3.Height}; i4:{i4.X},{i4.Y},{i4.Width},{i4.Height}; curr:{curr.X},{curr.Y},{curr.Width},{curr.Height}");
    //     return null;
    // }


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
                img.SaveAsPng(Global.Absolute($@"log\avatar_side_classify_error.png"));
                throw new Exception(
                    $"无法识别第{index}位角色，置信度{topClass.Confidence:F1}，结果：{topClass.Name.Name}。请重新阅读 BetterGI 文档中的《快速上手》！");
            }
        }
        else
        {
            if (topClass.Confidence < 0.7)
            {
                img.SaveAsPng(Global.Absolute($@"log\avatar_side_classify_error.png"));
                throw new Exception(
                    $"无法识别第{index}位角色，置信度{topClass.Confidence:F1}，结果：{topClass.Name.Name}。请重新阅读 BetterGI 文档中的《快速上手》！");
            }
        }

        return topClass.Name.Name;
    }

    private void InitializeTeamFromConfig(string teamNames, AutoFightConfig autoFightConfig)
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

        _logger.LogInformation("强制指定队伍角色:{Text}", string.Join(",", names));
        autoFightConfig.TeamNames = string.Join(",", names);
        Avatars = BuildAvatars([.. names], autoFightConfig: autoFightConfig);
    }

    public bool CheckTeamInitialized()
    {
        if (Avatars.Length != ExpectedTeamAvatarNum)
        {
            return false;
        }

        return true;
    }


    private Avatar[] BuildAvatars(List<string> names, List<Rect>? nameRects = null, List<Rect>? avatarIndexRectList = null, AutoFightConfig? autoFightConfig = null)
    {
        if (autoFightConfig == null)
        {
            autoFightConfig = TaskContext.Instance().Config.AutoFightConfig;
        }
        var cdConfig = autoFightConfig.ActionSchedulerByCd;
        if (avatarIndexRectList == null && ExpectedTeamAvatarNum == 4)
        {
            avatarIndexRectList = _autoFightAssets.AvatarIndexRectList;
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
            _logger.LogError("切换角色编号错误，当前角色数量{Count}，编号{Index}", AvatarCount, avatarIndex);
            throw new Exception("不存在的角色编号");
        }

        return Avatars[avatarIndex - 1];
    }

    /// <summary>
    /// 获取当前出战角色名
    /// 不考虑重新刷新编号框位置
    /// 不推荐使用
    /// </summary>
    /// <param name="force"></param>
    /// <param name="region"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public string? CurrentAvatar(bool force = false, ImageRegion? region = null,
        CancellationToken ct = default)
    {
        if (!force && LastActiveAvatarIndex > 0)
        {
            return Avatars[LastActiveAvatarIndex - 1].Name;
        }

        var imageRegion = region ?? TaskControl.CaptureToRectArea();

        var rectArray = Avatars.Select(t => t.IndexRect).ToArray();
        int index = PartyAvatarSideIndexHelper.GetAvatarIndexIsActiveWithContext(imageRegion, rectArray, new AvatarActiveCheckContext());

        if (index > 0)
        {
            LastActiveAvatarIndex = index;
        }

        return Avatars[LastActiveAvatarIndex - 1].Name;
    }

    /// <summary>
    /// 推荐使用
    /// 失败后自动刷新编号框位置
    /// </summary>
    /// <param name="imageRegion"></param>l
    /// <param name="context"></param>
    /// <returns></returns>
    public int GetActiveAvatarIndex(ImageRegion imageRegion, AvatarActiveCheckContext context)
    {
        var rectArray = Avatars.Select(t => t.IndexRect).ToArray();
        int index = PartyAvatarSideIndexHelper.GetAvatarIndexIsActiveWithContext(imageRegion, rectArray, context);

        if (index > 0)
        {
            LastActiveAvatarIndex = index;
            return index;
        }
        else
        {
            // 多次识别失败则尝试刷新角色编号位置
            // 应对草露问题
            if (context.TotalCheckFailedCount > 3)
            {
                // 失败多次，识别是否存在满足预期的编号框
                if (PartyAvatarSideIndexHelper.CountIndexRect(imageRegion) == Avatars.Length)
                {
                    bool res = RefreshTeamAvatarIndexRectList(imageRegion);
                    _logger.LogWarning("多次识别出战角色失败，尝试刷新角色编号位置（处理草露问题），刷新结果:{Result}", res ? "成功" : "失败");
                    if (res)
                    {
                        context.TotalCheckFailedCount = 0;
                    }
                }
            }
        }


        return -1;
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
            InitializeTeamFromConfig(TaskContext.Instance().Config.AutoFightConfig.TeamNames, TaskContext.Instance().Config.AutoFightConfig);
            return this;
        }

        // 剪裁出队伍区域
        var teamRa = content.CaptureRectArea.DeriveCrop(_autoFightAssets.TeamRectNoIndex);
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
            _logger.LogWarning("识别到的队伍角色数量不正确，当前识别结果:{Text}", string.Join(",", names));
        }

        if (names.Count == 3)
        {
            // 流浪者特殊处理
            // 4人以上的队伍，不支持流浪者的识别
            var wanderer = rectArea.Find(_autoFightAssets.WandererIconRa);
            if (wanderer.IsEmpty())
            {
                wanderer = rectArea.Find(_autoFightAssets.WandererIconNoActiveRa);
            }

            if (wanderer.IsEmpty())
            {
                // 补充识别流浪者
                _logger.LogWarning("二次尝试识别失败，当前识别结果:{Text}", string.Join(",", names));
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
                    _logger.LogWarning("图像识别到流浪者，但识别队内位置信息失败");
                }
            }
        }

        _logger.LogInformation("识别到的队伍角色:{Text}", string.Join(",", names));
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
        if (_ownsPredictor)
        {
            _predictor.Dispose();
        }
    }
}