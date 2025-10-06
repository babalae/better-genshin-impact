using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>
/// 角色编号，角色头像识别测试
/// </summary>
public class PartyAvatarInitTests
{
    /// <summary>
    /// 测试普通的多人游戏下的角色头像识别
    /// </summary>
    [Theory]
    [InlineData(@"AutoFight\联机满编\别人进我世界_2人.png", 2, new[] { "阿蕾奇诺","钟离" })]
    [InlineData(@"AutoFight\联机满编\别人进我世界_3人.png", 2, new[] { "阿蕾奇诺","钟离" })]
    [InlineData(@"AutoFight\联机满编\别人进我世界_4人.png", 1, new[] { "阿蕾奇诺" })]
    [InlineData(@"AutoFight\联机满编\别人进我世界_4人_2.png", 1, new[] { "阿蕾奇诺" })]
    [InlineData(@"AutoFight\联机满编\我进别人世界_2人.png", 2, new[] { "阿蕾奇诺","钟离" })]
    [InlineData(@"AutoFight\联机满编\我进别人世界_3人.png", 1, new[] { "阿蕾奇诺" })]
    [InlineData(@"AutoFight\联机满编\我进别人世界_4人.png", 1, new[] { "阿蕾奇诺" })]
    public void RecognisePartyAvatar_New_AvatarShouldBeRight(string screenshot1080P, int avatarNum, string[]? expectedNames = null)
    {
        //
        TaskContext.Instance().InitFakeForTest();
        Mat mat = new Mat(@$"..\..\..\Assets\{screenshot1080P}");
        var systemInfo = TaskContext.Instance().SystemInfo;
        // 桌面 -> 游戏捕获区域 -> 1080P区域
        var gameCaptureRegion =  systemInfo.DesktopRectArea.Derive(mat, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);
        var imageRegion = gameCaptureRegion.DeriveTo1080P();


        //
        var combatScenes = new CombatScenes().InitializeTeam(imageRegion);
        
        //
        Assert.True(combatScenes.CheckTeamInitialized());
        Assert.Equal(avatarNum, combatScenes.AvatarCount);
        if (expectedNames != null)
        {
            for (var i = 0; i < expectedNames.Length; i++)
            {
                Assert.Equal(expectedNames[i], combatScenes.GetAvatars()[i].Name);
            }
        }
    }
}