using System.Globalization;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.UnitTest.GameTaskTests
{
    /// <summary>
    /// 这些用例读取任务<b>实际消费的 resx 值</b>，而不是在测试里另写一份匹配模式：
    /// 后者在资源被改坏之后依然会通过，而运行时 OCR 匹配已经失效。
    /// </summary>
    [Collection("Init Collection")]
    public class OcrResourceTests
    {
        private readonly LocalizationFixture localization;

        public OcrResourceTests(LocalizationFixture localization)
        {
            this.localization = localization;
        }

        /// <summary>
        /// 英文界面里「地脉衍出」是单数的 "Ley Line Outcrop"，复数模式一条都匹配不上。
        /// 下面的界面文本取自游戏 TextMap，不是手写的。
        /// </summary>
        [Theory]
        [InlineData("en", "Ley Line Outcrop: Blossom of Revelation")]
        [InlineData("en", "Ley Line Outcrop: Blossom of Wealth")]
        [InlineData("en", "Ley Line Outcrop: Blossom of Revelation (Mondstadt)")]
        [InlineData("fr", "Fleur de la révélation des lignes énergétiques")]
        [InlineData("fr", "Fleur de la fortune des lignes énergétiques (Fontaine)")]
        public void LeyLineOutcropPattern_MatchesRealGameText(string culture, string uiText)
        {
            var pattern = this.localization.CreateStringLocalizer<AutoLeyLineOutcropTask>()
                .WithCultureGet(new CultureInfo(culture), "(地脉|衍出)");

            Assert.Matches(pattern, uiText);
        }

        [Theory]
        [InlineData("en", "Ley Line Outcrop: Blossom of Revelation")]
        [InlineData("fr", "Fleur de la fortune des lignes énergétiques")]
        public void LeyLineTouchPattern_MatchesRealGameText(string culture, string uiText)
        {
            var pattern = this.localization.CreateStringLocalizer<AutoLeyLineOutcropTask>()
                .WithCultureGet(new CultureInfo(culture), "(接触|地脉|之花)");

            Assert.Matches(pattern, uiText);
        }

        /// <summary>
        /// 该值会传给默认区分大小写的 <see cref="Regex.IsMatch(string, string)"/>，
        /// 所以模式不能依赖首字母的大小写。
        /// </summary>
        [Theory]
        [InlineData("en", "Insufficient Quantity")]
        [InlineData("en", "insufficient quantity")]
        [InlineData("fr", "Quantité insuffisante")]
        public void InsufficientQuantityPattern_DoesNotDependOnLetterCase(string culture, string uiText)
        {
            var pattern = this.localization.CreateStringLocalizer<AutoStygianOnslaughtTask>()
                .WithCultureGet(new CultureInfo(culture), "数量不足");

            Assert.Matches(pattern, uiText);
        }
    }
}
