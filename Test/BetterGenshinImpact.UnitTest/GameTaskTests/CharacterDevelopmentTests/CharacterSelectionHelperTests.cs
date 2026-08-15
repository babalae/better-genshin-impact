using BetterGenshinImpact.GameTask.CharacterDevelopment;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.CharacterDevelopmentTests;

public class CharacterSelectionHelperTests
{
    [Fact]
    public void OrderCandidateIndicesForVerification_PrioritizesOnlyConfidentTargetPredictions()
    {
        var target = CharacterSelectionHelper.CreateTarget("梦见月瑞希");
        AvatarGridIconCandidate[] candidates =
        [
            new("爱诺", "水", 0.110),
            new("梦见月瑞希", "风", 0.320),
            new("可莉", "火", double.NaN),
            new("梦见月瑞希", "风", 0.810),
            new("烟绯", "火", 0.112)
        ];

        var result = CharacterSelectionHelper.OrderCandidateIndicesForVerification(target, candidates);

        Assert.Equal([3, 0, 1, 2, 4], result);
    }

    [Fact]
    public void OrderCandidateIndicesForVerification_KeepsVisualOrderWhenNoPredictionIsConfident()
    {
        var target = CharacterSelectionHelper.CreateTarget("梦见月瑞希");
        AvatarGridIconCandidate[] candidates =
        [
            new("梦见月瑞希", "风", double.NaN),
            new("梦见月瑞希", "风", 0.200),
            new("爱诺", "水", 0.900)
        ];

        var result = CharacterSelectionHelper.OrderCandidateIndicesForVerification(target, candidates);

        Assert.Equal([0, 1, 2], result);
    }

    [Theory]
    [InlineData("梦见月瑞希", true)]
    [InlineData("梦见月 瑞希", true)]
    [InlineData("\r\n梦见月瑞希\t", true)]
    [InlineData("爱诺", false)]
    public void MatchesDisplayText_IgnoresOcrWhitespace(string text, bool expected)
    {
        var target = CharacterSelectionHelper.CreateTarget("梦见月瑞希");

        Assert.Equal(expected, target.MatchesDisplayText(text));
    }

    [Fact]
    public void MergeCharacterCards_RestoresCardsHiddenByOppositeSelectionStates()
    {
        CharacterCardRect[] first =
        [
            Card(133, 32), Card(261, 32), Card(391, 32), Card(519, 32),
            Card(4, 189), Card(133, 189), Card(261, 189), Card(391, 189)
        ];
        CharacterCardRect[] second =
        [
            Card(4, 32), Card(261, 32), Card(391, 32), Card(519, 32),
            Card(4, 189), Card(133, 189), Card(261, 189), Card(391, 189)
        ];

        var result = CharacterSelectionHelper.MergeCharacterCards(first, second, 1);

        Assert.Equal(9, result.Count);
        Assert.Equal(
            [(4, 32), (133, 32), (261, 32), (391, 32), (519, 32), (4, 189), (133, 189), (261, 189), (391, 189)],
            result.Select(card => (card.CardRect.X, card.CardRect.Y)));
    }

    [Fact]
    public void MergeCharacterCards_DeduplicatesSmallCoordinateJitter()
    {
        CharacterCardRect[] first = [Card(4, 32), Card(133, 32)];
        CharacterCardRect[] second = [Card(7, 35), Card(136, 29)];

        var result = CharacterSelectionHelper.MergeCharacterCards(first, second, 1);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MergeCharacterCards_KeepsVisualOrder()
    {
        CharacterCardRect[] first = [Card(261, 189), Card(391, 32)];
        CharacterCardRect[] second = [Card(4, 189), Card(133, 32)];

        var result = CharacterSelectionHelper.MergeCharacterCards(first, second, 1);

        Assert.Equal(
            [(133, 32), (391, 32), (4, 189), (261, 189)],
            result.Select(card => (card.CardRect.X, card.CardRect.Y)));
    }

    private static CharacterCardRect Card(int x, int y) =>
        new(new Rect(x, y, 115, 140), new Rect(x, y, 115, 115), new Rect(x, y, 48, 48));
}
