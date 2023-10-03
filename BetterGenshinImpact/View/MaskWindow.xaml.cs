using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Vanara.PInvoke;
using FontFamily = System.Windows.Media.FontFamily;

namespace BetterGenshinImpact.View
{
    /// <summary>
    /// MaskWindow.xaml 的交互逻辑
    /// 一个用于覆盖在游戏窗口上的窗口，用于显示识别结果、显示日志、设置区域位置等
    /// 请使用 Instance 方法获取单例
    /// </summary>
    public partial class MaskWindow : Window
    {
        private static MaskWindow? _maskWindow;

        private static readonly Typeface MyTypeface = new FontFamily("微软雅黑").GetTypefaces().First();

        public static MaskWindow Instance(IntPtr? hWnd = null)
        {
            _maskWindow ??= new MaskWindow();
            if (hWnd != null && hWnd!=IntPtr.Zero)
            {
                User32.GetWindowRect(hWnd.Value, out var rect);
                double scale = DpiHelper.ScaleY;
                _maskWindow.Left = (int)Math.Ceiling(rect.X * 1d / scale);
                _maskWindow.Top = (int)Math.Ceiling(rect.Y * 1d / scale);
                _maskWindow.Width = (int)Math.Ceiling(rect.Width * 1d / scale);
                _maskWindow.Height = (int)Math.Ceiling(rect.Height * 1d / scale);
                Debug.WriteLine($"原神窗口大小：{rect.Width} x {rect.Height}");
                Debug.WriteLine($"原神窗口大小(计算DPI缩放后)：{_maskWindow.Width} x {_maskWindow.Height}");
            }

            return _maskWindow;
        }

        private MaskWindow()
        {
            InitializeComponent();
            LogTextBox.TextChanged += LogTextBoxTextChanged;
            //AddAreaSettingsControl("测试识别窗口");
        }

        private void LogTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            var textRange = new TextRange(LogTextBox.Document.ContentStart, LogTextBox.Document.ContentEnd);
            if (textRange.Text.Length > 10000)
            {
                LogTextBox.Document.Blocks.Clear();
            }

            LogTextBox.ScrollToEnd();
        }

        public void Refresh()
        {
            Dispatcher.Invoke(InvalidateVisual);
        }

        public void Invoke(Action action)
        {
            Dispatcher.Invoke(action);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            //Logger?.LogInformation("绘制识别结果");
            try
            {
                foreach (var kv in VisionContext.Instance().DrawContent.RectList)
                {
                    var drawable = kv.Value;
                    if (!drawable.IsEmpty)
                    {
                        drawingContext.DrawRectangle(Brushes.Transparent,
                            new Pen(new SolidColorBrush(drawable.Pen.Color.ToWindowsColor()), drawable.Pen.Width),
                            drawable.Rect);
                    }
                }

                foreach (var kv in VisionContext.Instance().DrawContent.TextList)
                {
                    var drawable = kv.Value;
                    if (!drawable.IsEmpty)
                    {
                        drawingContext.DrawText(new FormattedText(drawable.Text,
                            CultureInfo.GetCultureInfo("zh-cn"),
                            FlowDirection.LeftToRight,
                            MyTypeface,
                            36, Brushes.Black, 1), drawable.Point);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }

            base.OnRender(drawingContext);
        }

        public RichTextBox LogBox => LogTextBox;

        public Canvas Panel => WholeCanvas;

        public void AddAreaSettingsControl(string name)
        {
            var control = new ContentControl();
            control.Width = 100;
            control.Height = 100;
            control.Style = (Style)FindResource("DraggableResizableItemStyle");

            var grid = new Grid();
            grid.Children.Add(new Rectangle
            {
                Fill = Brushes.White,
                Opacity = 0.2,
                IsHitTestVisible = false
            });
            grid.Children.Add(new TextBlock
            {
                Text = name,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            control.Content = grid;


            Canvas.SetTop(control, 200);
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

        public ConcurrentDictionary<string, Button> ButtonList { get; set; } = new();

        public void AddButton(string name, Rect position, Action action)
        {
            if (ButtonList.ContainsKey(name))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                var b = new Button
                {
                    Name = name,
                    Content = name,
                    Width = position.Width / TaskContext.Instance().DpiScale,
                    Height = position.Height / TaskContext.Instance().DpiScale
                };

                b.Click += (e, a) => { action.Invoke(); };

                Canvas.SetLeft(b, position.X / TaskContext.Instance().DpiScale);
                Canvas.SetTop(b, position.Y / TaskContext.Instance().DpiScale);
                WholeCanvas.Children.Add(b);
                ButtonList[name] = b;
            });
        }
    }
}