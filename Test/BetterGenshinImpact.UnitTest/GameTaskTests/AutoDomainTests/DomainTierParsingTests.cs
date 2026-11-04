using BetterGenshinImpact.GameTask.AutoDomain;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomainTests
{
    public class DomainTierParsingTests
    {
        [Theory]
        [InlineData("祝圣秘境：荒废砌造坞 I", "I")]
        [InlineData("祝圣秘境：荒废砌造坞 II", "II")]
        [InlineData("祝圣秘境：荒废砌造坞 III", "III")]
        [InlineData("祝圣秘境：荒废砌造坞 IV", "IV")]
        [InlineData("祝圣秘境：荒废砌造坞 VI", "VI")]
        [InlineData("祝圣秘境：荒废砌造坞 Ⅲ", "III")]
        [InlineData("祝圣秘境：荒废砌造坞Ⅳ", "IV")]
        [InlineData("祝圣秘境：荒废砌造坞 lll", "III")]
        [InlineData("祝圣秘境：荒废砌造坞 I|", "II")]
        [InlineData("  祝圣秘境：荒废砌造坞 iv  ", "IV")]
        public void ParseDomainTier_ReadsTrailingTier(string text, string expected)
        {
            Assert.Equal(expected, AutoDomainTask.ParseDomainTier(text));
        }

        // Row text as OCR really read it in the game, where I and III come back wrong or empty.
        private static readonly string[] RealOcrRows =
            ["祝圣秘境：深火的构筑】", "祝圣秘境：深火的构筑", "祝圣秘境：深火的构筑1", "祝圣秘境：深火的构筑IV"];

        [Theory]
        [InlineData("I", 0)]
        [InlineData("II", 1)]
        [InlineData("III", 2)]
        [InlineData("IV", 3)]
        [InlineData(" ii ", 1)]
        [InlineData("2", -1)]
        [InlineData("V", -1)]
        [InlineData("", -1)]
        public void FindDomainTierRow_UsesPositionWhenOcrMisreadsTiers(string target, int expected)
        {
            Assert.Equal(expected, AutoDomainTask.FindDomainTierRow(RealOcrRows, target));
        }

        [Fact]
        public void FindDomainTierRow_RefusesListThatDoesNotStartAtTierOne()
        {
            string[] scrolled = ["祝圣秘境 II", "祝圣秘境 III", "祝圣秘境 IV"];
            Assert.Equal(-1, AutoDomainTask.FindDomainTierRow(scrolled, "II"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("祝圣秘境：荒废砌造坞")]
        [InlineData("单人挑战")]
        [InlineData("祝圣秘境 IIII")]
        [InlineData("祝圣秘境 X")]
        public void ParseDomainTier_RejectsTextWithoutValidTier(string? text)
        {
            Assert.Null(AutoDomainTask.ParseDomainTier(text));
        }
    }
}
