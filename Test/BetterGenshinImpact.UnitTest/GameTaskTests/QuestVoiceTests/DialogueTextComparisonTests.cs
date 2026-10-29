using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>验证 OCR 比较保留语义差异，仅对明确格式波动直接去重。</summary>
public sealed class DialogueTextComparisonTests
{
    /// <summary>格式、逗号、短句和轻微长句误识分别进入明确的比较分类。</summary>
    [Theory]
    [InlineData("晚安啦，小可爱", "晚安啦小可爱", DialogueTextRelation.Equivalent)]
    [InlineData("晚安啦小可爱", "晚安啦，小可爱", DialogueTextRelation.Equivalent)]
    [InlineData("晚安啦，小可爱！", "晚安啦小可爱", DialogueTextRelation.Equivalent)]
    [InlineData("你好，旅行者。", "你好,旅行者", DialogueTextRelation.Equivalent)]
    [InlineData("你好！", "你好", DialogueTextRelation.Equivalent)]
    [InlineData("你好？", "你好！", DialogueTextRelation.Different)]
    [InlineData("等等……", "等等…", DialogueTextRelation.Equivalent)]
    [InlineData("等等...", "等等……", DialogueTextRelation.Equivalent)]
    [InlineData("不，行！", "不行！", DialogueTextRelation.Different)]
    [InlineData("好，吧", "好吧", DialogueTextRelation.Different)]
    [InlineData("我们不能走，大家留下", "我们不能走大家留下", DialogueTextRelation.Different)]
    [InlineData("明天出发吧，大家准备", "明天出发吧大家准备", DialogueTextRelation.Equivalent)]
    [InlineData("晚安，啦，小可爱", "晚安啦小可爱", DialogueTextRelation.Different)]
    [InlineData("晚安啦、小可爱", "晚安啦，小可爱", DialogueTextRelation.Different)]
    [InlineData("爸爸，姐姐在这里", "爸爸姐姐在这里", DialogueTextRelation.Different)]
    [InlineData("我的爸爸，姐姐在这里", "我的爸爸姐姐在这里", DialogueTextRelation.Different)]
    [InlineData("今天真好，大家准备", "今天真好大家准备", DialogueTextRelation.Different)]
    [InlineData("今天我们一起去城外看看吧！", "明天我们一起去城外看看吧", DialogueTextRelation.NeedsConfirmation)]
    [InlineData("今天我们一起去城外看看吧！", "明天我们一起去城外看看吧？", DialogueTextRelation.Different)]
    [InlineData("1,000", "1000", DialogueTextRelation.Different)]
    [InlineData("版本1.2", "版本12", DialogueTextRelation.Different)]
    [InlineData("Hello, friend", "Hello friend", DialogueTextRelation.Different)]
    [InlineData("We can go", "We can't go", DialogueTextRelation.Different)]
    [InlineData("ＡＢＣ　１２３！", "ABC 123!", DialogueTextRelation.Equivalent)]
    [InlineData("旅 行 者\r\n你好　！", "旅行者你好!", DialogueTextRelation.Equivalent)]
    [InlineData("Model A", "ModelA", DialogueTextRelation.Different)]
    [InlineData("1 2", "12", DialogueTextRelation.Different)]
    [InlineData("是", "否", DialogueTextRelation.Different)]
    [InlineData("走吧", "等吧", DialogueTextRelation.Different)]
    [InlineData("今天我们一起去城外看看吧", "明天我们一起去城外看看吧", DialogueTextRelation.NeedsConfirmation)]
    [InlineData("今天我们一起去城外看看吧", "今天我一起去城外看看吧", DialogueTextRelation.NeedsConfirmation)]
    [InlineData("今天我们一起去城外看看吧", "天我们一起去城外看看吧", DialogueTextRelation.NeedsConfirmation)]
    [InlineData("今天我们一起去城外看看吧", "今天我们一起去城外看一看吧", DialogueTextRelation.NeedsConfirmation)]
    [InlineData("晚安啦小可爱", "K晚安啦小可爱", DialogueTextRelation.NeedsConfirmation)]
    [InlineData("M晚安啦小可爱", "晚安啦小可爱", DialogueTextRelation.NeedsConfirmation)]
    [InlineData("晚安啦小可爱H", "晚安啦小可爱", DialogueTextRelation.NeedsConfirmation)]
    [InlineData("晚安啦小可爱", "晚安啦小可爱Q", DialogueTextRelation.Different)]
    [InlineData("K", "M", DialogueTextRelation.Different)]
    [InlineData("ModelK", "ModelM", DialogueTextRelation.Different)]
    [InlineData("今天我们一起去城外看看1", "今天我们一起去城外看看2", DialogueTextRelation.Different)]
    [InlineData("今天我们一起去城外看看吧", "今天我们先回蒙德休息吧", DialogueTextRelation.Different)]
    [InlineData("", "今天我们一起去城外看看吧", DialogueTextRelation.Different)]
    [InlineData("今天我们一起去城外看看吧", "", DialogueTextRelation.Different)]
    public void Compare_preserves_semantics_and_marks_uncertain_ocr(string accepted, string incoming, DialogueTextRelation expected)
    {
        Assert.Equal(expected, DialogueTextComparison.Compare(accepted, incoming));
        Assert.Equal(expected, DialogueTextComparison.Compare(incoming, accepted));
    }

    /// <summary>初次中文正文边缘的单个 KMH 请求确认，但不删除真实正文。</summary>
    [Theory]
    [InlineData("K晚安啦小可爱", true)]
    [InlineData("晚安啦小可爱M！", true)]
    [InlineData("H今天我们一起去城外看看吧", true)]
    [InlineData("晚安啦小可爱", false)]
    [InlineData("ModelK", false)]
    [InlineData("K你好", false)]
    [InlineData("KK晚安啦小可爱", false)]
    public void Initial_noise_requires_evidence(string raw, bool expected)
        => Assert.Equal(expected, DialogueTextComparison.RequiresInitialConfirmation(raw));

    /// <summary>比较键规范化独立于 raw，内部标点与疑似噪声均保留。</summary>
    [Fact]
    public void Key_is_separate_from_raw_and_preserves_meaningful_characters()
    {
        const string raw = "Ｋ晚安啦， 小可爱！";
        Assert.Equal("K晚安啦,小可爱!", DialogueTextComparison.CreateComparisonKey(raw));
        Assert.Equal("Ｋ晚安啦， 小可爱！", raw);
        Assert.NotEqual(DialogueTextComparison.CreateComparisonKey("不，行！"), DialogueTextComparison.CreateComparisonKey("不行！"));
        Assert.Equal("", DialogueTextComparison.CreateComparisonKey(null));
    }
}