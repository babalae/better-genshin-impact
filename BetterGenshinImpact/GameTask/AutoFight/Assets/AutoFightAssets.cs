using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Assets;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.GameTask.Model.Area;


namespace BetterGenshinImpact.GameTask.AutoFight.Assets;

public sealed class AutoFightAssets
{
    private static readonly CaptureAssetsCache<AutoFightAssets> Cache = new(static size => new AutoFightAssets(size));
    private readonly int _captureWidth;
    private readonly int _captureHeight;
    public Rect TeamRectNoIndex { get; private set; }
    public Rect TeamRect { get; private set; }
    public IReadOnlyList<Rect> AvatarSideIconRectList { get; private set; } // 侧边栏角色头像 非联机状态下
    public IReadOnlyList<Rect> AvatarIndexRectList { get; private set; } // 侧边栏角色头像对应的白色块 非联机状态下
    public IReadOnlyList<Rect> AvatarQRectListMap { get; private set; } // 角色头像对应的Q技能图标

    public Rect ERect { get; private set; }
    public Rect ECooldownRect { get; private set; }
    public Rect QRect { get; private set; }
    public Rect QRectForClassify { get; private set; }
    public Rect ZCooldownRect { get; private set; }
    public Rect EndTipsUpperRect { get; private set; } // 挑战达成提示
    public Rect EndTipsRect { get; private set; }

    public IReadOnlyDictionary<string, string> AvatarCostumeMap { get; private set; }

    public IReadOnlyDictionary<string, List<Rect>> AvatarSideIconRectListMap { get; private set; } // 侧边栏角色头像 联机状态下
    public IReadOnlyDictionary<string, List<Rect>> AvatarIndexRectListMap { get; private set; } // 侧边栏角色头像对应的白色块 联机状态下

    // 小道具位置
    public Rect GadgetRect { get; private set; }

    /// <summary>
    /// 经验值模板识别对象列表，用于检测怪物死亡时掉落的经验值数字图标
    /// </summary>
    public IReadOnlyList<RecognitionObject> ExperienceRecognitionObjects { get; private set; } = Array.Empty<RecognitionObject>();

    private Rect CaptureRect { get; }
    private double AssetScale { get; }

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
    private AutoFightAssets(CaptureSize captureSize)
    {
        _captureWidth = captureSize.Width;
        _captureHeight = captureSize.Height;
        CaptureRect = captureSize.CaptureRect;
        AssetScale = captureSize.AssetScale;
        Initialization();
    }
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。

    private void Initialization()
    {
        TeamRectNoIndex = new Rect(CaptureRect.Width - (int)(355 * AssetScale), (int)(220 * AssetScale),
            (int)((355 - 85) * AssetScale), (int)(465 * AssetScale));
        TeamRect = new Rect(CaptureRect.Width - (int)(355 * AssetScale), (int)(220 * AssetScale),
            (int)(355 * AssetScale), (int)(465 * AssetScale));
        ERect = new Rect(CaptureRect.Width - (int)(267 * AssetScale), CaptureRect.Height - (int)(132 * AssetScale),
            (int)(77 * AssetScale), (int)(77 * AssetScale));
        ECooldownRect = new Rect(CaptureRect.Width - (int)(241 * AssetScale), CaptureRect.Height - (int)(97 * AssetScale),
            (int)(41 * AssetScale), (int)(18 * AssetScale));
        QRect = new Rect(CaptureRect.Width - (int)(157 * AssetScale), CaptureRect.Height - (int)(165 * AssetScale),
            (int)(110 * AssetScale), (int)(110 * AssetScale));
        QRectForClassify = new Rect(CaptureRect.Width - (int)(172 * AssetScale), CaptureRect.Height - (int)(166 * AssetScale),
            (int)(137 * AssetScale), (int)(137 * AssetScale));
        ZCooldownRect = new Rect(CaptureRect.Width - (int)(130 * AssetScale), (int)(814 * AssetScale),
            (int)(60 * AssetScale), (int)(24 * AssetScale));
        // 小道具位置 1920-133,800,60,50
        GadgetRect = new Rect(CaptureRect.Width - (int)(133 * AssetScale), (int)(800 * AssetScale),
            (int)(60 * AssetScale), (int)(50 * AssetScale));
        // 结束提示从中间开始找相对位置
        EndTipsUpperRect = new Rect(CaptureRect.Width / 2 - (int)(100 * AssetScale), (int)(243 * AssetScale),
            (int)(200 * AssetScale), (int)(50 * AssetScale));
        EndTipsRect = new Rect(CaptureRect.Width / 2 - (int)(200 * AssetScale), CaptureRect.Height - (int)(160 * AssetScale),
            (int)(400 * AssetScale), (int)(80 * AssetScale));

        AvatarIndexRectList =
        [
            new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(256 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(352 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(448 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(544 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
        ];

        AvatarQRectListMap =
        [
            new Rect(CaptureRect.Width - (int)(336 * AssetScale), (int)(216 * AssetScale), (int)(64 * AssetScale), (int)(84 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(336 * AssetScale), (int)(316 * AssetScale), (int)(64 * AssetScale), (int)(84 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(336 * AssetScale), (int)(416 * AssetScale), (int)(64 * AssetScale), (int)(84 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(336 * AssetScale), (int)(516 * AssetScale), (int)(64 * AssetScale), (int)(84 * AssetScale)),
        ];

        AvatarSideIconRectList =
        [
            new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(225 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(315 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(410 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(500 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
        ];

        AvatarCostumeMap = new Dictionary<string, string>
        {
            { "Flamme", "殷红终夜" },
            { "Bamboo", "雨化竹身" },
            { "Dai", "冷花幽露" },
            { "Yu", "玄玉瑶芳" },
            { "Dancer", "帆影游风" },
            { "Witch", "琪花星烛" },
            { "Wic", "和谐" },
            { "Studentin", "叶隐芳名" },
            { "Fruhling", "花时来信" },
            { "Highness", "极夜真梦" },
            { "Feather", "霓裾翩跹" },
            { "Floral", "纱中幽兰" },
            { "Summertime", "闪耀协奏" },
            { "Sea", "海风之梦" },
        };

        // 联机
        // 1p_2 与 p_2 为同一位置
        // 1p_4 与 p_4 为同一位置
        AvatarSideIconRectListMap = new Dictionary<string, List<Rect>>
        {
            {
                "1p_2", [
                    new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(375 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
                    new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(470 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
                ]
            },
            {
                "1p_3", [
                    new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(375 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
                    new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(470 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
                ]
            },
            { "1p_4", [new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(515 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale))] },
            {
                "p_2", [
                    new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(375 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
                    new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(470 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale)),
                ]
            },
            { "p_3", [new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(475 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale))] },
            { "p_4", [new Rect(CaptureRect.Width - (int)(155 * AssetScale), (int)(515 * AssetScale), (int)(76 * AssetScale), (int)(76 * AssetScale))] },
        };

        AvatarIndexRectListMap = new Dictionary<string, List<Rect>>
        {
            {
                "1p_2", [
                    new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(412 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
                    new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(508 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
                ]
            },
            {
                "1p_3", [
                    new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(459 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
                    new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(555 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
                ]
            },
            { "1p_4", [new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(552 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale))] },
            {
                "p_2", [
                    new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(412 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
                    new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(508 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale)),
                ]
            },
            { "p_3", [new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(412 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale))] },
            { "p_4", [new Rect(CaptureRect.Width - (int)(61 * AssetScale), (int)(507 * AssetScale), (int)(28 * AssetScale), (int)(24 * AssetScale))] },
        };

        // 加载经验值模板图片（用于检测精英怪死亡时的经验值数字图标）
        try
        {
            LoadExperienceRecognitionObjects();
        }
        catch (Exception)
        {
            // 经验值模板加载失败不应阻止 AutoFightAssets 初始化
            ExperienceRecognitionObjects = Array.Empty<RecognitionObject>();
        }
    }

    public static AutoFightAssets Get(Region region)
    {
        return Cache.Get(region);
    }

    public static AutoFightAssets Get(int captureWidth, int captureHeight)
    {
        return Cache.Get(captureWidth, captureHeight);
    }

    /// <summary>
    /// 加载经验值模板图片，文件缺失时跳过，不影响其他功能
    /// </summary>
    private void LoadExperienceRecognitionObjects()
    {
        var experienceValues = new[] { 57, 58, 60 };
        var threshold = CaptureRect.Width > 1920 ? 0.6 : 0.9;
        var roi = new Rect(
            (int)(CaptureRect.Width * 0.145),
            (int)(CaptureRect.Height * 0.5),
            (int)(CaptureRect.Width * 0.02),
            (int)(CaptureRect.Height * 0.22));

        var list = new List<RecognitionObject>();
        foreach (var exp in experienceValues)
        {
            var fileName = $"experience_{exp}.png";
            try
            {
                var ro = new RecognitionObject
                {
                    Name = $"Experience_{exp}",
                    RecognitionType = RecognitionTypes.TemplateMatch,
                    TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", fileName, _captureWidth, _captureHeight),
                    RegionOfInterest = roi,
                    UseMask = true,
                    Threshold = threshold,
                    DrawOnWindow = true,
                }.InitTemplate();
                list.Add(ro);
            }
            catch (Exception)
            {
                // 构造阶段 Logger 可能不可用，静默跳过缺失的模板
            }
        }

        ExperienceRecognitionObjects = list.AsReadOnly();
    }
}
