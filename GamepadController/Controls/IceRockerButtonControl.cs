using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GamepadController.Controls
{
    /// <summary>
    /// 街机摇杆按钮
    /// </summary>
    [TemplatePart(Name = "Part_Path", Type = typeof(FrameworkElement))]
    public class IceRockerButtonControl : CheckBox
    {
        static IceRockerButtonControl() => DefaultStyleKeyProperty.OverrideMetadata(typeof(IceRockerButtonControl), new FrameworkPropertyMetadata(typeof(IceRockerButtonControl)));
    }
}
