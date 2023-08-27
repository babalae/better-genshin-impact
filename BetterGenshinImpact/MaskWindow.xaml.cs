using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BetterGenshinImpact
{
    /// <summary>
    /// MaskWindow.xaml 的交互逻辑
    /// 一个用于覆盖在游戏窗口上的窗口，用于显示识别结果、显示日志、设置区域位置等
    /// </summary>
    public partial class MaskWindow : Window
    {
        public MaskWindow()
        {
            InitializeComponent();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawRectangle(Brushes.Transparent, new Pen(Brushes.Red, 2), new Rect(20, 20, 250, 250));

            base.OnRender(drawingContext);
        }
    }
}
