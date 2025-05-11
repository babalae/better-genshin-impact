using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFight.Assets;

public class AutoFightAssets : BaseAssets<AutoFightAssets>
{
    public Rect TeamRectNoIndex;
    public Rect TeamRect;
    public List<Rect> AvatarSideIconRectList; // 侧边栏角色头像 非联机状态下
    public List<Rect> AvatarIndexRectList; // 侧边栏角色头像对应的白色块 非联机状态下
    public Rect ERect;
    public Rect ECooldownRect;
    public Rect QRect;
    public Rect EndTipsUpperRect; // 挑战达成提示
    public Rect EndTipsRect;
    public RecognitionObject WandererIconRa;
    public RecognitionObject WandererIconNoActiveRa;
    public RecognitionObject ConfirmRa;
    public RecognitionObject ArtifactAreaRa;
    public RecognitionObject ExitRa;
    public RecognitionObject ClickAnyCloseTipRa;
    public RecognitionObject UseCondensedResinRa;

    // 树脂状态
    public RecognitionObject CondensedResinCountRa;

    public RecognitionObject FragileResinCountRa;

    public Dictionary<string, string> AvatarCostumeMap;

    // 联机
    public RecognitionObject OnePRa;

    public RecognitionObject PRa;
    public Dictionary<string, List<Rect>> AvatarSideIconRectListMap; // 侧边栏角色头像 联机状态下
    public Dictionary<string, List<Rect>> AvatarIndexRectListMap; // 侧边栏角色头像对应的白色块 联机状态下

    // 小道具位置
    public Rect GadgetRect;

    private AutoFightAssets()
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

        // 左上角的 1P 图标
        OnePRa = new RecognitionObject
        {
            Name = "1P",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "1p.png"),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 4, CaptureRect.Height / 7),
            DrawOnWindow = false
        }.InitTemplate();
        // 右侧联机的 P 图标
        PRa = new RecognitionObject
        {
            Name = "P",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "p.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(CaptureRect.Width / 12.5), CaptureRect.Height / 5, (int)(CaptureRect.Width / 12.5), CaptureRect.Height / 2 - CaptureRect.Width / 7),
            DrawOnWindow = false
        }.InitTemplate();

        WandererIconRa = new RecognitionObject
        {
            Name = "WandererIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "wanderer_icon.png"),
            DrawOnWindow = false
        }.InitTemplate();
        WandererIconNoActiveRa = new RecognitionObject
        {
            Name = "WandererIconNoActive",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "wanderer_icon_no_active.png"),
            DrawOnWindow = false
        }.InitTemplate();

        // 右下角的按钮
        ConfirmRa = new RecognitionObject
        {
            Name = "Confirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "confirm.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();
        ArtifactAreaRa = new RecognitionObject
        {
            Name = "ArtifactArea",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "artifact_flower_logo.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 2,0,CaptureRect.Width / 2, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();

        // 点击任意处关闭提示
        ClickAnyCloseTipRa = new RecognitionObject
        {
            Name = "ClickAnyCloseTip",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "click_any_close_tip.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        UseCondensedResinRa = new RecognitionObject
        {
            Name = "UseCondensedResin",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "use_condensed_resin.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        ExitRa = new RecognitionObject
        {
            Name = "Exit",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "exit.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        CondensedResinCountRa = new RecognitionObject
        {
            Name = "CondensedResinCount",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "condensed_resin_count.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 3 * 2, CaptureRect.Width / 2, CaptureRect.Height / 3),
            DrawOnWindow = false
        }.InitTemplate();
        FragileResinCountRa = new RecognitionObject
        {
            Name = "FragileResinCount",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "fragile_resin_count.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 3 * 2, CaptureRect.Width / 2, CaptureRect.Height / 3),
            DrawOnWindow = false
        }.InitTemplate();
    }
}
