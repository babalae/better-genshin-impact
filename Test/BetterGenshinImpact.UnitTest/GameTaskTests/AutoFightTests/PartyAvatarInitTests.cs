using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Model;
using Microsoft.Extensions.Configuration;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>
/// 角色编号，角色头像识别测试
/// </summary>
public class PartyAvatarInitTests
{
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
    private static BgiYoloPredictor predictor;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
    private static BgiYoloPredictor Predictor
    {
        get
        {
            return LazyInitializer.EnsureInitialized(ref predictor,
                () => new BgiOnnxFactory(new FakeLogger<BgiOnnxFactory>())
                    .CreateYoloPredictor(BgiOnnxModel.BgiAvatarSide));
        }
    }
    internal class AutoFightAssets : GameTask.AutoFight.Assets.AutoFightAssets
    {
        internal AutoFightAssets(ISystemInfo systemInfo) : base(systemInfo)
        {
        }
    }
    internal class ElementAssets : GameTask.Common.Element.Assets.ElementAssets
    {
        internal ElementAssets(ISystemInfo systemInfo) : base(systemInfo)
        {
        }
    }

    /// <summary>
    /// 测试普通的多人游戏下的角色头像识别
    /// </summary>
    [Theory]
    [InlineData(@"AutoFight\联机满编\别人进我世界_2人.png", new[] { "阿蕾奇诺", "钟离" })]
    [InlineData(@"AutoFight\联机满编\别人进我世界_3人.png", new[] { "阿蕾奇诺", "钟离" })]
    [InlineData(@"AutoFight\联机满编\别人进我世界_4人.png", new[] { "阿蕾奇诺" })]
    [InlineData(@"AutoFight\联机满编\别人进我世界_4人_2.png", new[] { "阿蕾奇诺" })]
    [InlineData(@"AutoFight\联机满编\我进别人世界_2人.png", new[] { "阿蕾奇诺", "钟离" })]
    [InlineData(@"AutoFight\联机满编\我进别人世界_3人.png", new[] { "阿蕾奇诺" })]
    [InlineData(@"AutoFight\联机满编\我进别人世界_4人.png", new[] { "阿蕾奇诺" })]
    // 以下5条测试在Assets中无数据
    //[InlineData(@"AutoFight\可识别异常场景\单人队.png", new[] { "茜特菈莉" })]
    //[InlineData(@"AutoFight\可识别异常场景\三人队.png", new[] { "雷电将军", "温迪", "枫原万叶" })]
    //[InlineData(@"AutoFight\可识别异常场景\小三角能识别_出战2号位无法识别.png", new[] { "丝柯克", "爱可菲", "夜兰", "芙宁娜" })]
    //[InlineData(@"AutoFight\可识别异常场景\小三角能识别_出战4号位无法识别.png", new[] { "丝柯克", "爱可菲", "夜兰", "芙宁娜" })]
    //[InlineData(@"AutoFight\可识别异常场景\草露.png", new[] { "纳西妲", "空", "芙宁娜", "菈乌玛" })]
    public void RecognisePartyAvatar_New_AvatarShouldBeRight(string screenshot1080P, string[]? expectedNames = null)
    {
        //
        Mat mat = new Mat(@$"..\..\..\Assets\{screenshot1080P}");
        FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);
        // 桌面 -> 游戏捕获区域 -> 1080P区域
        var gameCaptureRegion = systemInfo.DesktopRectArea.Derive(mat, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);
        var imageRegion = gameCaptureRegion.DeriveTo1080P();

        AutoFightConfig autoFightConfig = new AutoFightConfig();

        FakeLogger logger = new FakeLogger();

        //
        var combatScenes = new CombatScenes(Predictor, new AutoFightAssets(systemInfo), logger, new ElementAssets(systemInfo), systemInfo).InitializeTeam(imageRegion, autoFightConfig);

        //
        Assert.True(combatScenes.CheckTeamInitialized());
        if (expectedNames != null)
        {
            Assert.Equal(expectedNames.Length, combatScenes.AvatarCount);
            for (var i = 0; i < expectedNames.Length; i++)
            {
                Assert.Equal(expectedNames[i], combatScenes.GetAvatars()[i].Name);
            }
        }
    }

    /// <summary>
    /// 测试普通的多人游戏下的角色头像识别
    /// </summary>
    // [Theory]
    // [InlineData(@"AutoFight\联机满编\别人进我世界_2人.png", new[] { "阿蕾奇诺", "钟离" })]
    // public void WhatAvatarIsActive_New_AvatarShouldBeRight(string screenshot1080P)
    // {
    //     //
    //     TaskContext.Instance().InitFakeForTest();
    //     Mat mat = new Mat(@$"..\..\..\Assets\{screenshot1080P}");
    //     var systemInfo = TaskContext.Instance().SystemInfo;
    //     // 桌面 -> 游戏捕获区域 -> 1080P区域
    //     var gameCaptureRegion = systemInfo.DesktopRectArea.Derive(mat, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);
    //     var imageRegion = gameCaptureRegion.DeriveTo1080P();
    //
    //
    //     //
    //     var combatScenes = new CombatScenes().InitializeTeam(imageRegion);
    //
    //     //
    //     Assert.True(combatScenes.CheckTeamInitialized());
    //
    // }
}