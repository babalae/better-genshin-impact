using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace BetterGenshinImpact.View.Controls.CodeEditor;

public class CodeBox : TextEditor
{
    public string Code
    {
        get => Text;
        set => Text = value;
    }

    public static readonly DependencyProperty CodeProperty =
        DependencyProperty.Register(nameof(Code), typeof(string), typeof(CodeBox), new PropertyMetadata(string.Empty, OnTextChange));

    private static void OnTextChange(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEditor editor)
        {
            editor.Text = (e.NewValue as string)!;
        }
    }

    public bool LineWrap
    {
        get => WordWrap;
        set
        {
            if (value)
            {
                WordWrap = true;
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            else
            {
                WordWrap = false;
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }
    }

    public CodeBox()
    {
        ShowLineNumbers = true;
        TextArea.SelectionBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x4F, 0x78));
        TextArea.SelectionBorder = new(Brushes.Transparent, 0d);
        TextArea.SelectionCornerRadius = 2d;
        TextArea.SelectionForeground = null!;
    }
}
