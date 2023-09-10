using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Vision.Recognition;
using Vision.Recognition.Helper.OpenCv;

namespace Vision.Recognition
{
    /// <summary>
    /// MaskWindow.xaml 的交互逻辑
    /// 一个用于覆盖在游戏窗口上的窗口，用于显示识别结果、显示日志、设置区域位置等
    /// 请使用 Instance 方法获取单例
    /// </summary>
    public partial class MaskWindow : Window
    {
        private static MaskWindow? _maskWindow;

        public ILogger<MaskWindow>? Logger { get; set; }

        private static readonly Typeface MyTypeface = new FontFamily("微软雅黑").GetTypefaces().First();

        public static MaskWindow Instance(ILogger<MaskWindow>? logger = null)
        {
            _maskWindow ??= new MaskWindow();
            _maskWindow.Logger ??= logger;
            VisionContext.Instance().Log ??= logger;
            return _maskWindow;
        }

        private MaskWindow()
        {
            InitializeComponent();
            AddAreaSettingsControl();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Logger?.LogInformation("OnRender...");

            foreach (var rect in VisionContext.Instance().DrawContentCache.RectList)
            {
                drawingContext.DrawRectangle(Brushes.Transparent, new Pen(Brushes.Red, 2), rect);
            }
            foreach (var obj in VisionContext.Instance().DrawContentCache.TextList)
            {
                drawingContext.DrawText(new FormattedText(obj.Item2,
                    CultureInfo.GetCultureInfo("zh-cn"),
                    FlowDirection.LeftToRight,
                    MyTypeface,
                    36, Brushes.Black, 1), obj.Item1);
            }

            base.OnRender(drawingContext);
        }

        public RichTextBox LogBox => LogTextBox;

        public Canvas Panel => WholeCanvas;

        public void AddAreaSettingsControl()
        {
            Logger?.LogInformation("添加设置控件");
            var control = new ContentControl();
            control.Width = 100;
            control.Height = 100;
            control.Style = (Style)FindResource("DraggableResizableItemStyle");
            var rc = new Rectangle
            {
                Fill = Brushes.Red,
                IsHitTestVisible = false
            };
            control.Content = rc;


            Canvas.SetTop(control, 100);
            Canvas.SetLeft(control, 20);
            WholeCanvas.Children.Add(control);
        }

        private void OnClick(object sender, RoutedEventArgs args)
        {
            CheckBox selectionCheckBox = sender as CheckBox;
            if (selectionCheckBox != null && selectionCheckBox.IsChecked == true)
            {
                foreach (Control child in WholeCanvas.Children)
                {
                    Selector.SetIsSelected(child, true);
                }
            }
            else
            {
                foreach (Control child in WholeCanvas.Children)
                {
                    Selector.SetIsSelected(child, false);
                }
            }
        }
    }
}