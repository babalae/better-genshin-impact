using System.Windows.Documents;
using BetterGenshinImpact.View.Behavior;

namespace BetterGenshinImpact.UnitTest.CoreTests;

public class AnimatedNavigationSelectionIndicatorBehaviorTests
{
    [Fact]
    public void FindVisualAncestorSupportsRunContentElements()
    {
        var span = new Span();
        var run = new Run("导航文字");
        span.Inlines.Add(run);

        var ancestor = AnimatedNavigationSelectionIndicatorBehavior
            .FindVisualAncestor<Span>(run);

        Assert.Same(span, ancestor);
    }
}
