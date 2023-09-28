using System.Windows;
using System.Windows.Controls;

namespace Vision.Recognition.Controls.Adorners;

public class ResizeRotateChrome : Control
{
    static ResizeRotateChrome()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ResizeRotateChrome), new FrameworkPropertyMetadata(typeof(ResizeRotateChrome)));
    }
}
