using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls;

public partial class CascadeSelector : UserControl
{
    private const double HorizontalMargin = 40;
    private const double MinFirstLevelWidth = 100;
    private const double MaxFirstLevelWidth = 300;
    private const double MinSecondLevelWidth = 100;
    private const double MaxSecondLevelWidth = 300;
    private const double MinPopupWidth = 120;
    private const double MaxPopupWidth = 600;

    public CascadeSelector()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateFirstLevelOptions();
        UpdatePopupWidth();
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
        DependencyProperty.Register("SecondLevelOptions", typeof(List<string>), typeof(CascadeSelector), 
            new PropertyMetadata(null, OnSecondLevelOptionsChanged));

    private static void OnSecondLevelOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CascadeSelector)d;
        control.Dispatcher.BeginInvoke(() =>
        {
            control.AdjustSecondLevelListWidth();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

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

    private double MeasureTextWidth(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            fontSize,
            Brushes.Black,
            new NumberSubstitution(),
            TextFormattingMode.Display,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formattedText.WidthIncludingTrailingWhitespace;
    }

    private void AdjustFirstLevelListWidth()
    {
        if (FirstLevelOptions == null || FirstLevelOptions.Count == 0)
        {
            return;
        }

        double maxWidth = MinFirstLevelWidth;
        foreach (var option in FirstLevelOptions)
        {
            double textWidth = MeasureTextWidth(option, FontSize) + HorizontalMargin;
            if (textWidth > maxWidth)
            {
                maxWidth = textWidth;
            }
        }

        if (maxWidth > MaxFirstLevelWidth)
        {
            maxWidth = MaxFirstLevelWidth;
        }

        var grid = PopupBorder?.Child as Grid;
        var firstColumn = grid?.ColumnDefinitions[0];
        if (firstColumn != null)
        {
            firstColumn.Width = new GridLength(maxWidth);
        }
    }

    private void AdjustSecondLevelListWidth()
    {
        if (SecondLevelOptions == null || SecondLevelOptions.Count == 0)
        {
            UpdatePopupWidth();
            return;
        }

        double maxWidth = MinSecondLevelWidth;
        foreach (var option in SecondLevelOptions)
        {
            double textWidth = MeasureTextWidth(option, FontSize) + HorizontalMargin;
            if (textWidth > maxWidth)
            {
                maxWidth = textWidth;
            }
        }

        if (maxWidth > MaxSecondLevelWidth)
        {
            maxWidth = MaxSecondLevelWidth;
        }

        var grid = PopupBorder?.Child as Grid;
        var thirdColumn = grid?.ColumnDefinitions[2];
        if (thirdColumn != null)
        {
            thirdColumn.Width = new GridLength(maxWidth);
        }

        Dispatcher.BeginInvoke(() => UpdatePopupWidth(), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void UpdatePopupWidth()
    {
        var grid = PopupBorder?.Child as Grid;
        if (grid == null || grid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        double totalWidth = 0;

        var firstColumn = grid.ColumnDefinitions[0];
        var secondColumn = grid.ColumnDefinitions[1];
        var thirdColumn = grid.ColumnDefinitions[2];

        if (firstColumn.Width.IsAuto)
        {
            firstColumn.Width = new GridLength(MinFirstLevelWidth);
        }
        totalWidth += firstColumn.Width.Value;

        totalWidth += secondColumn.Width.Value;
        totalWidth += thirdColumn.Width.Value;

        totalWidth += 8;

        if (totalWidth < MinPopupWidth)
        {
            totalWidth = MinPopupWidth;
        }
        if (totalWidth > MaxPopupWidth)
        {
            totalWidth = MaxPopupWidth;
        }

        PopupBorder.Width = totalWidth;
    }

    private void UpdateFirstLevelOptions()
    {
        if (CascadeOptions != null)
        {
            FirstLevelOptions = CascadeOptions.Keys.ToList();
            Dispatcher.BeginInvoke(() =>
            {
                AdjustFirstLevelListWidth();
                UpdatePopupWidth();
            }, System.Windows.Threading.DispatcherPriority.Render);
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
