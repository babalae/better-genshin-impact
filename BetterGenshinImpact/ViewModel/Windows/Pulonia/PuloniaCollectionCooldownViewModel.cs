using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 路线 CD 的独立表单草稿：先在“自动识别 / 不检查 / 自定义周期”三者中选择，自定义时再选具体周期；
/// 只有确认后才将整个配置对象应用到任务。叶子节点另显示本账号的 CD 状态，并可清除记录。
/// </summary>
public partial class PuloniaCollectionCooldownViewModel : ObservableObject
{
    /// <summary>“自动识别”选项键。</summary>
    private const string AutoChoice = "auto";

    /// <summary>“不检查”选项键。</summary>
    private const string DisabledChoice = "disabled";

    /// <summary>“自定义周期”选项键。</summary>
    private const string CustomChoice = "custom";

    /// <summary>用于识别材料的原始文件位置。</summary>
    private string? _path;

    /// <summary>原始仓库资源身份，避免缓存目录丢失材料名称。</summary>
    private ScriptResourceReference? _resource;

    /// <summary>是否有可识别的路线位置；设置方案中的草稿没有。</summary>
    private bool _hasResource;

    /// <summary>清除本节点路线 CD 记录的操作，返回清除后的状态文字与是否仍有记录；没有记录或不是叶子时为空。</summary>
    private Func<Task<(string Status, bool HasRecord)>>? _clearRecord;

    /// <summary>三种选择及中文名称。</summary>
    public IReadOnlyDictionary<string, string> Choices { get; } = new Dictionary<string, string>
    {
        [AutoChoice] = "按材料自动识别（推荐）", [DisabledChoice] = "不检查 CD", [CustomChoice] = "自定义周期"
    };

    /// <summary>自定义周期的具体模式及中文名称。</summary>
    public IReadOnlyDictionary<PuloniaCollectionCooldownMode, string> CustomModes { get; } =
        new Dictionary<PuloniaCollectionCooldownMode, string>
        {
            [PuloniaCollectionCooldownMode.Rolling] = "成功后每隔 N 小时",
            [PuloniaCollectionCooldownMode.Daily] = "每日固定时刻重置",
            [PuloniaCollectionCooldownMode.Weekly] = "每周固定时刻重置",
            [PuloniaCollectionCooldownMode.CalendarDays] = "成功日期后第 N 天 00:00（午夜估算）",
            [PuloniaCollectionCooldownMode.Once] = "只执行一次"
        };

    /// <summary>服务器重置小时选项。</summary>
    public IReadOnlyList<int> ResetHours { get; } = Enumerable.Range(0, 24).ToArray();

    /// <summary>星期选项，周一为 1，周日为 7。</summary>
    public IReadOnlyDictionary<int, string> Weekdays { get; } = new Dictionary<int, string>
        { [1] = "周一", [2] = "周二", [3] = "周三", [4] = "周四", [5] = "周五", [6] = "周六", [7] = "周日" };

    /// <summary>当前选择：自动识别、不检查或自定义周期。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode), nameof(ShowCustom), nameof(ShowHours), nameof(ShowDays), nameof(ShowResetHour),
        nameof(ShowWeekday), nameof(Preview))]
    private string _choice = AutoChoice;

    /// <summary>自定义周期的具体模式。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode), nameof(ShowHours), nameof(ShowDays), nameof(ShowResetHour), nameof(ShowWeekday),
        nameof(Preview))]
    private PuloniaCollectionCooldownMode _customMode = PuloniaCollectionCooldownMode.Rolling;

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
    private int _resetHour = PuloniaServerTime.DayResetHour;

    /// <summary>每周重置星期草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private int _resetWeekday = 1;

    /// <summary>整个配置对象的继承或覆盖来源。</summary>
    [ObservableProperty]
    private string _configurationSource = "能力默认值";

    /// <summary>本账号下这条路线的 CD 状态；只有叶子节点提供。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusText;

    /// <summary>分组下属路线的识别分布，或目录引用的说明。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDistribution))]
    private string? _distributionText;

    /// <summary>同一文件在其他计划中使用不同规则时的提示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    private string? _conflictText;

    /// <summary>实际写入参数的模式。</summary>
    public PuloniaCollectionCooldownMode Mode => Choice switch
    {
        AutoChoice => PuloniaCollectionCooldownMode.Auto,
        DisabledChoice => PuloniaCollectionCooldownMode.Disabled,
        _ => CustomMode
    };

    /// <summary>是否显示自定义周期选择。</summary>
    public bool ShowCustom => Choice == CustomChoice;

    /// <summary>是否显示相对小时输入。</summary>
    public bool ShowHours => Mode == PuloniaCollectionCooldownMode.Rolling;

    /// <summary>是否显示日期间隔输入。</summary>
    public bool ShowDays => Mode == PuloniaCollectionCooldownMode.CalendarDays;

    /// <summary>是否显示服务器重置小时。</summary>
    public bool ShowResetHour => Mode is PuloniaCollectionCooldownMode.Daily or PuloniaCollectionCooldownMode.Weekly;

    /// <summary>是否显示每周重置星期。</summary>
    public bool ShowWeekday => Mode == PuloniaCollectionCooldownMode.Weekly;

    /// <summary>是否显示本账号状态。</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    /// <summary>是否显示识别分布。</summary>
    public bool HasDistribution => !string.IsNullOrEmpty(DistributionText);

    /// <summary>是否显示规则冲突提示。</summary>
    public bool HasConflict => !string.IsNullOrEmpty(ConflictText);

    /// <summary>是否可以清除本节点的成功记录。</summary>
    public bool CanClearRecord => _clearRecord is not null;

    /// <summary>识别结果和有效规则；无效输入在预览中显示原因。</summary>
    public string Preview
    {
        get
        {
            try
            {
                var policy = PuloniaCollectionCooldown.Resolve(_path, _resource,
                    new JObject { [PuloniaCollectionCooldown.ParameterName] = BuildValue() });
                var recognition = !_hasResource
                    ? "识别：设置方案不对应具体路线，挂到节点后按各路线的目录识别"
                    : policy.MaterialName is { } material
                        ? $"识别：目录「{material}」"
                        : "识别：路径中没有已知的材料或类别目录，自动识别时成功后不会跳过，可选择“自定义周期”";
                return $"{recognition}\n规则：{policy.Description}\n依据：{PuloniaCollectionCooldown.CompletionBasis}";
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or PuloniaTaskValidationException)
            { return "配置无效：" + ex.Message; }
        }
    }

    /// <summary>加载独立配置草稿，不持有任务的可变参数对象。</summary>
    public void Load(JToken? value, string source = "能力默认值")
    {
        var settings = value as JObject ?? PuloniaCollectionCooldown.CreateDefaults();
        PuloniaCollectionCooldown.TryParseMode(settings.Value<string>("mode"), out var mode);
        Choice = mode switch
        {
            PuloniaCollectionCooldownMode.Auto => AutoChoice,
            PuloniaCollectionCooldownMode.Disabled => DisabledChoice,
            _ => CustomChoice
        };
        if (Choice == CustomChoice) CustomMode = mode;
        Hours = (settings.Value<double?>("hours") ?? 24).ToString("g", CultureInfo.InvariantCulture);
        Days = (settings.Value<int?>("days") ?? 1).ToString(CultureInfo.InvariantCulture);
        ResetHour = settings.Value<int?>("reset_hour") ?? PuloniaServerTime.DayResetHour;
        ResetWeekday = settings.Value<int?>("reset_weekday") ?? 1;
        ConfigurationSource = source;
        OnPropertyChanged(nameof(Preview));
    }

    /// <summary>更新材料识别位置，目录预览使用虚拟文件名，不读取或创建磁盘文件。</summary>
    public void SetResource(string? path, ScriptResourceReference? resource, bool isDirectory = false)
    {
        _path = isDirectory && path is not null ? System.IO.Path.Combine(path, "__preview__.json") : path;
        _resource = isDirectory && resource is not null ? resource.WithPath(resource.RelativePath + "/__preview__.json") : resource;
        _hasResource = path is not null || resource is not null;
        OnPropertyChanged(nameof(Preview));
    }

    /// <summary>设置本账号状态与清除操作；清除操作返回清除后的状态文字与是否仍有记录（用户取消确认时仍有）。</summary>
    internal void SetStatus(string? text, Func<Task<(string Status, bool HasRecord)>>? clearRecord)
    {
        StatusText = text;
        _clearRecord = clearRecord;
        OnPropertyChanged(nameof(CanClearRecord));
        ClearRecordCommand.NotifyCanExecuteChanged();
    }

    /// <summary>设置分组的识别分布或目录说明。</summary>
    internal void SetDistribution(string? text) => DistributionText = text;

    /// <summary>设置跨计划规则不一致的提示。</summary>
    internal void SetConflict(string? text) => ConflictText = text;

    /// <summary>清除本节点在当前账号下的成功记录，让路线下次照常执行；确认由调用方提供的操作完成。</summary>
    [RelayCommand(CanExecute = nameof(CanClearRecord))]
    private async Task ClearRecordAsync()
    {
        if (_clearRecord is not { } clear) return;
        var (status, hasRecord) = await clear();
        StatusText = status;
        // 清除后本次打开的表单不再提供清除；取消确认时仍有记录，可再次清除
        if (!hasRecord)
        {
            _clearRecord = null;
            OnPropertyChanged(nameof(CanClearRecord));
            ClearRecordCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>只输出当前模式使用的字段，配置对象作为一个整体参与参数覆盖。</summary>
    public JObject BuildValue()
    {
        var value = new JObject { ["mode"] = PuloniaCollectionCooldown.FormatMode(Mode) };
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
        PuloniaCollectionCooldown.ValidateSettings(value, "路线 CD");
        return value;
    }
}
