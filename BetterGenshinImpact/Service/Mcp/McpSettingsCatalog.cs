using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Service.Mcp;

/// <summary>
/// 为数量庞大的 AllConfig 属性建立可搜索、可解释的运行时目录。
/// </summary>
public sealed partial class McpSettingsCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, XmlDocumentation>> XmlDocumentationIndex =
        new(LoadXmlDocumentation);

    private static readonly IReadOnlyDictionary<string, string> SectionDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["root"] = "BetterGI 全局基础设置，包括截图方式、触发频率和当前选择项。",
            ["maskWindowConfig"] = "遮罩窗口、日志、状态栏、识别框、字体、颜色、缩放和点击穿透。",
            ["commonConfig"] = "通用运行行为、截图保存、日志、语言、界面和任务公共选项。",
            ["genshinStartConfig"] = "原神进程路径、自动启动、自动进入游戏、窗口和账号启动流程。",
            ["autoPickConfig"] = "自动拾取触发器、黑白名单、文字识别和拾取行为。",
            ["autoSkipConfig"] = "自动剧情、对话选择、自动邀约和语音/文本等待行为。",
            ["autoFishingConfig"] = "自动钓鱼的鱼饵、甩杆、识别、时间策略和失败处理。",
            ["quickTeleportConfig"] = "大地图快速传送、地图识别和传送交互。",
            ["autoGeniusInvokationConfig"] = "自动七圣召唤的牌组策略和执行选项。",
            ["autoWoodConfig"] = "自动伐木的角色、队伍和砍伐行为。轮数上限属于运行参数。",
            ["autoFightConfig"] = "自动战斗策略、队伍识别、超时、技能和战斗结束判断。",
            ["autoMusicGameConfig"] = "自动音游/专辑的难度、延迟、演奏和谱面选项。",
            ["autoDomainConfig"] = "自动秘境名称、队伍、树脂消耗、领奖和战后分解。",
            ["autoBossConfig"] = "自动首领讨伐的 Boss、次数、队伍、路线、树脂和战斗策略。",
            ["autoStygianOnslaughtConfig"] = "自动幽境危战的难度、队伍、轮数、策略和结束条件。",
            ["autoArtifactSalvageConfig"] = "圣遗物分解星级、套装过滤、OCR 失败策略和 JS 规则；会影响游戏内物品。",
            ["autoEatConfig"] = "自动吃药/食物的角色生命值识别、物品和冷却策略。",
            ["autoLeyLineOutcropConfig"] = "自动地脉花国家、类型、次数、树脂、战斗和掉落扫描。",
            ["autoCookConfig"] = "自动烹饪的连续制作和恢复按钮处理。",
            ["mapMaskConfig"] = "地图遮罩、点位标签、显示和交互设置。",
            ["skillCdConfig"] = "技能冷却显示和角色技能跟踪。",
            ["autoRedeemCodeConfig"] = "兑换码剪贴板监听和自动兑换行为。",
            ["getGridIconsConfig"] = "背包网格图标采集、分类、命名和模型测试。",
            ["macroConfig"] = "宏功能和按键触发行为。",
            ["recordConfig"] = "键鼠录制和回放相关设置。",
            ["musicConfig"] = "MIDI/乐器演奏、曲库、输入通道、速度和播放行为。",
            ["scriptConfig"] = "脚本仓库渠道、更新周期、订阅自动更新和仓库窗口状态。",
            ["pathingConditionConfig"] = "地图追踪通用队伍、自动拾取、战斗和路径执行条件。",
            ["hotKeyConfig"] = "BetterGI 功能热键及其监听方式。",
            ["notificationConfig"] = "通知事件和 Bark、邮件、Telegram、Webhook 等渠道凭据；包含大量敏感字段。",
            ["keyBindingsConfig"] = "游戏操作和 BetterGI 模拟输入使用的按键映射。",
            ["otherConfig"] = "自动重启、锄地规划、Cookie 等跨功能设置；可能包含敏感字段。",
            ["tpConfig"] = "地图传送、坐标和传送相关高级参数。",
            ["devConfig"] = "开发、调试、数据采集和实验性功能。",
            ["hardwareAccelerationConfig"] = "DirectML、GPU、硬件加速和推理设备设置。",
            ["childSessionConfig"] = "桌面分身、子会话、远程输入、音频和窗口同步。",
            ["agentConfig"] = "内置 AI Agent 的外部 OpenAI-compatible 地址、API Key、模型选择和工具调用轮数。",
        };

    private static readonly IReadOnlyDictionary<string, string> NameTranslations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Auto"] = "自动", ["Enabled"] = "是否启用", ["Enable"] = "启用", ["Disabled"] = "禁用",
            ["Use"] = "使用", ["Strategy"] = "策略", ["Name"] = "名称", ["Path"] = "路径",
            ["Count"] = "数量", ["Max"] = "最大", ["Min"] = "最小", ["Interval"] = "间隔",
            ["Timeout"] = "超时", ["Delay"] = "延迟", ["Threshold"] = "阈值", ["Width"] = "宽度",
            ["Height"] = "高度", ["Color"] = "颜色", ["Font"] = "字体", ["Size"] = "大小",
            ["Opacity"] = "透明度", ["Scale"] = "缩放", ["Mode"] = "模式", ["Type"] = "类型",
            ["List"] = "列表", ["Priority"] = "优先级", ["Country"] = "国家/地区", ["Domain"] = "秘境",
            ["Party"] = "队伍", ["Resin"] = "树脂", ["Original"] = "原粹", ["Condensed"] = "浓缩",
            ["Fragile"] = "脆弱", ["Transient"] = "须臾", ["Reward"] = "奖励", ["Recognition"] = "识别",
            ["Scan"] = "扫描", ["Drop"] = "掉落物", ["Drops"] = "掉落物", ["Screenshot"] = "截图",
            ["Save"] = "保存", ["Folder"] = "文件夹", ["File"] = "文件", ["Notification"] = "通知",
            ["Webhook"] = "Webhook", ["Proxy"] = "代理", ["Endpoint"] = "端点", ["Key"] = "密钥/按键",
            ["Token"] = "令牌", ["Password"] = "密码", ["Cookie"] = "Cookie", ["Language"] = "语言",
            ["Culture"] = "区域语言", ["Hot"] = "热", ["Mouse"] = "鼠标", ["Keyboard"] = "键盘",
            ["Game"] = "游戏", ["Window"] = "窗口", ["Capture"] = "捕获", ["Script"] = "脚本",
            ["Repo"] = "仓库", ["Repository"] = "仓库", ["Update"] = "更新", ["Subscribed"] = "已订阅",
            ["Subscribe"] = "订阅", ["Period"] = "周期", ["Last"] = "上次", ["Selected"] = "选中",
            ["Custom"] = "自定义", ["URL"] = "网址", ["HTTP"] = "网络请求", ["Retry"] = "重试",
            ["Failure"] = "失败", ["Restart"] = "重启", ["Loop"] = "循环", ["Round"] = "轮次",
            ["Daily"] = "每日", ["Fight"] = "战斗", ["Boss"] = "首领", ["Fishing"] = "钓鱼",
            ["Wood"] = "伐木", ["Music"] = "音乐", ["Artifact"] = "圣遗物", ["Salvage"] = "分解",
            ["Ley"] = "地脉", ["Line"] = "线/地脉", ["Outcrop"] = "花", ["Pick"] = "拾取",
            ["Skip"] = "跳过", ["Teleport"] = "传送", ["Map"] = "地图", ["Mask"] = "遮罩",
            ["Log"] = "日志", ["Text"] = "文本", ["Background"] = "背景", ["Border"] = "边框",
            ["Stroke"] = "描边", ["Shadow"] = "阴影", ["Blur"] = "模糊", ["Radius"] = "半径",
            ["Item"] = "项目/物品", ["Grid"] = "网格", ["Inventory"] = "背包", ["Character"] = "角色",
            ["Avatar"] = "角色", ["Team"] = "队伍", ["Switch"] = "切换", ["Start"] = "启动",
            ["Close"] = "关闭", ["Exit"] = "退出", ["Action"] = "动作", ["Completion"] = "完成后",
            ["Schedule"] = "计划周期", ["Task"] = "任务", ["Progress"] = "进度", ["Shell"] = "Shell 命令",
            ["Command"] = "命令", ["Working"] = "工作", ["Directory"] = "目录", ["Arguments"] = "参数",
            ["Check"] = "检查", ["Detect"] = "检测", ["Default"] = "默认", ["Restore"] = "恢复",
            ["Focus"] = "焦点", ["Lost"] = "丢失", ["Only"] = "仅", ["After"] = "之后",
            ["Before"] = "之前", ["Show"] = "显示", ["Hide"] = "隐藏", ["Open"] = "打开",
            ["Audio"] = "音频", ["Volume"] = "音量", ["Device"] = "设备", ["Channel"] = "渠道/通道",
        };

    public IReadOnlyList<McpSettingEntry> Build(AllConfig current)
    {
        AllConfig? defaults = null;
        try
        {
            defaults = new AllConfig();
        }
        catch
        {
            /* 默认值不可用时仍提供当前目录。 */
        }

        var result = new List<McpSettingEntry>();
        BuildObject(current, defaults, typeof(AllConfig), string.Empty, "root", result, [], 0);
        return result.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<McpSettingSection> BuildSections(AllConfig current)
    {
        var entries = Build(current);
        return entries.GroupBy(x => x.Section, StringComparer.OrdinalIgnoreCase)
            .Select(group => new McpSettingSection(
                group.Key,
                SectionDescriptions.GetValueOrDefault(group.Key, $"{group.Key} 设置分区。"),
                group.Count(),
                group.Count(x => x.Writable),
                group.Count(x => x.Sensitive)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static object? SerializeValue(object? value, Type type, bool sensitive, bool includeSensitive)
    {
        if (sensitive && !includeSensitive) return "***REDACTED***";
        if (value is null) return null;
        try
        {
            return JsonSerializer.SerializeToElement(value, type, ConfigService.JsonOptions);
        }
        catch
        {
            return value.ToString();
        }
    }

    public static (object Owner, PropertyInfo Property) ResolveProperty(object root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ArgumentException("设置路径不能为空。", nameof(path));
        object owner = root;
        for (var index = 0; index < parts.Length; index++)
        {
            var property = owner.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                               .FirstOrDefault(x =>
                                   JsonNamingPolicy.CamelCase.ConvertName(x.Name).Equals(parts[index],
                                       StringComparison.OrdinalIgnoreCase)
                                   || x.Name.Equals(parts[index], StringComparison.OrdinalIgnoreCase))
                           ?? throw new ArgumentException($"设置路径不存在：{string.Join('.', parts.Take(index + 1))}",
                               nameof(path));
            if (index == parts.Length - 1) return (owner, property);
            owner = property.GetValue(owner)
                    ?? throw new InvalidOperationException($"设置路径中间属性 {property.Name} 为 null。");
        }

        throw new InvalidOperationException("无法解析设置路径。");
    }

    public static bool IsSensitive(string pathOrName)
    {
        string[] parts =
        [
            "password", "token", "secret", "cookie", "authorization", "credential", "webhook",
            "accesskey", "privatekey", "devicekey", "sendkey", "apikey", "endpoint", "url",
        ];
        return parts.Any(x => pathOrName.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private static void BuildObject(
        object? current,
        object? defaults,
        Type type,
        string prefix,
        string section,
        List<McpSettingEntry> result,
        HashSet<Type> ancestors,
        int depth)
    {
        if (depth > 14 || !ancestors.Add(type)) return;

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(x => x.GetMethod is not null
                                 && x.GetIndexParameters().Length == 0
                                 && x.GetCustomAttribute<JsonIgnoreAttribute>() is null
                                 && !typeof(Delegate).IsAssignableFrom(x.PropertyType)))
        {
            var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                           ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            var path = string.IsNullOrEmpty(prefix) ? jsonName : $"{prefix}.{jsonName}";
            var currentValue = SafeGet(property, current);
            var defaultValue = SafeGet(property, defaults);
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var currentSection = string.IsNullOrEmpty(prefix) && !IsLeaf(propertyType) ? jsonName : section;

            if (!IsLeaf(propertyType))
            {
                BuildObject(currentValue, defaultValue, propertyType, path, currentSection, result,
                    new HashSet<Type>(ancestors), depth + 1);
                continue;
            }

            var documentation = GetDocumentation(type, property);
            var sectionDescription = SectionDescriptions.GetValueOrDefault(currentSection, $"{currentSection} 设置分区。 ");
            var description = !string.IsNullOrWhiteSpace(documentation.Text)
                ? documentation.Text
                : $"{sectionDescription} 此项控制“{TranslatePropertyName(property.Name)}”（源码属性 {property.Name}，路径 {path}）。";
            var range = property.GetCustomAttribute<RangeAttribute>();
            result.Add(new McpSettingEntry(
                path,
                currentSection,
                property.Name,
                FriendlyType(property.PropertyType),
                description,
                documentation.Source,
                property.SetMethod is not null,
                IsSensitive(path),
                typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType) && propertyType != typeof(string),
                propertyType.IsEnum ? Enum.GetNames(propertyType) : null,
                propertyType.IsEnum ? GetEnumDescriptions(propertyType) : null,
                property.GetCustomAttribute<DefaultValueAttribute>()?.Value,
                range?.Minimum,
                range?.Maximum,
                currentValue,
                defaultValue,
                property.PropertyType));
        }
    }

    private static object? SafeGet(PropertyInfo property, object? owner)
    {
        if (owner is null) return null;
        try
        {
            return property.GetValue(owner);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLeaf(Type type) =>
        type.IsValueType
        || type == typeof(string)
        || typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
        || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true;

    private static string FriendlyType(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return $"{FriendlyType(nullable)}?";
        if (type.IsArray) return $"array<{FriendlyType(type.GetElementType()!)}>";
        var enumerable = type.GetInterfaces().Append(type)
            .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is not null) return $"array<{FriendlyType(enumerable.GetGenericArguments()[0])}>";
        return type.IsEnum
            ? $"enum:{type.Name}"
            : type.Name switch
            {
                "Boolean" => "boolean",
                "String" => "string",
                "Int32" => "integer",
                "Int64" => "integer64",
                "Single" or "Double" or "Decimal" => "number",
                _ => type.Name,
            };
    }

    private static XmlDocumentation GetDocumentation(Type ownerType, PropertyInfo property)
    {
        var key = $"P:{ownerType.FullName}.{property.Name}";
        if (XmlDocumentationIndex.Value.TryGetValue(key, out var documentation) &&
            !string.IsNullOrWhiteSpace(documentation.Text))
            return documentation;
        var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description;
        return string.IsNullOrWhiteSpace(description)
            ? new XmlDocumentation(string.Empty, "inferred")
            : new XmlDocumentation(description, "DescriptionAttribute");
    }

    private static IReadOnlyDictionary<string, XmlDocumentation> LoadXmlDocumentation()
    {
        try
        {
            var xmlPath = Path.ChangeExtension(typeof(McpSettingsCatalog).Assembly.Location, ".xml");
            if (!File.Exists(xmlPath)) return new Dictionary<string, XmlDocumentation>();
            var document = XDocument.Load(xmlPath);
            var members = document.Descendants("member")
                .Where(x => x.Attribute("name") is not null)
                .ToDictionary(x => x.Attribute("name")!.Value, StringComparer.Ordinal);
            var result = new Dictionary<string, XmlDocumentation>(StringComparer.Ordinal);
            foreach (var (name, element) in members)
            {
                var summary = NormalizeXmlText(element.Element("summary")?.Value);
                var source = "xml-summary";
                if (string.IsNullOrWhiteSpace(summary) && element.Element("inheritdoc")?.Attribute("cref")?.Value is
                                                           { } cref
                                                       && members.TryGetValue(cref, out var inherited))
                {
                    summary = NormalizeXmlText(inherited.Element("summary")?.Value);
                    source = "xml-inheritdoc";
                }

                result[name] = new XmlDocumentation(summary, string.IsNullOrWhiteSpace(summary) ? "inferred" : source);
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, XmlDocumentation>();
        }
    }

    private static string NormalizeXmlText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : WhitespaceRegex().Replace(value.Trim(), " ");

    private static string Humanize(string value) => PascalBoundaryRegex().Replace(value, "$1 $2");

    private static string TranslatePropertyName(string value)
    {
        var tokens = NameTokenRegex().Matches(value).Select(x => x.Value).ToArray();
        return string.Join(' ', tokens.Select(x => NameTranslations.GetValueOrDefault(x, x)));
    }

    private static IReadOnlyDictionary<string, string> GetEnumDescriptions(Type enumType) =>
        Enum.GetNames(enumType).ToDictionary(
            x => x,
            x => enumType.GetField(x)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? x,
            StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex PascalBoundaryRegex();

    [GeneratedRegex("[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex NameTokenRegex();

    private sealed record XmlDocumentation(string Text, string Source);
}

public sealed record McpSettingSection(
    string Name,
    string Description,
    int SettingCount,
    int WritableCount,
    int SensitiveCount);

public sealed record McpSettingEntry(
    string Path,
    string Section,
    string Property,
    string ValueType,
    string Description,
    string DescriptionSource,
    bool Writable,
    bool Sensitive,
    bool Collection,
    IReadOnlyList<string>? AllowedValues,
    IReadOnlyDictionary<string, string>? AllowedValueDescriptions,
    object? AttributeDefault,
    object? Minimum,
    object? Maximum,
    object? CurrentValue,
    object? DefaultValue,
    Type ClrType);

public sealed record McpSettingChange
{
    [Description("bgi_search_settings 或 bgi_get_setting_details 返回的精确设置路径。")]
    public required string Path { get; init; }

    [Description("符合目标 valueType 的 JSON 值。")]
    public required JsonElement Value { get; init; }
}