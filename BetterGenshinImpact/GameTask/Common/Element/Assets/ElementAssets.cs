﻿using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers.Extensions;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public class ElementAssets : BaseAssets<ElementAssets>
{
    public RecognitionObject BtnWhiteConfirm;
    public RecognitionObject BtnWhiteCancel;
    public RecognitionObject BtnBlackConfirm;
    public RecognitionObject BtnBlackCancel;
    public RecognitionObject BtnOnlineYes;
    public RecognitionObject BtnOnlineNo;

    public RecognitionObject PaimonMenuRo;
    public RecognitionObject BlueTrackPoint;

    public RecognitionObject UiLeftTopCookIcon;

    public RecognitionObject SpaceKey;
    public RecognitionObject XKey;

    public RecognitionObject FriendChat;

    public RecognitionObject PartyBtnChooseView;
    public RecognitionObject PartyBtnDelete;

    public RecognitionObject CraftCondensedResin;

    public RecognitionObject BagArtifactUnchecked;
    public RecognitionObject BagArtifactChecked;
    public RecognitionObject BtnArtifactSalvage;
    public RecognitionObject BtnArtifactSalvageConfirm;

    public RecognitionObject BtnClaimEncounterPointsRewards;
    public RecognitionObject PrimogemRo;

    public RecognitionObject EscMailReward;
    public RecognitionObject CollectRo;
    
    public RecognitionObject PageCloseWhiteRo;


    private ElementAssets()
    {
        // 按钮
        BtnWhiteConfirm = new RecognitionObject
        {
            Name = "BtnWhiteConfirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_white_confirm.png"),
            Use3Channels = true
        }.InitTemplate();
        BtnWhiteCancel = new RecognitionObject
        {
            Name = "BtnWhiteCancel",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_white_cancel.png"),
            Use3Channels = true
        }.InitTemplate();
        BtnBlackConfirm = new RecognitionObject
        {
            Name = "BtnBlackConfirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_black_confirm.png"),
            Use3Channels = true
        }.InitTemplate();
        BtnBlackCancel = new RecognitionObject
        {
            Name = "BtnBlackCancel",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_black_cancel.png"),
            Use3Channels = true
        }.InitTemplate();
        BtnOnlineYes = new RecognitionObject
        {
            Name = "BtnOnlineYes",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_online_yes.png"),
            Use3Channels = true
        }.InitTemplate();
        BtnOnlineNo = new RecognitionObject
        {
            Name = "BtnOnlineNo",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_online_no.png"),
            Use3Channels = true
        }.InitTemplate();

        // 派蒙菜单
        // 此图38x40 小地图210x210 小地图左上角位置 24,-15
        PaimonMenuRo = new RecognitionObject
        {
            Name = "PaimonMenu",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "paimon_menu.png"),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 4, CaptureRect.Height / 4),
            DrawOnWindow = false
        }.InitTemplate();

        // 任务追踪点位
        BlueTrackPoint = new RecognitionObject
        {
            Name = "BlueTrackPoint",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "blue_track_point_28x.png"),
            RegionOfInterest = new Rect((int)(300 * AssetScale), 0, CaptureRect.Width - (int)(600 * AssetScale), CaptureRect.Height),
            Threshold = 0.6,
            DrawOnWindow = true
        }.InitTemplate();

        // 左上角UI元素
        UiLeftTopCookIcon = new RecognitionObject
        {
            Name = "UiLeftTopCookIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "ui_left_top_cook_icon.png"),
            RegionOfInterest = new Rect(0, 0, (int)(150 * AssetScale), (int)(120 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        // 右下角的按键提示
        SpaceKey = new RecognitionObject
        {
            Name = "SpaceKey",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "key_space.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(350 * AssetScale), CaptureRect.Height - (int)(70 * AssetScale), (int)(200 * AssetScale), (int)(70 * AssetScale)),

            DrawOnWindow = false
        }.InitTemplate();
        XKey = new RecognitionObject
        {
            Name = "XKey",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "key_x.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(350 * AssetScale), CaptureRect.Height - (int)(70 * AssetScale), (int)(200 * AssetScale), (int)(70 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        // 左下角的好友聊天icon
        FriendChat = new RecognitionObject
        {
            Name = "FriendChat",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "friend_chat.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height - (int)(70 * AssetScale), (int)(83 * AssetScale), (int)(70 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        // 队伍切换
        PartyBtnChooseView = new RecognitionObject
        {
            Name = "PartyBtnChooseView",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "party_btn_choose_view.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height - (int)(120 * AssetScale), CaptureRect.Width / 7, (int)(120 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
        PartyBtnDelete = new RecognitionObject
        {
            Name = "PartyBtnDelete",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "party_btn_delete.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 4, CaptureRect.Height - (int)(120 * AssetScale), CaptureRect.Width / 2, (int)(120 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        // 合成树脂
        CraftCondensedResin = new RecognitionObject
        {
            Name = "CraftCondensedResin",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "craft_condensed_resin.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, 0, CaptureRect.Width / 2, CaptureRect.Height / 3 * 2),
            DrawOnWindow = false
        }.InitTemplate();

        // 分解圣遗物
        BagArtifactUnchecked = new RecognitionObject
        {
            Name = "BagArtifactUnchecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_artifact_unchecked.png"),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.87,
            DrawOnWindow = false
        }.InitTemplate();
        BagArtifactChecked = new RecognitionObject
        {
            Name = "BagArtifactChecked",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "bag_artifact_checked.png"),
            RegionOfInterest = CaptureRect.CutTop(0.1),
            Threshold = 0.8,
            DrawOnWindow = false
        }.InitTemplate();
        BtnArtifactSalvage = new RecognitionObject
        {
            Name = "BtnArtifactSalvage",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_artifact_salvage.png"),
            RegionOfInterest = CaptureRect.CutBottom(0.1),
            DrawOnWindow = false
        }.InitTemplate();
        BtnArtifactSalvageConfirm = new RecognitionObject
        {
            Name = "BtnArtifactSalvageConfirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_artifact_salvage_confirm.png"),
            RegionOfInterest = CaptureRect.CutBottom(0.1),
            DrawOnWindow = false
        }.InitTemplate();

        // 历练点奖励
        BtnClaimEncounterPointsRewards = new RecognitionObject
        {
            Name = "BtnClaimEncounterPointsRewards",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_claim_encounter_points_rewards.png"),
            RegionOfInterest = CaptureRect.CutRightBottom(0.3, 0.5),
            DrawOnWindow = false
        }.InitTemplate();

        PrimogemRo = new RecognitionObject
        {
            Name = "Primogem",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "primogem.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 3, CaptureRect.Width, CaptureRect.Height / 3),
            DrawOnWindow = false
        }.InitTemplate();

        // 邮件
        EscMailReward = new RecognitionObject
        {
            Name = "EscMailReward",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "esc_mail_reward.png"),
            RegionOfInterest = CaptureRect.CutLeftBottom(0.1, 0.5)
        }.InitTemplate();

        CollectRo = new RecognitionObject
        {
            Name = "Collect",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "collect.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height - CaptureRect.Height / 3, CaptureRect.Width / 4, CaptureRect.Height / 3),
            DrawOnWindow = false
        }.InitTemplate();
        
        PageCloseWhiteRo = new RecognitionObject
        {
            Name = "PageCloseWhite",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "page_close_white.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 8, 0, CaptureRect.Width / 8, CaptureRect.Height / 8),
            DrawOnWindow = true
        }.InitTemplate();
    }
}