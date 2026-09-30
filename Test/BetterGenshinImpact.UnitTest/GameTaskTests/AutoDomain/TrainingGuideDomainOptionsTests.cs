using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideDomainOptionsTests
{
    [Fact]
    public void Filter_PreservesSourceAndSelectableNodeIdentity()
    {
        var guide = new CascadingItem(AutoDomainTask.TrainingGuideOption) { Tag = AutoDomainTask.TrainingGuideOption };
        var other = new CascadingItem("其他自动选项") { Tag = "other" };
        var automatic = new CascadingItem("自动选择", new ICascadingItem[] { guide, other }) { Tag = "group" };
        var domain = new CascadingItem("秘境 | 材料") { Tag = "秘境" };
        var country = new CascadingItem("国家", new ICascadingItem[] { domain });
        var source = new ICascadingItem[] { automatic, country };

        var filtered = TrainingGuideDomainOptions.Filter(source);

        Assert.Equal(2, automatic.Children!.Count());
        Assert.Same(guide, automatic.Children!.First());
        Assert.NotSame(automatic, filtered[0]);
        Assert.Equal(automatic.Label, filtered[0].Label);
        Assert.Equal(automatic.Tag, filtered[0].Tag);
        Assert.Same(other, Assert.Single(filtered[0].Children!));
        Assert.Same(country, filtered[1]);
        Assert.Same(domain, Assert.Single(filtered[1].Children!));
    }

    [Fact]
    public void Filter_RemovesOnlyParentEmptiedByGuideRemoval()
    {
        var guide = new CascadingItem(AutoDomainTask.TrainingGuideOption) { Tag = AutoDomainTask.TrainingGuideOption };
        var automatic = new CascadingItem("自动选择", new ICascadingItem[] { guide });
        var emptyCountry = new CascadingItem("空国家", Array.Empty<ICascadingItem>());
        var country = new CascadingItem("国家", new ICascadingItem[] { guide });

        var filtered = TrainingGuideDomainOptions.Filter(new ICascadingItem[] { automatic, emptyCountry, country });

        Assert.Equal(new ICascadingItem[] { emptyCountry, country }, filtered);
        Assert.Same(guide, Assert.Single(automatic.Children!));
    }
}
