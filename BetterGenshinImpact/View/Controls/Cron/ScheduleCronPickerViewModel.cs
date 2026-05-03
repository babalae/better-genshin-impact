using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.View.Controls.Cron;

public enum ScheduleMode
{
    Daily,
    Weekly
}

public partial class ScheduleCronPickerViewModel : ObservableObject
{
    private sealed record Snapshot(
        ScheduleMode Mode,
        int Hour,
        int Minute,
        int Second,
        bool Monday,
        bool Tuesday,
        bool Wednesday,
        bool Thursday,
        bool Friday,
        bool Saturday,
        bool Sunday
    );

    private static readonly (string Quartz, string Chinese, int Numeric)[] Weekdays =
    [
        ("MON", "一", 2),
        ("TUE", "二", 3),
        ("WED", "三", 4),
        ("THU", "四", 5),
        ("FRI", "五", 6),
        ("SAT", "六", 7),
        ("SUN", "日", 1)
    ];

    private Snapshot _snapshot;
    private bool _ignoreRevertOnClose;
    private string? _lastLoadedCron;

    public ObservableCollection<int> Hours { get; } = new(Enumerable.Range(0, 24));
    public ObservableCollection<int> Minutes { get; } = new(Enumerable.Range(0, 60));
    public ObservableCollection<int> Seconds { get; } = new(Enumerable.Range(0, 60));

    [ObservableProperty]
    private bool _isPopupOpen;

    [ObservableProperty]
    private ScheduleMode _selectedMode = ScheduleMode.Daily;

    [ObservableProperty]
    private int _hour = 8;

    [ObservableProperty]
    private int _minute;

    [ObservableProperty]
    private int _second;

    [ObservableProperty]
    private bool _monday = true;

    [ObservableProperty]
    private bool _tuesday;

    [ObservableProperty]
    private bool _wednesday;

    [ObservableProperty]
    private bool _thursday;

    [ObservableProperty]
    private bool _friday;

    [ObservableProperty]
    private bool _saturday;

    [ObservableProperty]
    private bool _sunday;

    [ObservableProperty]
    private string _displayText = string.Empty;

    public event EventHandler<string>? ApplyRequested;

    public ScheduleCronPickerViewModel()
    {
        _snapshot = CreateSnapshot();
        UpdateDisplayText();
    }

    public void LoadFromCron(string? cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            _lastLoadedCron = cronExpression;
            UpdateDisplayText();
            return;
        }

        if (string.Equals(_lastLoadedCron, cronExpression, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoadedCron = cronExpression;

        if (!TryParseQuartzCron(cronExpression, out var parsed))
        {
            DisplayText = cronExpression.Trim();
            _snapshot = CreateSnapshot();
            return;
        }

        _ignoreRevertOnClose = true;
        SelectedMode = parsed.Mode;
        Hour = parsed.Hour;
        Minute = parsed.Minute;
        Second = parsed.Second;
        Monday = parsed.Monday;
        Tuesday = parsed.Tuesday;
        Wednesday = parsed.Wednesday;
        Thursday = parsed.Thursday;
        Friday = parsed.Friday;
        Saturday = parsed.Saturday;
        Sunday = parsed.Sunday;
        _ignoreRevertOnClose = false;

        UpdateDisplayText();
        _snapshot = CreateSnapshot();
    }

    [RelayCommand]
    private void Apply()
    {
        EnsureWeeklyHasAtLeastOneDay();

        _ignoreRevertOnClose = true;
        var cron = BuildQuartzCron();
        ApplyRequested?.Invoke(this, cron);
        LoadFromCron(cron);
        IsPopupOpen = false;
        _ignoreRevertOnClose = false;
    }

    [RelayCommand]
    private void Cancel()
    {
        _ignoreRevertOnClose = true;
        RestoreSnapshot(_snapshot);
        IsPopupOpen = false;
        _ignoreRevertOnClose = false;
    }

    partial void OnIsPopupOpenChanged(bool value)
    {
        if (value)
        {
            _snapshot = CreateSnapshot();
            _ignoreRevertOnClose = false;
            return;
        }

        if (!_ignoreRevertOnClose)
        {
            RestoreSnapshot(_snapshot);
        }

        _ignoreRevertOnClose = false;
    }

    partial void OnSelectedModeChanged(ScheduleMode value) => UpdateDisplayText();
    partial void OnHourChanged(int value) => UpdateDisplayText();
    partial void OnMinuteChanged(int value) => UpdateDisplayText();
    partial void OnSecondChanged(int value) => UpdateDisplayText();
    partial void OnMondayChanged(bool value) => UpdateDisplayText();
    partial void OnTuesdayChanged(bool value) => UpdateDisplayText();
    partial void OnWednesdayChanged(bool value) => UpdateDisplayText();
    partial void OnThursdayChanged(bool value) => UpdateDisplayText();
    partial void OnFridayChanged(bool value) => UpdateDisplayText();
    partial void OnSaturdayChanged(bool value) => UpdateDisplayText();
    partial void OnSundayChanged(bool value) => UpdateDisplayText();

    private void UpdateDisplayText()
    {
        DisplayText = BuildChineseText();
    }

    private string BuildChineseText()
    {
        var time = $"{Hour:00}:{Minute:00}:{Second:00}";

        return SelectedMode switch
        {
            ScheduleMode.Daily => $"每天 {time} 执行",
            ScheduleMode.Weekly => $"每周{BuildSelectedWeekdaysChinese()} {time} 执行",
            _ => time
        };
    }

    private string BuildSelectedWeekdaysChinese()
    {
        var selected = Weekdays
            .Where(d => IsSelected(d.Quartz))
            .Select(d => d.Chinese)
            .ToArray();

        if (selected.Length == 0)
        {
            return "一";
        }

        return string.Join("、", selected);
    }

    private string BuildQuartzCron()
    {
        var sec = Second.ToString(CultureInfo.InvariantCulture);
        var min = Minute.ToString(CultureInfo.InvariantCulture);
        var hour = Hour.ToString(CultureInfo.InvariantCulture);

        return SelectedMode switch
        {
            ScheduleMode.Daily => $"{sec} {min} {hour} * * ?",
            ScheduleMode.Weekly => $"{sec} {min} {hour} ? * {BuildSelectedWeekdaysQuartz()}",
            _ => $"{sec} {min} {hour} * * ?"
        };
    }

    private string BuildSelectedWeekdaysQuartz()
    {
        var selected = Weekdays
            .Where(d => IsSelected(d.Quartz))
            .Select(d => d.Quartz)
            .ToArray();

        if (selected.Length == 0)
        {
            return "MON";
        }

        return string.Join(",", selected);
    }

    private void EnsureWeeklyHasAtLeastOneDay()
    {
        if (SelectedMode != ScheduleMode.Weekly)
        {
            return;
        }

        if (Monday || Tuesday || Wednesday || Thursday || Friday || Saturday || Sunday)
        {
            return;
        }

        Monday = true;
    }

    private Snapshot CreateSnapshot() =>
        new(
            SelectedMode,
            Hour,
            Minute,
            Second,
            Monday,
            Tuesday,
            Wednesday,
            Thursday,
            Friday,
            Saturday,
            Sunday
        );

    private void RestoreSnapshot(Snapshot snapshot)
    {
        SelectedMode = snapshot.Mode;
        Hour = snapshot.Hour;
        Minute = snapshot.Minute;
        Second = snapshot.Second;
        Monday = snapshot.Monday;
        Tuesday = snapshot.Tuesday;
        Wednesday = snapshot.Wednesday;
        Thursday = snapshot.Thursday;
        Friday = snapshot.Friday;
        Saturday = snapshot.Saturday;
        Sunday = snapshot.Sunday;
        UpdateDisplayText();
    }

    private bool IsSelected(string quartzDay) =>
        quartzDay switch
        {
            "MON" => Monday,
            "TUE" => Tuesday,
            "WED" => Wednesday,
            "THU" => Thursday,
            "FRI" => Friday,
            "SAT" => Saturday,
            "SUN" => Sunday,
            _ => false
        };

    private static bool TryParseQuartzCron(string cron, out Snapshot snapshot)
    {
        snapshot = new Snapshot(ScheduleMode.Daily, 8, 0, 0, true, false, false, false, false, false, false);

        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 6)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sec))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var min))
        {
            return false;
        }

        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour))
        {
            return false;
        }

        if (sec is < 0 or > 59 || min is < 0 or > 59 || hour is < 0 or > 23)
        {
            return false;
        }

        var dayOfMonth = parts[3];
        var month = parts[4];
        var dayOfWeek = parts[5];

        if (dayOfMonth == "*" && month == "*" && dayOfWeek == "?")
        {
            snapshot = new Snapshot(ScheduleMode.Daily, hour, min, sec, true, false, false, false, false, false, false);
            return true;
        }

        if (dayOfMonth == "?" && month == "*" && dayOfWeek != "?" && dayOfWeek != "*")
        {
            var selected = new bool[7];
            var tokens = dayOfWeek.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                var normalized = token.ToUpperInvariant();
                var idx = Array.FindIndex(Weekdays, d => d.Quartz == normalized);
                if (idx >= 0)
                {
                    selected[idx] = true;
                    continue;
                }

                if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                {
                    var idx2 = Array.FindIndex(Weekdays, d => d.Numeric == numeric);
                    if (idx2 >= 0)
                    {
                        selected[idx2] = true;
                    }
                }
            }

            if (selected.All(x => !x))
            {
                selected[0] = true;
            }

            snapshot = new Snapshot(
                ScheduleMode.Weekly,
                hour,
                min,
                sec,
                selected[0],
                selected[1],
                selected[2],
                selected[3],
                selected[4],
                selected[5],
                selected[6]
            );
            return true;
        }

        return false;
    }
}

