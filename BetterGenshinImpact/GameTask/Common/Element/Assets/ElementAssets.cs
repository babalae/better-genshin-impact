using System;
using System.Collections.Generic;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers.Extensions;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public class ElementAssets : BaseAssets<ElementAssets>
{
    public RecognitionObject PromptDialogLeftBottomStar; // 弹出框左下角的星星

    public RecognitionObject BtnWhiteConfirm;
    public RecognitionObject BtnWhiteCancel;
    public RecognitionObject BtnBlackConfirm;
    public RecognitionObject BtnBlackCancel;
    public RecognitionObject BtnOnlineYes;
    public RecognitionObject BtnOnlineNo;
    public Lazy<RecognitionObject> BtnExitDoor;
    public RecognitionObject InDomainRo;

    public RecognitionObject PaimonMenuRo;
    public RecognitionObject BlueTrackPoint;

    public RecognitionObject UiLeftTopCookIcon;

    public RecognitionObject SpaceKey;
    public RecognitionObject XKey;

    public RecognitionObject FriendChat;

    public RecognitionObject PartyBtnChooseView;
    public RecognitionObject PartyBtnDelete;

    public RecognitionObject CraftCondensedResin;
    public RecognitionObject CondensedResinCount;
    public RecognitionObject fragileResinCount;
    public RecognitionObject Keyreduce;
    public RecognitionObject Keyincrease;

    public RecognitionObject BagWeaponUnchecked;
    public RecognitionObject BagWeaponChecked;
    public RecognitionObject BagArtifactUnchecked;
    public RecognitionObject BagArtifactChecked;
    public RecognitionObject BagCharacterDevelopmentItemUnchecked;
    public RecognitionObject BagCharacterDevelopmentItemChecked;
    public RecognitionObject BagFoodUnchecked;
    public RecognitionObject BagFoodChecked;
    public RecognitionObject BagMaterialUnchecked;
    public RecognitionObject BagMaterialChecked;
    public RecognitionObject BagGadgetUnchecked;
    public RecognitionObject BagGadgetChecked;
    public RecognitionObject BagQuestUnchecked;
    public RecognitionObject BagQuestChecked;
    public RecognitionObject BagPreciousItemUnchecked;
    public RecognitionObject BagPreciousItemChecked;
    public RecognitionObject BagFurnishingUnchecked;
    public RecognitionObject BagFurnishingChecked;
    public RecognitionObject BtnArtifactSalvage;
    public RecognitionObject BtnArtifactSalvageConfirm;

    public RecognitionObject BtnClaimEncounterPointsRewards;
    public RecognitionObject PrimogemRo;

    public RecognitionObject EscMailReward;
    public RecognitionObject CollectRo;

    public RecognitionObject PageCloseWhiteRo;

    public RecognitionObject SereniteaPotHomeRo;
    public RecognitionObject TeleportSereniteaPotHomeRo;
    public RecognitionObject AYuanIconRo;
    public RecognitionObject SereniteaPotLoveRo;
    public RecognitionObject SereniteaPotMoneyRo;
    public RecognitionObject SereniteapotPageClose;
    public RecognitionObject SereniteapotShopNumberBtn;

    public RecognitionObject AYuanClothRo;
    public RecognitionObject AYuanresinRo;
    public RecognitionObject SereniteapotExpBookRo;
    public RecognitionObject SereniteapotExpBookSmallRo;
    public RecognitionObject AYuanMagicmineralprecisionRo;
    public RecognitionObject AYuanMOlaRo;
    public RecognitionObject AYuanExpBottleBigRo;
    public RecognitionObject AYuanExpBottleSmallRo;
    public RecognitionObject FingerIconRo;

    public RecognitionObject LeylineDisorderIconRo;

    public RecognitionObject Index1;
    public RecognitionObject Index2;
    public RecognitionObject Index3;
    public RecognitionObject Index4;
    public List<RecognitionObject> IndexList => [Index1, Index2, Index3, Index4];
    public RecognitionObject CurrentAvatarThreshold;


#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
    private ElementAssets() : base()
    {
        Initialization(this.systemInfo);
    }

    protected ElementAssets(ISystemInfo systemInfo) : base(systemInfo)
    {
        Initialization(systemInfo);
    }
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。

    private void Initialization(ISystemInfo systemInfo)
    {
        PromptDialogLeftBottomStar = new RecognitionObject
        {
            Name = "PromptDialogLeftBottomStar",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "prompt_dialog_left_bottom_star.png", systemInfo),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height - CaptureRect.Height / 2),
            Threshold = 0.8,
        }.InitTemplate();
        // 按钮
        BtnWhiteConfirm = new RecognitionObject
        {
            Name = "BtnWhiteConfirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_white_confirm.png", systemInfo),
            Use3Channels = true
        }.InitTemplate();
        BtnWhiteCancel = new RecognitionObject
        {
            Name = "BtnWhiteCancel",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_white_cancel.png", systemInfo),
            Use3Channels = true
        }.InitTemplate();
        BtnBlackConfirm = new RecognitionObject
        {
            Name = "BtnBlackConfirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_black_confirm.png", systemInfo),
            Use3Channels = true
        }.InitTemplate();
        BtnBlackCancel = new RecognitionObject
        {
            Name = "BtnBlackCancel",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_black_cancel.png", systemInfo),
            Use3Channels = true
        }.InitTemplate();
        BtnOnlineYes = new RecognitionObject
        {
            Name = "BtnOnlineYes",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_online_yes.png", systemInfo),
            Use3Channels = true
        }.InitTemplate();
        BtnOnlineNo = new RecognitionObject
        {
            Name = "BtnOnlineNo",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_online_no.png", systemInfo),
            Use3Channels = true
        }.InitTemplate();
        BtnExitDoor = new Lazy<RecognitionObject>(() => new RecognitionObject
        {
            Name = "BtnExitDoor",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_exit_door.png", systemInfo),
            DrawOnWindow = false
        }.InitTemplate());

        // 秘境退出图标
        InDomainRo = new RecognitionObject
        {
            Name = "InDomain",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "in_domain.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 4, CaptureRect.Height / 4),
            DrawOnWindow = false
        }.InitTemplate();

        // 派蒙菜单
        // 此图38x40 小地图210x210 小地图左上角位置 24,-15
        PaimonMenuRo = new RecognitionObject
        {
            Name = "PaimonMenu",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "paimon_menu.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 4, CaptureRect.Height / 4),
            DrawOnWindow = false
        }.InitTemplate();

        // 任务追踪点位
        BlueTrackPoint = new RecognitionObject
        {
            Name = "BlueTrackPoint",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "blue_track_point_28x.png", systemInfo),
            RegionOfInterest = new Rect((int)(300 * AssetScale), 0, CaptureRect.Width - (int)(600 * AssetScale), CaptureRect.Height),
            Threshold = 0.6,
            DrawOnWindow = true
        }.InitTemplate();

        // 左上角UI元素
        UiLeftTopCookIcon = new RecognitionObject
        {
            Name = "UiLeftTopCookIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "ui_left_top_cook_icon.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, (int)(150 * AssetScale), (int)(120 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        // 右下角的按键提示
        SpaceKey = new RecognitionObject
        {
            Name = "SpaceKey",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "key_space.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(350 * AssetScale), CaptureRect.Height - (int)(70 * AssetScale), (int)(200 * AssetScale), (int)(70 * AssetScale)),

            DrawOnWindow = false
        }.InitTemplate();
        XKey = new RecognitionObject
        {
            Name = "XKey",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "key_x.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(350 * AssetScale), CaptureRect.Height - (int)(70 * AssetScale), (int)(200 * AssetScale), (int)(70 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        // 左下角的好友聊天icon
        FriendChat = new RecognitionObject
        {
            Name = "FriendChat",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "friend_chat.png", systemInfo),
            RegionOfInterest = new Rect(0, CaptureRect.Height - (int)(70 * AssetScale), (int)(83 * AssetScale), (int)(70 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        // 队伍切换
        PartyBtnChooseView = new RecognitionObject
        {
            Name = "PartyBtnChooseView",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "party_btn_choose_view.png", systemInfo),
            RegionOfInterest = new Rect(0, CaptureRect.Height - (int)(120 * AssetScale), CaptureRect.Width / 7, (int)(120 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
        PartyBtnDelete = new RecognitionObject
        {
            Name = "PartyBtnDelete",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "party_btn_delete.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 4, CaptureRect.Height - (int)(120 * AssetScale), CaptureRect.Width / 2, (int)(120 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        // 合成树脂
        CraftCondensedResin = new RecognitionObject
        {
            Name = "CraftCondensedResin",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "craft_condensed_resin.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, 0, CaptureRect.Width / 2, CaptureRect.Height / 3 * 2),
            DrawOnWindow = false
        }.InitTemplate();
        // 树脂数量
        fragileResinCount = new RecognitionObject
        {
            Name = "fragileResinCount",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "fragile_resin_count.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height * 3 / 4, CaptureRect.Width / 3, CaptureRect.Height / 6),
            DrawOnWindow = true
        }.InitTemplate();
        CondensedResinCount = new RecognitionObject
        {
            Name = "CondensedResinCount",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "condensed_resin_count.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, 0, CaptureRect.Width / 4, CaptureRect.Height / 15),
            DrawOnWindow = true
        }.InitTemplate();
        // 减少合成
        Keyreduce = new RecognitionObject
        {
            Name = "Keyreduce",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "key_reduce.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();
        // 增加合成
        Keyincrease = new RecognitionObject
        {
            Name = "Keyincrease",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "key_increase.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        // 背包武器
        BagWeaponUnchecked = new RecognitionObject
        {
            Name = "BagWeaponUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_weapon_unchecked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagWeaponChecked = new RecognitionObject
        {
            Name = "BagWeaponChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_weapon_checked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
        // 背包圣遗物
        BagArtifactUnchecked = new RecognitionObject
        {
            Name = "BagArtifactUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_artifact_unchecked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagArtifactChecked = new RecognitionObject
        {
            Name = "BagArtifactChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_artifact_checked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
        // 背包养成道具
        BagCharacterDevelopmentItemUnchecked = new RecognitionObject
        {
            Name = "BagCharacterDevelopmentItemUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_characterdevelopmentitem_unchecked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagCharacterDevelopmentItemChecked = new RecognitionObject
        {
            Name = "BagCharacterDevelopmentItemChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_characterdevelopmentitem_checked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
        // 背包食物
        BagFoodUnchecked = new RecognitionObject
        {
            Name = "BagFoodUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_food_unchecked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagFoodChecked = new RecognitionObject
        {
            Name = "BagFoodChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_food_checked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
        // 背包材料
        BagMaterialUnchecked = new RecognitionObject
        {
            Name = "BagMaterialUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_material_unchecked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagMaterialChecked = new RecognitionObject
        {
            Name = "BagMaterialChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_material_checked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
        // 背包小道具
        BagGadgetUnchecked = new RecognitionObject
        {
            Name = "BagGadgetUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_gadget_unchecked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagGadgetChecked = new RecognitionObject
        {
            Name = "BagGadgetChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_gadget_checked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
        // 背包任务
        BagQuestUnchecked = new RecognitionObject
        {
            Name = "BagQuestUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_quest_unchecked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagQuestChecked = new RecognitionObject
        {
            Name = "BagQuestChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_quest_checked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
        // 背包贵重道具
        BagPreciousItemUnchecked = new RecognitionObject
        {
            Name = "BagPreciousItemUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_preciousitem_unchecked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagPreciousItemChecked = new RecognitionObject
        {
            Name = "BagPreciousItemChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_preciousitem_checked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
        // 背包摆设
        BagFurnishingUnchecked = new RecognitionObject
        {
            Name = "BagFurnishingUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_furnishing_unchecked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagFurnishingChecked = new RecognitionObject
        {
            Name = "BagFurnishingChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_furnishing_checked.png", systemInfo),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();

        // 分解圣遗物
        BtnArtifactSalvage = new RecognitionObject
        {
            Name = "BtnArtifactSalvage",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_artifact_salvage.png", systemInfo),
            RegionOfInterest = CaptureRect.CutBottom(0.1),
            DrawOnWindow = false
        }.InitTemplate();
        BtnArtifactSalvageConfirm = new RecognitionObject
        {
            Name = "BtnArtifactSalvageConfirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_artifact_salvage_confirm.png", systemInfo),
            RegionOfInterest = CaptureRect.CutRightBottom(0.3, 0.1),
            DrawOnWindow = false
        }.InitTemplate();

        // 历练点奖励
        BtnClaimEncounterPointsRewards = new RecognitionObject
        {
            Name = "BtnClaimEncounterPointsRewards",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_claim_encounter_points_rewards.png", systemInfo),
            RegionOfInterest = CaptureRect.CutRightBottom(0.3, 0.5),
            DrawOnWindow = false
        }.InitTemplate();

        PrimogemRo = new RecognitionObject
        {
            Name = "Primogem",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "primogem.png", systemInfo),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 3, CaptureRect.Width, CaptureRect.Height / 3),
            DrawOnWindow = false
        }.InitTemplate();

        // 邮件
        EscMailReward = new RecognitionObject
        {
            Name = "EscMailReward",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "esc_mail_reward.png", systemInfo),
            RegionOfInterest = CaptureRect.CutLeftBottom(0.1, 0.5)
        }.InitTemplate();

        CollectRo = new RecognitionObject
        {
            Name = "Collect",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "collect.png", systemInfo),
            RegionOfInterest = new Rect(0, CaptureRect.Height - CaptureRect.Height / 3, CaptureRect.Width / 4, CaptureRect.Height / 3),
            DrawOnWindow = false
        }.InitTemplate();

        PageCloseWhiteRo = new RecognitionObject
        {
            Name = "PageCloseWhite",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "page_close_white.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 8, 0, CaptureRect.Width / 8, CaptureRect.Height / 8),
            DrawOnWindow = true
        }.InitTemplate();

        // 尘歌壶
        SereniteaPotHomeRo = new RecognitionObject
        {
            Name = "SereniteaPotHome",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "sereniteapot_home.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        TeleportSereniteaPotHomeRo = new RecognitionObject
        {
            Name = "TeleportSereniteaPotHome",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "sereniteapot_home.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();
        AYuanIconRo = new RecognitionObject
        {
            Name = "AYuanIconRo",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "ayuan.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();


        SereniteaPotLoveRo = new RecognitionObject
        {
            Name = "SereniteaPotLoveRo",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "sereniteapot_love.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 8, CaptureRect.Height / 2, CaptureRect.Width / 8, CaptureRect.Height / 4),
            DrawOnWindow = false
        }.InitTemplate();
        SereniteaPotMoneyRo = new RecognitionObject
        {
            Name = "SereniteaPotMoneyRo",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "sereniteapot_money.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height - CaptureRect.Height / 4, CaptureRect.Width / 4, CaptureRect.Height / 4),
            DrawOnWindow = false
        }.InitTemplate();
        AYuanExpBottleBigRo = new RecognitionObject
        {
            Name = "祝圣精华",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "exp_bottle_big.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width * 7 / 10, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        AYuanExpBottleSmallRo = new RecognitionObject
        {
            Name = "祝圣油膏",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "exp_bottle_small.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width * 7 / 10, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        SereniteapotPageClose = new RecognitionObject
        {
            Name = "SereniteapotPageClose",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "sereniteapot_page_close.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 5, CaptureRect.Width / 4, CaptureRect.Height / 8),
            DrawOnWindow = false
        }.InitTemplate();
        SereniteapotShopNumberBtn = new RecognitionObject
        {
            Name = "SereniteapotShopNumberBtn",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "sereniteapot_shop_number_btn.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();
        SereniteapotExpBookRo = new RecognitionObject
        {
            Name = "大英雄的经验",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "exp_book.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width * 7 / 10, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        SereniteapotExpBookSmallRo = new RecognitionObject
        {
            Name = "流浪者的经验",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "exp_book_small.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width * 7 / 10, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        AYuanClothRo = new RecognitionObject
        {
            Name = "布匹",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "ayuan_cloth.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width * 7 / 10, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        AYuanresinRo = new RecognitionObject
        {
            Name = "须臾树脂",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "ayuan_resin.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width * 7 / 10, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        AYuanMagicmineralprecisionRo = new RecognitionObject
        {
            Name = "精锻用魔矿",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "ayuan_magicmineralprecision.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width * 7 / 10, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        AYuanMOlaRo = new RecognitionObject
        {
            Name = "摩拉",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "ayuan_mola.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width * 7 / 10, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        FingerIconRo = new RecognitionObject
        {
            Name = "尘歌壶小手",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "finger.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(650 * AssetScale), 0, (int)(80 * AssetScale), (int)(80 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
        LeylineDisorderIconRo = new RecognitionObject
        {
            Name = "LeylineDisorderIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "leyline_disorder_icon.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, (int)(200 * AssetScale), (int)(200 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();


        Rect partyRect = new Rect(CaptureRect.Width - (int)(65 * AssetScale), (int)(155 * AssetScale), (int)(35 * AssetScale), (int)(600 * AssetScale));
        // 1 2 3 4 按键
        Index1 = new RecognitionObject
        {
            Name = "Index1",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "index_1.png", systemInfo),
            RegionOfInterest = partyRect,
            // DrawOnWindow = true
        }.InitTemplate();
        Index2 = new RecognitionObject
        {
            Name = "Index2",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "index_2.png", systemInfo),
            RegionOfInterest = partyRect,
            // DrawOnWindow = true
        }.InitTemplate();
        Index3 = new RecognitionObject
        {
            Name = "Index3",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "index_3.png", systemInfo),
            RegionOfInterest = partyRect,
            // DrawOnWindow = true
        }.InitTemplate();
        Index4 = new RecognitionObject
        {
            Name = "Index4",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "index_4.png", systemInfo),
            RegionOfInterest = partyRect,
            // DrawOnWindow = true
        }.InitTemplate();
        CurrentAvatarThreshold = new RecognitionObject
        {
            Name = "CurrentAvatarThreshold",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "current_avatar_threshold.png", systemInfo),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(240 * AssetScale), (int)(155 * AssetScale), (int)(210 * AssetScale), (int)(600 * AssetScale)),
            UseBinaryMatch = true,
            BinaryThreshold = 200,
            // DrawOnWindow = true
        }.InitTemplate();
    }
}