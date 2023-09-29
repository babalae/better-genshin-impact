using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls.Adorners;

public class SizeAdorner : Adorner
{
    private readonly SizeChrome chrome;
    private readonly VisualCollection visuals;
    private readonly ContentControl designerItem;

    protected override int VisualChildrenCount
    {
        get
        {
            return visuals.Count;
        }
    }

    public SizeAdorner(ContentControl designerItem)
        : base(designerItem)
    {
        SnapsToDevicePixels = true;
        this.designerItem = designerItem;
        chrome = new SizeChrome
        {
            DataContext = designerItem
        };
        visuals = new VisualCollection(this)
        {
            chrome
        };
    }

    protected override Visual GetVisualChild(int index)
    {
        return visuals[index];
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        chrome.Arrange(new Rect(default, arrangeBounds));
        return arrangeBounds;
    }
}
