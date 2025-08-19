using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;
using System;


namespace BetterGenshinImpact.GameTask.AutoFight.Assets;

public class AutoFightAssets : BaseAssets<AutoFightAssets>
{
    public Rect TeamRectNoIndex;
    public Rect TeamRect;
    public List<Rect> AvatarSideIconRectList; // 侧边栏角色头像 非联机状态下
    public List<Rect> AvatarIndexRectList; // 侧边栏角色头像对应的白色块 非联机状态下
    public List<Rect> AvatarQRectListMap; // 角色头像对应的Q技能图标 
    
    public Rect ERect;
    public Rect ECooldownRect;
    public Rect QRect;
    public Rect ZCooldownRect;
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
    // 自动秘境
    // public RecognitionObject LockIconRa; // 锁定辅助图标
    public RecognitionObject CondensedResinTopIconRa;
    public RecognitionObject OriginalResinTopIconRa;

    public Dictionary<string, string> AvatarCostumeMap;

    // 联机
    public RecognitionObject OnePRa;

    public RecognitionObject PRa;
    public Dictionary<string, List<Rect>> AvatarSideIconRectListMap; // 侧边栏角色头像 联机状态下
    public Dictionary<string, List<Rect>> AvatarIndexRectListMap; // 侧边栏角色头像对应的白色块 联机状态下

    // 小道具位置
    public Rect GadgetRect;
    
    public RecognitionObject AbnormalIconRa;
    
    // 定义7种元素要检测的Q技能颜色，每种元素两种颜色
    public static readonly Dictionary<ElementalType, List<Scalar>> Colors = new Dictionary<ElementalType, List<Scalar>> {
        { ElementalType.Cryo, new List<Scalar> { new Scalar(117, 212, 233), new Scalar(176, 255, 255) } }, // 冰 √ 
        { ElementalType.Anemo, new List<Scalar> { new Scalar(47, 189, 147), new Scalar(172, 255, 239) } }, // 风 待确定优化 1 √ 
        { ElementalType.Geo, new List<Scalar> { new Scalar(226, 147, 21), new Scalar(255, 218, 121) } }, // 岩 待确定优化 1 √ 
        { ElementalType.Dendro, new List<Scalar> { new Scalar(111, 179, 30), new Scalar(219, 255, 136) } }, // 草 待确定优化 1 √ 
        { ElementalType.Electro, new List<Scalar> { new Scalar(158, 100, 235), new Scalar(244, 205, 255) } }, // 雷 待确定优化 1  new Scalar(158, 100, 235), new Scalar(244, 205, 255)
        { ElementalType.Hydro, new List<Scalar> { new Scalar(21, 149, 252), new Scalar(123, 245, 255) } }, // 水 待确定优化 1 √ 
        { ElementalType.Pyro, new List<Scalar> { new Scalar(222, 88, 60), new Scalar(255, 185, 170) } } ,// 火 待确定优化 1 √ 
        { ElementalType.Omni, new List<Scalar> { new Scalar(0, 0, 0), new Scalar(0, 0, 0) } } // 无效果
    };
    //1 1959,466
    //2 1959,562
    //3 1961,666
    //4 1959,754

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
        ZCooldownRect = new Rect(CaptureRect.Width - (int)(130 * AssetScale),  (int)(814 * AssetScale),
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
            new Rect(CaptureRect.Width - (int)(330 * AssetScale), (int)(228 * AssetScale), (int)(62 * AssetScale), (int)(70 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(330 * AssetScale), (int)(328 * AssetScale), (int)(62 * AssetScale), (int)(70 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(330 * AssetScale), (int)(428 * AssetScale), (int)(62 * AssetScale), (int)(70 * AssetScale)),
            new Rect(CaptureRect.Width - (int)(330 * AssetScale), (int)(518 * AssetScale), (int)(62 * AssetScale), (int)(70 * AssetScale)),
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
        
        // 自动秘境
        // LockIconRa = new RecognitionObject
        // {
        //     Name = "LockIcon",
        //     RecognitionType = RecognitionTypes.TemplateMatch,
        //     TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "lock_icon.png"),
        //     RegionOfInterest = new Rect(CaptureRect.Width - (int)(215 * AssetScale), 0, (int)(215 * AssetScale), (int)(80 * AssetScale)),
        //     DrawOnWindow = false
        // }.InitTemplate();
        CondensedResinTopIconRa = new RecognitionObject
        {
            Name = "CondensedResinTopIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "condensed_resin_top_icon.png"),
            RegionOfInterest = new Rect((int)(1270 * AssetScale), (int)(25 * AssetScale), (int)(520 * AssetScale), (int)(45 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
        OriginalResinTopIconRa = new RecognitionObject
        {
            Name = "OriginalResinTopIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "original_resin_top_icon.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(450 * AssetScale), (int)(25 * AssetScale), (int)(265 * AssetScale), (int)(45 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
        AbnormalIconRa = new RecognitionObject
        {
            Name = "AbnormalIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "abnormal_icon.png"),
            RegionOfInterest = new Rect(0,(int)(CaptureRect.Height*0.08), (int)(CaptureRect.Width*0.04), (int)(CaptureRect.Height*0.07)),
            DrawOnWindow = false
        }.InitTemplate();
        
    }
    
    //参考元素收集，新建立角色的元素相关信息类，后续可以扩展元素相关战斗策略
    public class ElementalAvatar(string name, ElementalType elementalType, bool normalAttack, bool elementalSkill, bool elementalBurst)
    {
        public string Name { get; set; } = name;
        
        public ElementalType ElementalType { get; set; } = elementalType;
        
        public bool NormalAttack { get; set; } = normalAttack;

        public bool ElementalSkill { get; set; } = elementalSkill;
        
        public bool ElementalBurst { get; set; } = elementalBurst;

        public DateTime LastUseSkillTime { get; set; } = DateTime.MinValue;
        
        public DateTime LastUseBurstTime { get; set; } = DateTime.MinValue;
    }
    
    //全角色元素列表
     public static List<ElementalAvatar> Lists { get; set; } =
    [
        // 水
        new ElementalAvatar("芭芭拉", ElementalType.Hydro, true, true, true),
        new ElementalAvatar("莫娜", ElementalType.Hydro, true, false, true),
        new ElementalAvatar("珊瑚宫心海", ElementalType.Hydro, true, true, true),
        new ElementalAvatar("玛拉妮", ElementalType.Hydro, true, false, true),
        new ElementalAvatar("那维莱特", ElementalType.Hydro, true, true, true),
        new ElementalAvatar("芙宁娜", ElementalType.Hydro, true, false, true),
        new ElementalAvatar("妮露", ElementalType.Hydro, false, true, true),
        new ElementalAvatar("坎蒂斯", ElementalType.Hydro, false, true, true),
        new ElementalAvatar("行秋", ElementalType.Hydro, false, true, true),
        new ElementalAvatar("神里绫人", ElementalType.Hydro, false, true, true),
        new ElementalAvatar("塔利雅", ElementalType.Hydro, false, true, true),
        new ElementalAvatar("希格雯", ElementalType.Hydro, false, true, true),
        new ElementalAvatar("夜兰", ElementalType.Hydro, false, false, true),
        new ElementalAvatar("达达利亚", ElementalType.Hydro, false, false, true),
        // 雷
        new ElementalAvatar("丽莎", ElementalType.Electro, true, true, true),
        new ElementalAvatar("八重神子", ElementalType.Electro, true, false, true),
        new ElementalAvatar("瓦雷莎", ElementalType.Electro, true, false, true),
        new ElementalAvatar("雷电将军", ElementalType.Electro, false, true, true),
        new ElementalAvatar("久岐忍", ElementalType.Electro, false, true, true),
        new ElementalAvatar("北斗", ElementalType.Electro, false, true, true),
        new ElementalAvatar("菲谢尔", ElementalType.Electro, false, true, true),
        new ElementalAvatar("雷泽", ElementalType.Electro, false, true, true),
        new ElementalAvatar("伊涅芙", ElementalType.Electro, false, true, true),
        new ElementalAvatar("伊安珊", ElementalType.Electro, false, false, true),
        new ElementalAvatar("欧洛伦", ElementalType.Electro, false, true, true),
        new ElementalAvatar("克洛琳德", ElementalType.Electro, false, false, true),
        new ElementalAvatar("赛索斯", ElementalType.Electro, false, false, true),
        new ElementalAvatar("赛诺", ElementalType.Electro, false, false, true),
        new ElementalAvatar("多莉", ElementalType.Electro, false, true, true),
        new ElementalAvatar("九条裟罗", ElementalType.Electro, false, false, true),
        new ElementalAvatar("刻晴", ElementalType.Electro, false, false, true),
        // 风
        new ElementalAvatar("砂糖", ElementalType.Anemo, true, true, true),
        new ElementalAvatar("鹿野院平藏", ElementalType.Anemo, true, true, true),
        new ElementalAvatar("流浪者", ElementalType.Anemo, true, false, true),
        new ElementalAvatar("闲云", ElementalType.Anemo, true, false, true),
        new ElementalAvatar("蓝砚", ElementalType.Anemo, true, false, true),
        new ElementalAvatar("枫原万叶", ElementalType.Anemo, false, true, true),
        new ElementalAvatar("珐露珊", ElementalType.Anemo, false, true, true),
        new ElementalAvatar("琳妮特", ElementalType.Anemo, false, true, true),
        new ElementalAvatar("温迪", ElementalType.Anemo, false, true, true),
        new ElementalAvatar("琴", ElementalType.Anemo, false, true, true),
        new ElementalAvatar("早柚", ElementalType.Anemo, false, true, true),
        new ElementalAvatar("伊法", ElementalType.Anemo, true, false, true),
        new ElementalAvatar("梦见月瑞希", ElementalType.Anemo, true, false, true),
        new ElementalAvatar("恰斯卡", ElementalType.Anemo, false, false, true),
        new ElementalAvatar("魈", ElementalType.Anemo, false, false, true),
        // 火
        new ElementalAvatar("烟绯", ElementalType.Pyro, true, true, true),
        new ElementalAvatar("迪卢克", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("可莉", ElementalType.Pyro, true, true, true),
        new ElementalAvatar("班尼特", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("香菱", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("托马", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("胡桃", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("迪希雅", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("夏沃蕾", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("辛焱", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("林尼", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("宵宫", ElementalType.Pyro, false, true, true),
        new ElementalAvatar("玛薇卡", ElementalType.Pyro, false, false, true),
        new ElementalAvatar("阿蕾奇诺", ElementalType.Pyro, false, false, true),
        new ElementalAvatar("嘉明", ElementalType.Pyro, false, false, true),
        new ElementalAvatar("安柏", ElementalType.Pyro, false, false, true),
        // 草
        new ElementalAvatar("基尼奇", ElementalType.Dendro, false, false, true),
        new ElementalAvatar("艾梅莉埃", ElementalType.Dendro, false, true, true),
        new ElementalAvatar("绮良良", ElementalType.Dendro, false, true, true),
        new ElementalAvatar("白术", ElementalType.Dendro, true, true, true),
        new ElementalAvatar("卡维", ElementalType.Dendro, false, true, true),
        new ElementalAvatar("艾尔海森", ElementalType.Dendro, false, false, true),
        new ElementalAvatar("瑶瑶", ElementalType.Dendro, false, false, true),
        new ElementalAvatar("纳西妲", ElementalType.Dendro, true, true, true),
        new ElementalAvatar("提纳里", ElementalType.Dendro, false, true, true),
        new ElementalAvatar("柯莱", ElementalType.Dendro, false, true, true),
        // 岩
        new ElementalAvatar("希诺宁", ElementalType.Geo, false, false, true),
        new ElementalAvatar("卡齐娜", ElementalType.Geo, false, true, true),
        new ElementalAvatar("千织", ElementalType.Geo, false, true, true),
        new ElementalAvatar("钟离", ElementalType.Geo, false, true, true),
        new ElementalAvatar("娜维娅", ElementalType.Geo, false, true, true),
        new ElementalAvatar("云堇", ElementalType.Geo, false, true, true),
        new ElementalAvatar("荒泷一斗", ElementalType.Geo, false, true, true),
        new ElementalAvatar("五郎", ElementalType.Geo, false, true, true),
        new ElementalAvatar("阿贝多", ElementalType.Geo, false, true, true),
        new ElementalAvatar("诺艾尔", ElementalType.Geo, false, true, true),
        new ElementalAvatar("凝光", ElementalType.Geo, true, true, true),
        // 冰
        new ElementalAvatar("茜特菈莉", ElementalType.Cryo, true, true, true),
        new ElementalAvatar("丝柯克", ElementalType.Cryo, false, false, true),
        new ElementalAvatar("爱可菲", ElementalType.Cryo, false, true, true),
        new ElementalAvatar("夏洛蒂", ElementalType.Cryo, true, true, true),
        new ElementalAvatar("莱欧斯利", ElementalType.Cryo, true, false, true),
        new ElementalAvatar("菲米尼", ElementalType.Cryo, false, true, true),
        new ElementalAvatar("米卡", ElementalType.Cryo, false, true, true),
        new ElementalAvatar("莱依拉", ElementalType.Cryo, false, true, true),
        new ElementalAvatar("申鹤", ElementalType.Cryo, false, false, true),
        new ElementalAvatar("埃洛伊", ElementalType.Cryo, false, false, true),
        new ElementalAvatar("神里绫华", ElementalType.Cryo, false, true, true),
        new ElementalAvatar("优菈", ElementalType.Cryo, false, false, true),
        new ElementalAvatar("罗莎莉亚", ElementalType.Cryo, false, true, true),
        new ElementalAvatar("甘雨", ElementalType.Cryo, false, false, true),
        new ElementalAvatar("迪奥娜", ElementalType.Cryo, false, false, true),
        new ElementalAvatar("七七", ElementalType.Cryo, false, true, true),
        new ElementalAvatar("重云", ElementalType.Cryo, false, false, true),
        new ElementalAvatar("凯亚", ElementalType.Cryo, false, true, true),
    ];
}
