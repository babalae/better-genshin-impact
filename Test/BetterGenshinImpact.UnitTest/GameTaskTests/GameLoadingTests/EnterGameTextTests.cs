using System.Globalization;
using BetterGenshinImpact.GameTask.GameLoading;
using BetterGenshinImpact.GameTask.Model.Area;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.GameLoadingTests;

/// <summary>
/// 非中文游戏语言下用 OCR 识别“点击进入”的测试（不点击、不初始化游戏运行环境）
/// </summary>
public sealed class EnterGameTextTests
{
    /// <summary>建立脱离截图树的 OCR 区域</summary>
    private static Region Text(int x, int y, int w, string text) => new(x, y, w, 30) { Text = text };

    /// <summary>葡萄牙语：服务器选择界面（中部）和开门界面（底部横条）的“Pressione para entrar”都能找到</summary>
    [Theory]
    [InlineData(826, 575)]
    [InlineData(826, 1005)]
    public void Find_PortugueseEnterText(int x, int y)
    {
        var pattern = EnterGameText.CreatePattern(new LocalizationFixture().CreateStringLocalizer<GameLoadingTrigger>(), new CultureInfo("pt-BR"));
        Region[] regions = [Text(780, 480, 350, "Iniciar jogo"), Text(x, y, 270, "Pressione para entrar"), Text(890, 910, 140, "America")];

        var found = EnterGameText.Find(regions, 1920, 1080, pattern!);

        Assert.NotNull(found);
        Assert.Equal("Pressione para entrar", found!.Text);
    }

    /// <summary>英语：“CLICK TO BEGIN”能找到</summary>
    [Fact]
    public void Find_EnglishEnterText()
    {
        var pattern = EnterGameText.CreatePattern(new LocalizationFixture().CreateStringLocalizer<GameLoadingTrigger>(), new CultureInfo("en"));
        Region[] regions = [Text(850, 1005, 220, "CLICK TO BEGIN")];

        Assert.NotNull(EnterGameText.Find(regions, 1920, 1080, pattern!));
    }

    /// <summary>不在屏幕下半部中间区域的同名文字（如左下角日志）不点击</summary>
    [Fact]
    public void Find_IgnoresTextOutsideCenterBottom()
    {
        var pattern = EnterGameText.CreatePattern(new LocalizationFixture().CreateStringLocalizer<GameLoadingTrigger>(), new CultureInfo("pt-BR"));
        Region[] regions = [Text(20, 900, 270, "Pressione para entrar"), Text(826, 200, 270, "Pressione para entrar")];

        Assert.Null(EnterGameText.Find(regions, 1920, 1080, pattern!));
    }

    /// <summary>中文游戏语言继续使用原有图片模板，不创建 OCR 规则</summary>
    [Fact]
    public void CreatePattern_ChineseUsesTemplateOnly()
    {
        Assert.Null(EnterGameText.CreatePattern(new LocalizationFixture().CreateStringLocalizer<GameLoadingTrigger>(), new CultureInfo("zh-Hans")));
    }
}
