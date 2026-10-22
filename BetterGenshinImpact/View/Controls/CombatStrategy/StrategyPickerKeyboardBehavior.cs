using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BetterGenshinImpact.ViewModel.Controls.CombatStrategy;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Controls.CombatStrategy;

/// <summary>两栏策略菜单的方向键、返回和确认交互，焦点逻辑不进入 ViewModel。</summary>
public sealed class StrategyPickerKeyboardBehavior : Behavior<FrameworkElement>
{
    /// <summary>监听面板键盘事件。</summary>
    protected override void OnAttached() => AssociatedObject.PreviewKeyDown += OnKeyDown;
    /// <summary>卸载时释放事件。</summary>
    protected override void OnDetaching() => AssociatedObject.PreviewKeyDown -= OnKeyDown;
    /// <summary>按菜单当前位置导航；文本输入框保留自己的方向键和退格。</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (AssociatedObject.DataContext is not CombatStrategyPickerViewModel vm) return;
        var categories = PopupScrollIsolationBehavior.FindNamed<ListBox>(AssociatedObject, "CategoryList");
        var inCategories = categories?.IsKeyboardFocusWithin == true;
        // 排序下拉框先处理自身的 Esc/方向键/确认，避免把 Enter 误当作选用策略。
        if (HasComboBoxAncestor(Keyboard.FocusedElement as DependencyObject)) return;
        if (e.Key == Key.Escape) { vm.EscapeCommand.Execute(null); e.Handled = true; return; }
        if (inCategories && e.Key == Key.Right)
        {
            FrameworkElement? target = PopupScrollIsolationBehavior.FindNamed<TextBox>(AssociatedObject, "SearchInput")
                ?? (FrameworkElement?)PopupScrollIsolationBehavior.FindNamed<ListBox>(AssociatedObject, "ScopeList");
            if (target != null) Keyboard.Focus(target);
            else PopupScrollIsolationBehavior.FindNamed<ContentControl>(AssociatedObject, "CategoryContent")?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            e.Handled = true;
            return;
        }
        if (!inCategories && (e.Key is Key.Down or Key.Enter) && Keyboard.FocusedElement is TextBox)
        {
            var list = PopupScrollIsolationBehavior.FindNamed<ListBox>(AssociatedObject, "EntryList");
            if (list != null)
            {
                if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
                FocusSelectedItem(list);
                e.Handled = true;
                return;
            }
        }
        if (inCategories || Keyboard.FocusedElement is TextBoxBase or ButtonBase or ComboBox) return;
        if (e.Key is Key.Left or Key.Back)
        {
            if (vm.SelectedCategory is StrategyBrowserViewModel browser && browser.FolderRelativePath.Length > 0 && !browser.IsSearching)
            {
                browser.GoUp();
                FocusSelectedItem(PopupScrollIsolationBehavior.FindNamed<ListBox>(AssociatedObject, "EntryList"));
            }
            else categories?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Right)
        {
            if (vm.SelectedCategory is StrategyBrowserViewModel browser && browser.SelectedEntry is { } entry
                && (e.Key == Key.Enter || entry.Node.IsFolder))
            {
                browser.Enter(entry);
                if (entry.Node.IsFolder) FocusSelectedItem(PopupScrollIsolationBehavior.FindNamed<ListBox>(AssociatedObject, "EntryList"));
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && vm.SelectedCategory is AutoMatchCategoryViewModel auto)
            {
                auto.SelectCommand.Execute(auto.SelectedScope);
                e.Handled = true;
            }
        }
    }
    /// <summary>识别排序框及其下拉项的内部焦点，保留控件原生键盘行为。</summary>
    private static bool HasComboBoxAncestor(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is ComboBox or ComboBoxItem) return true;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    /// <summary>目录重建后把焦点交给实际行容器，方向键可继续导航。</summary>
    private static void FocusSelectedItem(ListBox? list)
    {
        if (list == null) return;
        if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
        list.UpdateLayout();
        if (list.SelectedItem is { } selected && list.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item) item.Focus();
        else list.Focus();
    }
}
