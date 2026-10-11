using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>采集 CD 的独立表单草稿；只有确认后才将整个配置对象应用到任务。</summary>
public partial class PuloniaCollectionCooldownViewModel : ObservableObject
{
    /// <summary>用于识别材料的原始文件位置。</summary>
    private string? _path;
    /// <summary>原始仓库资源身份，避免缓存目录丢失材料名称。</summary>
    private ScriptResourceReference? _resource;

    /// <summary>可选规则模式及中文名称。</summary>
    public IReadOnlyDictionary<string, string> Modes { get; } = new Dictionary<string, string>
    {
        ["auto"] = "自动识别", ["disabled"] = "关闭限制", ["rolling"] = "相对时长",
        ["daily"] = "每日重置", ["calendar_days"] = "日期间隔（午夜估算）", ["weekly"] = "每周重置", ["once"] = "一次性"
    };
    /// <summary>服务器重置小时选项。</summary>
    public IReadOnlyList<int> ResetHours { get; } = Enumerable.Range(0, 24).ToArray();
    /// <summary>星期选项，周一为 1，周日为 7。</summary>
    public IReadOnlyDictionary<int, string> Weekdays { get; } = new Dictionary<int, string>
        { [1] = "周一", [2] = "周二", [3] = "周三", [4] = "周四", [5] = "周五", [6] = "周六", [7] = "周日" };

    /// <summary>当前规则模式。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHours), nameof(ShowDays), nameof(ShowResetHour), nameof(ShowWeekday), nameof(Preview))]
    private string _mode = "auto";
    /// <summary>相对时长草稿，以小时为单位。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _hours = "24";
    /// <summary>午夜日期间隔草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _days = "1";
    /// <summary>服务器重置小时草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private int _resetHour = 4;
    /// <summary>每周重置星期草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private int _resetWeekday = 1;
    /// <summary>整个配置对象的继承或覆盖来源。</summary>
    [ObservableProperty]
    private string _configurationSource = "能力默认值";

    /// <summary>是否显示相对小时输入。</summary>
    public bool ShowHours => Mode == "rolling";
    /// <summary>是否显示日期间隔输入。</summary>
    public bool ShowDays => Mode == "calendar_days";
    /// <summary>是否显示服务器重置小时。</summary>
    public bool ShowResetHour => Mode is "daily" or "weekly";
    /// <summary>是否显示每周重置星期。</summary>
    public bool ShowWeekday => Mode == "weekly";
    /// <summary>当前识别材料和有效规则；无效输入在预览中显示原因。</summary>
    public string Preview
    {
        get
        {
            try
            {
                var policy = PuloniaCollectionCooldown.Resolve(_path, _resource,
                    new JObject { [PuloniaCollectionCooldown.ParameterName] = BuildValue() });
                return $"采集物：{policy.MaterialName ?? "未识别"}\n规则：{policy.Description}\n依据：按整份文件执行成功估算采集完成";
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or PuloniaTaskValidationException)
            { return "配置无效：" + ex.Message; }
        }
    }

    /// <summary>加载独立配置草稿，不持有任务的可变参数对象。</summary>
    public void Load(JToken? value, string source = "能力默认值")
    {
        var settings = value as JObject ?? PuloniaCollectionCooldown.CreateDefaults();
        Mode = settings.Value<string>("mode") ?? "auto";
        Hours = (settings.Value<double?>("hours") ?? 24).ToString("g", CultureInfo.InvariantCulture);
        Days = (settings.Value<int?>("days") ?? 1).ToString(CultureInfo.InvariantCulture);
        ResetHour = settings.Value<int?>("reset_hour") ?? 4;
        ResetWeekday = settings.Value<int?>("reset_weekday") ?? 1;
        ConfigurationSource = source;
        OnPropertyChanged(nameof(Preview));
    }

    /// <summary>更新材料识别位置，目录预览使用虚拟文件名，不读取或创建磁盘文件。</summary>
    public void SetResource(string? path, ScriptResourceReference? resource, bool isDirectory = false)
    {
        _path = isDirectory && path is not null ? System.IO.Path.Combine(path, "__preview__.json") : path;
        _resource = isDirectory && resource is not null ? resource.WithPath(resource.RelativePath + "/__preview__.json") : resource;
        OnPropertyChanged(nameof(Preview));
    }

    /// <summary>只输出当前模式使用的字段，配置对象作为一个整体参与参数覆盖。</summary>
    public JObject BuildValue()
    {
        var value = new JObject { ["mode"] = Mode };
        if (ShowHours)
        {
            if (!double.TryParse(Hours, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
                throw new FormatException("相对 CD 小时数必须是数字。");
            value["hours"] = hours;
        }
        if (ShowDays)
        {
            if (!int.TryParse(Days, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
                throw new FormatException("日期间隔必须是正整数天数。");
            value["days"] = days;
        }
        if (ShowResetHour) value["reset_hour"] = ResetHour;
        if (ShowWeekday) value["reset_weekday"] = ResetWeekday;
        PuloniaCollectionCooldown.ValidateSettings(value, "采集 CD");
        return value;
    }
}
