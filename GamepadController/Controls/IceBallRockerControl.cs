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
    /// 街机球形摇杆
    /// </summary>
    [TemplatePart(Name = "Part_Path", Type = typeof(FrameworkElement))]
    public class IceBallRockerControl : RadioButton
    {
        static IceBallRockerControl() => DefaultStyleKeyProperty.OverrideMetadata(typeof(IceBallRockerControl), new FrameworkPropertyMetadata(typeof(IceBallRockerControl)));
    }
}
