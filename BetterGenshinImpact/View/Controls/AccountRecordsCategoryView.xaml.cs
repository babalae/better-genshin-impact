using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls;

/// <summary>库存和需求共用的类型单元格，固定图标，文字按剩余空间省略。</summary>
public partial class AccountRecordsCategoryView : UserControl
{
    /// <summary>背包页图标属性。</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(AccountRecordsCategoryView));
    /// <summary>材料类型文字属性。</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(AccountRecordsCategoryView), new PropertyMetadata(""));
    /// <summary>完整分类提示属性。</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(AccountRecordsCategoryView), new PropertyMetadata(""));
    /// <summary>即使没有资源仍保留图标位置。</summary>
    public ImageSource? Icon { get => (ImageSource?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    /// <summary>完整材料类型，窄列不改变原文。</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    /// <summary>图标和文字共用完整悬停提示。</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    /// <summary>初始化独立可解析的本地资源。</summary>
    public AccountRecordsCategoryView() => InitializeComponent();
}
