using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

[McpServerToolType]
public sealed class McpConfigurationTools(McpApplicationServices application)
{
    private readonly McpSettingsCatalog _settingsCatalog = new();
    private static readonly string[] SensitiveNameParts =
    [
        "password", "token", "secret", "cookie", "authorization", "credential", "webhook", "accesskey", "privatekey",
        "devicekey", "sendkey", "apikey", "endpoint", "url",
    ];

    [McpServerTool(Name = "bgi_get_settings", ReadOnly = true, Idempotent = true), Description("读取 BetterGI 设置。默认遮蔽密码、Token、Cookie、Webhook 等敏感字段。")]
    public string GetSettings(
        [Description("可选的点分隔属性路径，例如 scriptConfig.autoUpdateSubscribedScripts；不区分大小写。")]
        string? path = null,
        [Description("是否返回敏感字段；设为 true 时还必须将 confirmSensitive 设为 true。")]
        bool includeSensitive = false,
        [Description("确认了解返回内容可能包含账号凭据。")]
        bool confirmSensitive = false)
    {
        if (includeSensitive && !confirmSensitive)
        {
            throw new InvalidOperationException("读取敏感设置需要将 confirmSensitive 设为 true。");
        }

        var configService = application.Services.GetRequiredService<IConfigService>();
        JsonNode node = JsonSerializer.SerializeToNode(configService.Get(), ConfigService.JsonOptions)
                        ?? throw new InvalidOperationException("设置序列化失败。");
        if (!string.IsNullOrWhiteSpace(path))
        {
            node = ResolveJsonPath(node, path)
                   ?? throw new ArgumentException($"设置路径不存在：{path}", nameof(path));
        }

        if (!includeSensitive)
        {
            Redact(node);
        }

        return node.ToJsonString(ConfigService.JsonOptions);
    }

    [McpServerTool(Name = "bgi_describe_settings", ReadOnly = true, Idempotent = true), Description("列出可通过 bgi_set_setting 修改的设置路径、CLR 类型和敏感性，不返回设置值。")]
    public IReadOnlyList<object> DescribeSettings(
        [Description("可选的路径或类型名称过滤词。")]
        string? filter = null)
    {
        var configService = application.Services.GetRequiredService<IConfigService>();
        var rows = _settingsCatalog.Build(configService.Get())
            .Select(ToMetadataOnly)
            .ToArray();
        return string.IsNullOrWhiteSpace(filter) ? rows : rows
            .Where(x => x.ToString()?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
    }

    [McpServerTool(Name = "bgi_list_setting_sections", ReadOnly = true, Idempotent = true), Description("列出 BetterGI 全部设置分区的中文用途、设置项数量、可写数量和敏感项数量。面对大量设置时应先调用本工具选择分区。")]
    public IReadOnlyList<McpSettingSection> ListSettingSections()
    {
        var configService = application.Services.GetRequiredService<IConfigService>();
        return _settingsCatalog.BuildSections(configService.Get());
    }

    [McpServerTool(Name = "bgi_search_settings", ReadOnly = true, Idempotent = true), Description("结构化搜索 BetterGI 设置。支持多检索词 AND/OR、分区、路径前缀、值类型、可写性和分页；返回中文说明、默认值、枚举候选及可选当前值。不要只靠单个模糊关键词猜设置。")]
    public object SearchSettings(
        [Description("零到多个检索词，搜索路径、属性名、中文说明、类型和枚举候选。")]
        string[]? terms = null,
        [Description("all 表示所有词都必须命中；any 表示至少一个命中。")]
        string matchMode = "all",
        [Description("可选精确分区名，来自 bgi_list_setting_sections，例如 autoDomainConfig。")]
        string? section = null,
        [Description("可选路径前缀，例如 autoDomainConfig.resin。")]
        string? pathPrefix = null,
        [Description("可选值类型过滤，例如 boolean、string、integer、number、enum。")]
        string? valueType = null,
        [Description("true 时只返回可由 bgi_set_setting/bgi_update_settings 修改的项。")]
        bool writableOnly = true,
        [Description("是否附带当前值；默认 false，避免搜索结果过大。")]
        bool includeCurrentValues = false,
        [Description("返回敏感当前值。必须同时 confirmSensitive=true。")]
        bool includeSensitive = false,
        [Description("确认搜索结果可能包含凭据。")]
        bool confirmSensitive = false,
        [Description("1 开始页码。")]
        int page = 1,
        [Description("每页 1-100 项，默认 30。")]
        int pageSize = 30)
    {
        ValidateSensitiveAccess(includeSensitive, confirmSensitive);
        if (matchMode is not ("all" or "any")) throw new ArgumentException("matchMode 必须是 all 或 any。", nameof(matchMode));
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedTerms = (terms ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var config = application.Services.GetRequiredService<IConfigService>().Get();
        var candidates = _settingsCatalog.Build(config)
            .Where(x => !writableOnly || x.Writable)
            .Where(x => string.IsNullOrWhiteSpace(section) || x.Section.Equals(section, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(pathPrefix) || x.Path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(valueType) || x.ValueType.Contains(valueType, StringComparison.OrdinalIgnoreCase))
            .Select(x => new { Entry = x, Score = SettingSearchScore(x, normalizedTerms, matchMode) })
            .Where(x => normalizedTerms.Length == 0 || x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var total = candidates.Length;
        var items = candidates.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => ToPublicSetting(x.Entry, includeCurrentValues, includeSensitive))
            .ToArray();
        return new
        {
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            filters = new { terms = normalizedTerms, matchMode, section, pathPrefix, valueType, writableOnly },
            items,
            nextStep = "对候选路径调用 bgi_get_setting_details；确认后再用 bgi_set_setting 或 bgi_update_settings。",
        };
    }

    [McpServerTool(Name = "bgi_get_setting_details", ReadOnly = true, Idempotent = true), Description("按精确路径批量读取设置详情。返回中文说明来源、当前值、默认值、CLR/JSON 类型、枚举候选、范围、可写性和敏感性；最多 50 项。")]
    public IReadOnlyList<object> GetSettingDetails(
        [Description("一到五十个由 bgi_search_settings 返回的精确路径。")]
        string[] paths,
        [Description("是否返回敏感值；必须同时 confirmSensitive=true。")]
        bool includeSensitive = false,
        [Description("确认结果可能包含凭据。")]
        bool confirmSensitive = false)
    {
        ValidateSensitiveAccess(includeSensitive, confirmSensitive);
        if (paths.Length is < 1 or > 50) throw new ArgumentException("paths 数量必须在 1-50。", nameof(paths));
        var entries = _settingsCatalog.Build(application.Services.GetRequiredService<IConfigService>().Get())
            .ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        return paths.Select(path => entries.TryGetValue(path, out var entry)
                ? ToPublicSetting(entry, includeCurrentValue: true, includeSensitive)
                : throw new ArgumentException($"设置路径不存在或不是可直接赋值的叶子项：{path}", nameof(paths)))
            .ToArray();
    }

    [McpServerTool(Name = "bgi_update_settings", Destructive = true), Description("批量预检或修改多个 BetterGI 设置。所有路径和值会先在配置副本上完成类型校验，再在 UI 线程应用并只主动保存一次；默认 dryRun=true。")]
    public async Task<object> UpdateSettings(
        [Description("设置变更数组，每项包含精确 path 和 JSON value；最多 100 项，路径不能重复。")]
        McpSettingChange[] changes,
        [Description("true 只校验并返回变更计划，不修改；建议始终先预检。")]
        bool dryRun = true,
        [Description("dryRun=false 时必须明确设为 true。")]
        bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (changes.Length is < 1 or > 100) throw new ArgumentException("changes 数量必须在 1-100。", nameof(changes));
        if (changes.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Length)
            throw new ArgumentException("changes 中不能包含重复路径。", nameof(changes));
        if (!dryRun && !confirm) throw new InvalidOperationException("实际批量修改需要 dryRun=false 且 confirm=true。");

        var configService = application.Services.GetRequiredService<IConfigService>();
        var current = configService.Get();
        var catalog = _settingsCatalog.Build(current).ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var clone = JsonSerializer.Deserialize<AllConfig>(
            JsonSerializer.Serialize(current, ConfigService.JsonOptions), ConfigService.JsonOptions)
            ?? throw new InvalidOperationException("无法创建配置校验副本。");
        var plans = new List<SettingChangePlan>();
        foreach (var change in changes)
        {
            if (!catalog.TryGetValue(change.Path, out var entry) || !entry.Writable)
                throw new ArgumentException($"设置路径不存在或不可写：{change.Path}", nameof(changes));
            var (cloneOwner, cloneProperty) = McpSettingsCatalog.ResolveProperty(clone, change.Path);
            var parsed = DeserializeSettingValue(change.Value, cloneProperty.PropertyType, change.Path);
            cloneProperty.SetValue(cloneOwner, parsed);
            plans.Add(new SettingChangePlan(change.Path, entry, parsed));
        }

        var publicPlan = plans.Select(x => new
        {
            x.Path,
            x.Entry.ValueType,
            x.Entry.Description,
            previousValue = McpSettingsCatalog.SerializeValue(x.Entry.CurrentValue, x.Entry.ClrType, x.Entry.Sensitive, false),
            newValue = McpSettingsCatalog.SerializeValue(x.NewValue, x.Entry.ClrType, x.Entry.Sensitive, false),
        }).ToArray();
        if (dryRun) return new { valid = true, applied = false, changes = publicPlan };

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previousCallback = current.OnAnyChangedAction;
            var rollback = new List<(object Owner, PropertyInfo Property, object? Value)>();
            current.OnAnyChangedAction = null;
            try
            {
                foreach (var plan in plans)
                {
                    var (owner, property) = McpSettingsCatalog.ResolveProperty(current, plan.Path);
                    rollback.Add((owner, property, property.GetValue(owner)));
                    property.SetValue(owner, plan.NewValue);
                }
                configService.Save();
            }
            catch
            {
                foreach (var item in rollback.AsEnumerable().Reverse())
                {
                    item.Property.SetValue(item.Owner, item.Value);
                }
                configService.Save();
                throw;
            }
            finally
            {
                current.OnAnyChangedAction = previousCallback;
            }
        }).Task;
        return new { valid = true, applied = true, saved = true, changes = publicPlan };
    }

    private static object ToMetadataOnly(McpSettingEntry entry) => new
    {
        entry.Path,
        entry.Section,
        entry.Property,
        entry.ValueType,
        entry.Description,
        entry.DescriptionSource,
        entry.Writable,
        entry.Sensitive,
        entry.Collection,
        entry.AllowedValues,
        entry.AllowedValueDescriptions,
        entry.Minimum,
        entry.Maximum,
    };

    private static object ToPublicSetting(McpSettingEntry entry, bool includeCurrentValue, bool includeSensitive) => new
    {
        entry.Path,
        entry.Section,
        entry.Property,
        entry.ValueType,
        clrType = entry.ClrType.FullName,
        entry.Description,
        entry.DescriptionSource,
        entry.Writable,
        entry.Sensitive,
        entry.Collection,
        entry.AllowedValues,
        entry.AllowedValueDescriptions,
        entry.Minimum,
        entry.Maximum,
        currentValue = includeCurrentValue
            ? McpSettingsCatalog.SerializeValue(entry.CurrentValue, entry.ClrType, entry.Sensitive, includeSensitive)
            : null,
        defaultValue = McpSettingsCatalog.SerializeValue(entry.DefaultValue, entry.ClrType, entry.Sensitive, includeSensitive),
    };

    private static int SettingSearchScore(McpSettingEntry entry, IReadOnlyList<string> terms, string matchMode)
    {
        if (terms.Count == 0) return 0;
        var fields = new[]
        {
            entry.Path,
            entry.Section,
            entry.Property,
            entry.Description,
            entry.ValueType,
            entry.AllowedValues is null ? string.Empty : string.Join(' ', entry.AllowedValues),
        };
        var matches = terms.Select(term => fields.Any(x => x.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matchMode == "all" ? matches.Any(x => !x) : matches.All(x => !x)) return -1;

        var score = 0;
        foreach (var term in terms)
        {
            if (entry.Path.Equals(term, StringComparison.OrdinalIgnoreCase)) score += 100;
            else if (entry.Path.EndsWith('.' + term, StringComparison.OrdinalIgnoreCase)) score += 50;
            else if (entry.Property.Equals(term, StringComparison.OrdinalIgnoreCase)) score += 45;
            else if (entry.Path.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 25;
            if (entry.Description.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 12;
            if (entry.AllowedValues?.Any(x => x.Contains(term, StringComparison.OrdinalIgnoreCase)) == true) score += 8;
        }
        return score;
    }

    private static object? DeserializeSettingValue(JsonElement value, Type propertyType, string path)
    {
        try
        {
            var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (underlying.IsEnum && value.ValueKind == JsonValueKind.String)
            {
                return Enum.Parse(underlying, value.GetString()!, ignoreCase: true);
            }
            var parsed = JsonSerializer.Deserialize(value.GetRawText(), propertyType, ConfigService.JsonOptions);
            if (parsed is null && propertyType.IsValueType && Nullable.GetUnderlyingType(propertyType) is null)
                throw new ArgumentException($"设置 {path} 不接受 null。");
            return parsed;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new ArgumentException($"设置 {path} 的值无法转换为 {propertyType.FullName}：{ex.Message}", nameof(value), ex);
        }
    }

    private static void ValidateSensitiveAccess(bool includeSensitive, bool confirmSensitive)
    {
        if (includeSensitive && !confirmSensitive)
            throw new InvalidOperationException("返回敏感值需要同时将 confirmSensitive 设为 true。");
    }

    private sealed record SettingChangePlan(string Path, McpSettingEntry Entry, object? NewValue);

    [McpServerTool(Name = "bgi_set_setting", Destructive = true), Description("按点分隔属性路径修改一个 BetterGI 设置，并立即写入 User/config.json。")]
    public async Task<object> SetSetting(
        [Description("设置属性路径，例如 scriptConfig.autoUpdateSubscribedScripts。")]
        string path,
        [Description("与目标属性类型兼容的 JSON 值。")]
        JsonElement value,
        [Description("必须明确设为 true，表示确认修改并持久化设置。")]
        bool confirm,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("修改设置需要将 confirm 设为 true。");
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("设置路径不能为空。", nameof(path));
        }

        var configService = application.Services.GetRequiredService<IConfigService>();
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (owner, property) = McpSettingsCatalog.ResolveProperty(configService.Get(), path);
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                throw new InvalidOperationException($"设置属性 {property.Name} 标记为 JsonIgnore，不能通过 MCP 持久化修改。");
            }

            var oldValue = property.GetValue(owner);
            var newValue = DeserializeSettingValue(value, property.PropertyType, path);

            property.SetValue(owner, newValue);
            configService.Save();
            return (object)new
            {
                path,
                propertyType = property.PropertyType.FullName,
                previousValue = McpSettingsCatalog.IsSensitive(path) ? "***REDACTED***" : oldValue,
                currentValue = McpSettingsCatalog.IsSensitive(path) ? "***REDACTED***" : newValue,
                saved = true,
            };
        }).Task;
    }

    private static (object Owner, PropertyInfo Property) ResolveProperty(object root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("设置路径不能为空。", nameof(path));
        }

        object owner = root;
        for (var i = 0; i < parts.Length; i++)
        {
            var property = owner.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(x => x.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"设置路径不存在：{string.Join('.', parts.Take(i + 1))}", nameof(path));
            if (i == parts.Length - 1)
            {
                if (!property.CanWrite)
                {
                    throw new InvalidOperationException($"设置属性 {property.Name} 是只读的。");
                }
                return (owner, property);
            }

            owner = property.GetValue(owner)
                    ?? throw new InvalidOperationException($"设置路径中间属性 {property.Name} 为 null。");
        }

        throw new UnreachableException();
    }

    private static JsonNode? ResolveJsonPath(JsonNode root, string path)
    {
        JsonNode? current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is not JsonObject obj)
            {
                return null;
            }
            current = obj.FirstOrDefault(x => x.Key.Equals(part, StringComparison.OrdinalIgnoreCase)).Value;
        }
        return current;
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (IsSensitive(property.Key))
                {
                    obj[property.Key] = "***REDACTED***";
                }
                else
                {
                    Redact(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                Redact(child);
            }
        }
    }

    private static bool IsSensitive(string name) =>
        SensitiveNameParts.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static void DescribeObject(Type type, string prefix, List<object> rows, HashSet<Type> ancestors, int depth)
    {
        if (depth > 12 || !ancestors.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(x => x.GetMethod is not null && x.GetIndexParameters().Length == 0))
        {
            var path = string.IsNullOrEmpty(prefix)
                ? JsonNamingPolicy.CamelCase.ConvertName(property.Name)
                : $"{prefix}.{JsonNamingPolicy.CamelCase.ConvertName(property.Name)}";
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var isLeaf = propertyType.IsPrimitive
                         || propertyType.IsEnum
                         || propertyType == typeof(string)
                         || propertyType == typeof(decimal)
                         || propertyType == typeof(DateTime)
                         || propertyType == typeof(DateTimeOffset)
                         || typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType);
            if (isLeaf)
            {
                rows.Add(new
                {
                    path,
                    type = property.PropertyType.FullName,
                    writable = property.SetMethod is not null && property.GetCustomAttribute<JsonIgnoreAttribute>() is null,
                    sensitive = IsSensitive(property.Name),
                });
            }
            else
            {
                DescribeObject(propertyType, path, rows, new HashSet<Type>(ancestors), depth + 1);
            }
        }
    }
}
