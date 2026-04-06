using System;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls;

public partial class DomainSelector : UserControl
{
    public DomainSelector()
    {
        InitializeComponent();
        Countries = MapLazyAssets.Instance.CountryToDomains.Keys.Reverse().ToList();
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// 控件卸载时清理事件处理器，防止内存泄漏
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        RemoveWindowMouseWheelHandler();
    }

    public List<string> Countries
    {
        get { return (List<string>)GetValue(CountriesProperty); }
        set { SetValue(CountriesProperty, value); }
    }

    public static readonly DependencyProperty CountriesProperty =
        DependencyProperty.Register("Countries", typeof(List<string>), typeof(DomainSelector), new PropertyMetadata(null));

    public string SelectedCountry
    {
        get { return (string)GetValue(SelectedCountryProperty); }
        set { SetValue(SelectedCountryProperty, value); }
    }

    public static readonly DependencyProperty SelectedCountryProperty =
        DependencyProperty.Register("SelectedCountry", typeof(string), typeof(DomainSelector), new PropertyMetadata(null, OnSelectedCountryChanged));

    private static void OnSelectedCountryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DomainSelector)d;
        var country = (string)e.NewValue;
        if (string.IsNullOrEmpty(country))
        {
            control.FilteredDomains = new List<System.Tuple<string, GiTpPosition>>();
        }
        else
        {
            if (MapLazyAssets.Instance.CountryToDomains.TryGetValue(country, out var domains))
            {
                // Reverse the list for display
                control.FilteredDomains = domains.Select(i => new System.Tuple<string, GiTpPosition>(i.Name! + " | " + string.Join(" ", i.Rewards), i)).Reverse().ToList();
            }
            else
            {
                control.FilteredDomains = new List<System.Tuple<string, GiTpPosition>>();
            }
        }
    }

    public List<System.Tuple<string, GiTpPosition>> FilteredDomains
    {
        get { return (List<System.Tuple<string, GiTpPosition>>)GetValue(FilteredDomainsProperty); }
        set { SetValue(FilteredDomainsProperty, value); }
    }

    public static readonly DependencyProperty FilteredDomainsProperty =
        DependencyProperty.Register("FilteredDomains", typeof(List<System.Tuple<string, GiTpPosition>>), typeof(DomainSelector), new PropertyMetadata(null));

    public string SelectedDomain
    {
        get { return (string)GetValue(SelectedDomainProperty); }
        set { SetValue(SelectedDomainProperty, value); }
    }

    public static readonly DependencyProperty SelectedDomainProperty =
        DependencyProperty.Register("SelectedDomain", typeof(string), typeof(DomainSelector), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDomainChanged));

    private static void OnSelectedDomainChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DomainSelector)d;
        var domain = (string)e.NewValue;

        if (string.IsNullOrEmpty(domain)) return;

        // Verify if domain matches current country, if not, update country
        var country = MapLazyAssets.Instance.GetCountryByDomain(domain);
        if (country != null && country != control.SelectedCountry)
        {
            control.SelectedCountry = country;
        }
    }

    private void DomainListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainToggle.IsChecked == true)
        {
            MainToggle.IsChecked = false;
        }
    }

    /// <summary>
    /// Popup 打开时添加全局滚轮事件拦截
    /// </summary>
    private void MainPopup_Opened(object sender, EventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.PreviewMouseWheel -= Window_PreviewMouseWheel;
            window.PreviewMouseWheel += Window_PreviewMouseWheel;
        }
    }

    /// <summary>
    /// Popup 关闭时移除全局滚轮事件拦截
    /// </summary>
    private void MainPopup_Closed(object sender, EventArgs e)
    {
        RemoveWindowMouseWheelHandler();
    }

    /// <summary>
    /// 移除窗口级滚轮事件处理器
    /// </summary>
    private void RemoveWindowMouseWheelHandler()
    {
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.PreviewMouseWheel -= Window_PreviewMouseWheel;
        }
    }

    /// <summary>
    /// 全局滚轮事件处理，当 Popup 打开时拦截所有滚轮事件
    /// </summary>
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (MainPopup.IsOpen)
        {
            e.Handled = true;

            var scrollViewer1 = FindScrollViewer(CountriesListView);
            var scrollViewer2 = FindScrollViewer(DomainsListView);

            if (scrollViewer1 != null && scrollViewer1.IsMouseOver)
            {
                scrollViewer1.ScrollToVerticalOffset(scrollViewer1.VerticalOffset - e.Delta / 2.0);
                return;
            }

            if (scrollViewer2 != null && scrollViewer2.IsMouseOver)
            {
                scrollViewer2.ScrollToVerticalOffset(scrollViewer2.VerticalOffset - e.Delta / 2.0);
                return;
            }
        }
    }

    /// <summary>
    /// 处理 Popup 内的鼠标滚轮事件，防止滚动穿透到外部页面
    /// </summary>
    private void PopupBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        var scrollViewer1 = FindScrollViewer(CountriesListView);
        var scrollViewer2 = FindScrollViewer(DomainsListView);

        if (scrollViewer1 != null && scrollViewer1.IsMouseOver)
        {
            scrollViewer1.ScrollToVerticalOffset(scrollViewer1.VerticalOffset - e.Delta / 2.0);
            return;
        }

        if (scrollViewer2 != null && scrollViewer2.IsMouseOver)
        {
            scrollViewer2.ScrollToVerticalOffset(scrollViewer2.VerticalOffset - e.Delta / 2.0);
            return;
        }
    }

    /// <summary>
    /// 在视觉树中查找 ScrollViewer
    /// </summary>
    private ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var result = FindScrollViewer(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
