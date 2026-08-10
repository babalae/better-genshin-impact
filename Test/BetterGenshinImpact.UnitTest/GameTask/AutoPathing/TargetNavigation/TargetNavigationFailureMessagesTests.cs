using BetterGenshinImpact.GameTask.AutoPathing.TargetNavigation;

namespace BetterGenshinImpact.UnitTest.AutoPathing.TargetNavigation;

public class TargetNavigationFailureMessagesTests
{
    [Theory]
    [InlineData(TargetNavigationFailureCode.TargetNotSelected, "未选择目标")]
    [InlineData(TargetNavigationFailureCode.GraphFileMissing, "路网文件不存在")]
    [InlineData(TargetNavigationFailureCode.GraphEmpty, "路网为空")]
    [InlineData(TargetNavigationFailureCode.CurrentPositionUnrecognized, "当前坐标不可识别")]
    [InlineData(TargetNavigationFailureCode.CurrentPointNotConnected, "当前点无法接入路网")]
    [InlineData(TargetNavigationFailureCode.TargetPointNotConnected, "目标点无法接入路网")]
    [InlineData(TargetNavigationFailureCode.MapMismatch, "当前地图和目标地图不一致")]
    [InlineData(TargetNavigationFailureCode.NoRoute, "没有可用路径")]
    [InlineData(TargetNavigationFailureCode.TeleportUnavailable, "传送点不可用")]
    [InlineData(TargetNavigationFailureCode.CaptureNotInitialized, "截图器未初始化")]
    [InlineData(TargetNavigationFailureCode.GameWindowNotFound, "原神窗口不存在")]
    [InlineData(TargetNavigationFailureCode.NotInMainUi, "当前不在主界面")]
    [InlineData(TargetNavigationFailureCode.GraphNotLoaded, "路网尚未加载")]
    [InlineData(TargetNavigationFailureCode.TaskRunnerBusy, "其他独立任务正在运行")]
    [InlineData(TargetNavigationFailureCode.WindowActivationFailed, "原神窗口激活失败")]
    [InlineData(TargetNavigationFailureCode.GameWindowLostFocus, "原神窗口已失去前台")]
    public void Format_ReturnsExplicitChineseReason(TargetNavigationFailureCode code, string expected)
    {
        Assert.Contains(expected, TargetNavigationFailureMessages.Format(code));
    }
}
