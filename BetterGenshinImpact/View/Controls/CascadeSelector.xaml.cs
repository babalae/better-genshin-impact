using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

public partial class CascadeSelector : UserControl
{
    public CascadeSelector()
    {
        InitializeComponent();
    }

    public Dictionary<string, List<string>>? CascadeOptions
    {
        get { return (Dictionary<string, List<string>>?)GetValue(CascadeOptionsProperty); }
        set { SetValue(CascadeOptionsProperty, value); }
    }

    public static readonly DependencyProperty CascadeOptionsProperty =
        DependencyProperty.Register("CascadeOptions", typeof(Dictionary<string, List<string>>), typeof(CascadeSelector), 
            new PropertyMetadata(null, OnCascadeOptionsChanged));

    private static void OnCascadeOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CascadeSelector)d;
        control.UpdateFirstLevelOptions();
    }

    public List<string> FirstLevelOptions
    {
        get { return (List<string>)GetValue(FirstLevelOptionsProperty); }
        set { SetValue(FirstLevelOptionsProperty, value); }
    }

    public static readonly DependencyProperty FirstLevelOptionsProperty =
        DependencyProperty.Register("FirstLevelOptions", typeof(List<string>), typeof(CascadeSelector), new PropertyMetadata(null));

    public List<string> SecondLevelOptions
    {
        get { return (List<string>)GetValue(SecondLevelOptionsProperty); }
        set { SetValue(SecondLevelOptionsProperty, value); }
    }

    public static readonly DependencyProperty SecondLevelOptionsProperty =
        DependencyProperty.Register("SecondLevelOptions", typeof(List<string>), typeof(CascadeSelector), new PropertyMetadata(null));

    public string? SelectedValue
    {
        get { return (string?)GetValue(SelectedValueProperty); }
        set { SetValue(SelectedValueProperty, value); }
    }

    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register("SelectedValue", typeof(string), typeof(CascadeSelector), 
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedValueChanged));

    private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CascadeSelector)d;
        control.HandleSelectedValueChanged((string?)e.NewValue);
    }

    public string? DefaultValue
    {
        get { return (string?)GetValue(DefaultValueProperty); }
        set { SetValue(DefaultValueProperty, value); }
    }

    public static readonly DependencyProperty DefaultValueProperty =
        DependencyProperty.Register("DefaultValue", typeof(string), typeof(CascadeSelector), new PropertyMetadata(null));

    private void UpdateFirstLevelOptions()
    {
        if (CascadeOptions != null)
        {
            FirstLevelOptions = CascadeOptions.Keys.ToList();
        }
        else
        {
            FirstLevelOptions = new List<string>();
        }
    }

    private void HandleSelectedValueChanged(string? newValue)
    {
        if (string.IsNullOrEmpty(newValue) || CascadeOptions == null)
        {
            return;
        }

        foreach (var kvp in CascadeOptions)
        {
            if (kvp.Value.Contains(newValue))
            {
                FirstLevelListView.SelectedItem = kvp.Key;
                SecondLevelOptions = kvp.Value;
                SecondLevelListView.SelectedItem = newValue;
                break;
            }
        }
    }

    private void FirstLevelListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FirstLevelListView.SelectedItem is string selectedFirstLevel)
        {
            if (CascadeOptions != null && CascadeOptions.TryGetValue(selectedFirstLevel, out var secondLevelOptions))
            {
                SecondLevelOptions = secondLevelOptions;
                SecondLevelListView.SelectedItem = null;
            }
        }
    }

    private void SecondLevelListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SecondLevelListView.SelectedItem is string selectedSecondLevel)
        {
            SelectedValue = selectedSecondLevel;
            if (MainToggle.IsChecked == true)
            {
                MainToggle.IsChecked = false;
            }
        }
    }
}
