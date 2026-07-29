using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace BetterGenshinImpact.View.Controls;

public enum CronScheduleMode
{
    Daily,
    Weekly
}

public partial class CronSchedulePicker : UserControl
{
    private static readonly string[] OrderedWeekDays = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];
    private const string DefaultCronExpression = "1 0 4 * * ?";

    private bool _isInternalUpdate;
    private CronScheduleMode _mode = CronScheduleMode.Daily;

    public static readonly DependencyProperty CronExpressionProperty =
        DependencyProperty.Register(
            nameof(CronExpression),
            typeof(string),
            typeof(CronSchedulePicker),
            new FrameworkPropertyMetadata(
                DefaultCronExpression,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnCronExpressionChanged));

    public string CronExpression
    {
        get => (string)GetValue(CronExpressionProperty);
        set => SetValue(CronExpressionProperty, value);
    }

    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(
            nameof(DisplayText),
            typeof(string),
            typeof(CronSchedulePicker),
            new PropertyMetadata("每天 04:00:01 执行"));

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public CronSchedulePicker()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        InitTimeComboBox(HourComboBox, 24);
        InitTimeComboBox(MinuteComboBox, 60);
        InitTimeComboBox(SecondComboBox, 60);
        ApplyCronToEditor(CronExpression);
    }

    private static void OnCronExpressionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CronSchedulePicker control || control._isInternalUpdate)
        {
            return;
        }

        control.ApplyCronToEditor(e.NewValue?.ToString() ?? string.Empty);
    }

    private static void InitTimeComboBox(ComboBox comboBox, int count)
    {
        comboBox.ItemsSource = Enumerable.Range(0, count).Select(i => i.ToString("00")).ToList();
    }

    private void TriggerButton_OnClick(object sender, RoutedEventArgs e)
    {
        PickerPopup.IsOpen = true;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        PickerPopup.IsOpen = false;
    }

    private void ModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInternalUpdate)
        {
            return;
        }

        _mode = GetModeFromCombo();
        WeeklyPanel.Visibility = _mode == CronScheduleMode.Weekly ? Visibility.Visible : Visibility.Collapsed;
        UpdateOutput();
    }

    private void TimeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInternalUpdate)
        {
            return;
        }

        UpdateOutput();
    }

    private void WeekdayCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isInternalUpdate || _mode != CronScheduleMode.Weekly)
        {
            return;
        }

        UpdateOutput();
    }

    private void UpdateOutput()
    {
        if (HourComboBox.SelectedItem == null || MinuteComboBox.SelectedItem == null || SecondComboBox.SelectedItem == null)
        {
            return;
        }

        var hour = HourComboBox.SelectedItem.ToString() ?? "00";
        var minute = MinuteComboBox.SelectedItem.ToString() ?? "00";
        var second = SecondComboBox.SelectedItem.ToString() ?? "00";

        string cron;
        string display;

        if (_mode == CronScheduleMode.Daily)
        {
            cron = $"{second} {minute} {hour} * * ?";
            display = $"每天 {hour}:{minute}:{second} 执行";
        }
        else
        {
            var selectedDays = GetSelectedWeekDays();
            if (selectedDays.Count == 0)
            {
                cron = string.Empty;
                display = $"每周(请选择周几) {hour}:{minute}:{second} 执行";
            }
            else
            {
                cron = $"{second} {minute} {hour} ? * {string.Join(",", selectedDays)}";
                display = $"每周{ToChineseWeekDays(selectedDays)} {hour}:{minute}:{second} 执行";
            }
        }

        _isInternalUpdate = true;
        SetCurrentValue(CronExpressionProperty, cron);
        SetCurrentValue(DisplayTextProperty, display);
        _isInternalUpdate = false;
    }

    private void ApplyCronToEditor(string cron)
    {
        _isInternalUpdate = true;
        try
        {
            if (string.IsNullOrWhiteSpace(cron))
            {
                SetDefaultEditorState();
                return;
            }

            if (TryParseDaily(cron, out var dailySecond, out var dailyMinute, out var dailyHour))
            {
                SetMode(CronScheduleMode.Daily);
                SetTime(dailyHour, dailyMinute, dailySecond);
                ClearAllWeekdaySelection();
                UpdateOutput();
                return;
            }

            if (TryParseWeekly(cron, out var weeklySecond, out var weeklyMinute, out var weeklyHour, out var weekDays))
            {
                SetMode(CronScheduleMode.Weekly);
                SetTime(weeklyHour, weeklyMinute, weeklySecond);
                SetWeekdaySelection(weekDays);
                UpdateOutput();
                return;
            }

            SetCurrentValue(DisplayTextProperty, $"自定义 Cron: {cron}");
        }
        finally
        {
            _isInternalUpdate = false;
        }
    }

    private void SetDefaultEditorState()
    {
        SetMode(CronScheduleMode.Daily);
        SetTime(4, 0, 1);
        ClearAllWeekdaySelection();
        UpdateOutput();
    }

    private void SetMode(CronScheduleMode mode)
    {
        _mode = mode;
        ModeComboBox.SelectedIndex = mode == CronScheduleMode.Daily ? 0 : 1;
        WeeklyPanel.Visibility = mode == CronScheduleMode.Weekly ? Visibility.Visible : Visibility.Collapsed;
    }

    private CronScheduleMode GetModeFromCombo()
    {
        return ModeComboBox.SelectedIndex == 1 ? CronScheduleMode.Weekly : CronScheduleMode.Daily;
    }

    private void SetTime(int hour, int minute, int second)
    {
        HourComboBox.SelectedItem = hour.ToString("00");
        MinuteComboBox.SelectedItem = minute.ToString("00");
        SecondComboBox.SelectedItem = second.ToString("00");
    }

    private void ClearAllWeekdaySelection()
    {
        foreach (var checkBox in FindWeekdayCheckBoxes())
        {
            checkBox.IsChecked = false;
        }
    }

    private void SetWeekdaySelection(IEnumerable<string> selectedWeekDays)
    {
        var selectedSet = new HashSet<string>(selectedWeekDays);
        foreach (var checkBox in FindWeekdayCheckBoxes())
        {
            var tag = checkBox.Tag?.ToString();
            checkBox.IsChecked = !string.IsNullOrWhiteSpace(tag) && selectedSet.Contains(tag);
        }
    }

    private List<string> GetSelectedWeekDays()
    {
        var selected = new HashSet<string>(
            FindWeekdayCheckBoxes()
                .Where(c => c.IsChecked == true)
                .Select(c => c.Tag?.ToString())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Cast<string>());

        return OrderedWeekDays.Where(selected.Contains).ToList();
    }

    private IEnumerable<CheckBox> FindWeekdayCheckBoxes()
    {
        return WeeklyPanel.Children
            .OfType<UniformGrid>()
            .SelectMany(grid => grid.Children.OfType<CheckBox>());
    }

    private static bool TryParseDaily(string cron, out int second, out int minute, out int hour)
    {
        second = 0;
        minute = 0;
        hour = 0;
        if (!TrySplitQuartzCron(cron, out var parts))
        {
            return false;
        }

        var day = parts[3];
        var month = parts[4];
        var week = parts[5];

        var isDaily = month == "*"
                      && ((day == "*" && week == "?") || (day == "?" && week == "*"));

        if (!isDaily)
        {
            return false;
        }

        return ParseTime(parts[0], parts[1], parts[2], out second, out minute, out hour);
    }

    private static bool TryParseWeekly(string cron, out int second, out int minute, out int hour, out List<string> weekDays)
    {
        second = 0;
        minute = 0;
        hour = 0;
        weekDays = new List<string>();

        if (!TrySplitQuartzCron(cron, out var parts))
        {
            return false;
        }

        if (parts[3] != "?" || parts[4] != "*")
        {
            return false;
        }

        if (!ParseTime(parts[0], parts[1], parts[2], out second, out minute, out hour))
        {
            return false;
        }

        var days = parts[5]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeWeekDayToken)
            .ToList();

        if (days.Count == 0 || days.Any(string.IsNullOrWhiteSpace) || days.Any(d => !OrderedWeekDays.Contains(d!)))
        {
            return false;
        }

        weekDays = days!.Cast<string>().ToList();
        return true;
    }

    private static bool TrySplitQuartzCron(string cron, out string[] parts)
    {
        parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 6 or 7)
        {
            return true;
        }

        parts = Array.Empty<string>();
        return false;
    }

    private static bool ParseTime(string secondRaw, string minuteRaw, string hourRaw, out int second, out int minute, out int hour)
    {
        second = 0;
        minute = 0;
        hour = 0;

        if (!int.TryParse(secondRaw, out second)
            || !int.TryParse(minuteRaw, out minute)
            || !int.TryParse(hourRaw, out hour))
        {
            return false;
        }

        return second is >= 0 and <= 59
               && minute is >= 0 and <= 59
               && hour is >= 0 and <= 23;
    }

    private static string? NormalizeWeekDayToken(string token)
    {
        var normalized = token.Trim().ToUpperInvariant();

        return normalized switch
        {
            "1" => "SUN",
            "2" => "MON",
            "3" => "TUE",
            "4" => "WED",
            "5" => "THU",
            "6" => "FRI",
            "7" => "SAT",
            _ => normalized
        };
    }

    private static string ToChineseWeekDays(IEnumerable<string> weekDays)
    {
        return string.Join('、', weekDays.Select(day => day switch
        {
            "MON" => "一",
            "TUE" => "二",
            "WED" => "三",
            "THU" => "四",
            "FRI" => "五",
            "SAT" => "六",
            "SUN" => "日",
            _ => day
        }));
    }
}
