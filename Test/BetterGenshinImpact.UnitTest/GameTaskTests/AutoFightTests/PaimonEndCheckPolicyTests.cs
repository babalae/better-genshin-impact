using BetterGenshinImpact.GameTask.AutoFight;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>
/// 派蒙辅助检测的决策逻辑测试。
///
/// 背景：原实现把"派蒙头像可见"当成不可推翻的"战斗未结束"判决，直接提前返回并跳过黄条判定。
/// 一旦派蒙检测误判（模板过拟合、截图取到按 L 之前的旧帧等），战斗结束检查会被无限次拦截，
/// 自动战斗无法正常结束，日志反复输出"派蒙头像可见，提前跳出战斗结束检查"。
///
/// 新语义："派蒙可见" 只能推出"按 L 未生效、编队界面未打开"，因此应当重按一次 L 复检；
/// 复检仍可见时也必须继续走黄条判定兜底，不允许形成无法收敛的提前退出。
/// </summary>
public class PaimonEndCheckPolicyTests
{
    /// <summary>
    /// 派蒙头像不可见：说明编队界面已打开（或头像已消失），可以直接进行黄条判定
    /// </summary>
    [Fact]
    public void Decide_PaimonNotVisible_ShouldProceedToBarCheck()
    {
        var decision = PaimonEndCheckPolicy.Decide(paimonVisible: false, reopenAttempts: 0);

        Assert.Equal(PaimonCheckDecision.ProceedToBarCheck, decision);
    }

    /// <summary>
    /// 派蒙头像可见且尚未重试：应重按 L 复检，而不是直接认定战斗未结束
    /// </summary>
    [Fact]
    public void Decide_PaimonVisibleFirstTime_ShouldReopenPartyScreen()
    {
        var decision = PaimonEndCheckPolicy.Decide(paimonVisible: true, reopenAttempts: 0);

        Assert.Equal(PaimonCheckDecision.ReopenPartyScreen, decision);
    }

    /// <summary>
    /// 派蒙头像可见且已重试：必须继续走黄条判定兜底，绝不能再次提前退出
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(100)]
    public void Decide_PaimonVisibleAfterReopen_ShouldProceedToBarCheck(int reopenAttempts)
    {
        var decision = PaimonEndCheckPolicy.Decide(paimonVisible: true, reopenAttempts: reopenAttempts);

        Assert.Equal(PaimonCheckDecision.ProceedToBarCheck, decision);
    }

    /// <summary>
    /// 派蒙持续误判可见时，重试次数必须有限并最终收敛到黄条判定，
    /// 保证"派蒙可见"不会无限阻断战斗结束检查
    /// </summary>
    [Fact]
    public void Decide_PaimonAlwaysVisible_ShouldConvergeWithinMaxReopenAttempts()
    {
        var reopenAttempts = 0;
        var decision = PaimonEndCheckPolicy.Decide(paimonVisible: true, reopenAttempts);

        while (decision == PaimonCheckDecision.ReopenPartyScreen)
        {
            reopenAttempts++;
            Assert.True(reopenAttempts <= PaimonEndCheckPolicy.MaxReopenAttempts,
                "重按 L 的次数超过上限，派蒙检测仍可能无限阻断战斗结束检查");
            decision = PaimonEndCheckPolicy.Decide(paimonVisible: true, reopenAttempts);
        }

        Assert.Equal(PaimonCheckDecision.ProceedToBarCheck, decision);
    }

    /// <summary>
    /// 重按 L 的次数上限至少为 1 次（否则等同于取消派蒙辅助检测的复检能力）
    /// </summary>
    [Fact]
    public void MaxReopenAttempts_ShouldAllowAtLeastOneRetry()
    {
        Assert.True(PaimonEndCheckPolicy.MaxReopenAttempts >= 1);
    }

    /// <summary>
    /// 派蒙检测延时不得低于键盘输入到生效的下限：游戏内键鼠响应有 30~60ms 延迟，
    /// 截图来源本身也有延迟，延时过短会取到按 L 之前的旧帧而误判"派蒙可见"
    /// </summary>
    [Fact]
    public void NormalizeDelayMs_ShouldNotBeShorterThanInputLatency()
    {
        Assert.True(PaimonEndCheckPolicy.MinDelayMs >= 100,
            "派蒙检测延时下限低于输入响应延迟，容易取到按 L 之前的旧帧");

        Assert.Equal(PaimonEndCheckPolicy.MinDelayMs, PaimonEndCheckPolicy.NormalizeDelayMs(0));
        Assert.Equal(PaimonEndCheckPolicy.MinDelayMs, PaimonEndCheckPolicy.NormalizeDelayMs(75));
    }

    /// <summary>
    /// 派蒙检测延时上限保持可配置范围约束，避免等待过久把延时拖成"真正的界面等待"
    /// </summary>
    [Theory]
    [InlineData(200, 200)]
    [InlineData(400, 400)]
    [InlineData(9999, 400)]
    public void NormalizeDelayMs_ShouldClampIntoSupportedRange(int configuredMs, int expectedMs)
    {
        Assert.Equal(expectedMs, PaimonEndCheckPolicy.NormalizeDelayMs(configuredMs));
    }

    /// <summary>
    /// 重按 L 复检会额外消耗等待时间，补足延时必须扣掉已等待部分，
    /// 否则总等待会超过 detectDelayTime，把编队界面真的等出来
    /// </summary>
    [Theory]
    [InlineData(450, 200, 250)]
    [InlineData(450, 400, 50)]
    [InlineData(450, 600, 0)]
    [InlineData(450, 0, 450)]
    public void RemainingDelayMs_ShouldNotExceedDetectDelayTime(int detectDelayTime, int waitedMs, int expectedMs)
    {
        var remaining = PaimonEndCheckPolicy.RemainingDelayMs(detectDelayTime, waitedMs);

        Assert.Equal(expectedMs, remaining);
        Assert.True(waitedMs + remaining <= Math.Max(waitedMs, detectDelayTime),
            "补足后的总等待时长不应超过 detectDelayTime");
    }
}
