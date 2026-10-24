using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Controls.CombatStrategy;

/// <summary>列表上输入字符时转到搜索框，兼容输入法提交的文本。</summary>
public sealed class TypeToSearchBehavior : Behavior<ListBox>
{
    /// <summary>目标搜索框依赖属性。</summary>
    public static readonly DependencyProperty SearchBoxProperty = DependencyProperty.Register(nameof(SearchBox), typeof(TextBox), typeof(TypeToSearchBehavior));
    /// <summary>当前浏览器搜索框。</summary>
    public TextBox? SearchBox { get => (TextBox?)GetValue(SearchBoxProperty); set => SetValue(SearchBoxProperty, value); }
    /// <summary>监听文本输入。</summary>
    protected override void OnAttached() => AssociatedObject.PreviewTextInput += OnTextInput;
    /// <summary>释放文本输入监听。</summary>
    protected override void OnDetaching() => AssociatedObject.PreviewTextInput -= OnTextInput;
    /// <summary>保持已输入文本并移动光标到末尾。</summary>
    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SearchBox == null || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        SearchBox.Focus();
        SearchBox.SetCurrentValue(TextBox.TextProperty, SearchBox.Text + e.Text);
        SearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }
}
