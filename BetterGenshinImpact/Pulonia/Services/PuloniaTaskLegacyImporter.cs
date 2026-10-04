using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service;
using Newtonsoft.Json.Linq;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>一次性转换旧配置组、一条龙及 d-v3 数据；只产生预览，不调用旧执行引擎。</summary>
public sealed class PuloniaTaskLegacyImporter
{
    /// <summary>新引擎注册的能力及参数约束。</summary>
    private readonly IReadOnlyList<PuloniaTaskDefinition> _definitions;
    /// <summary>复用运行系统的资源版本范围。</summary>
    private readonly PuloniaTaskResourceVersionService _versions;
    /// <summary>旧配置使用 System.Text.Json，保留其命名、枚举和忽略约定。</summary>
    private static readonly JsonSerializerOptions LegacyOptions = new(ConfigService.JsonOptions) { PropertyNameCaseInsensitive = true };

    /// <summary>建立独立导入器，不订阅全局状态。</summary>
    public PuloniaTaskLegacyImporter(IReadOnlyList<PuloniaTaskDefinition> definitions, PuloniaTaskResourceCatalog catalog)
    {
        _definitions = definitions;
        _versions = new PuloniaTaskResourceVersionService(catalog);
    }

    /// <summary>有限读取源文件并完整转换，单个文件失败只加入差异报告，不生成半份计划。</summary>
    public async Task<PuloniaTaskImportResult> PrepareAsync(IReadOnlyList<string> files, AllConfig config, CancellationToken ct = default)
    {
        if (files.Count > 100) throw new InvalidOperationException("一次最多导入 100 个文件。");
        var result = new PuloniaTaskImportResult();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var presetStart = result.Presets.Count;
            try
            {
                var json = await ReadSourceAsync(file, result, ct).ConfigureAwait(false);
                var data = PuloniaTaskJson.Read<JObject>(json);
                var name = Value<string>(data, "name") ?? Path.GetFileNameWithoutExtension(file);
                var plan = new PuloniaTaskPlan { Name = name, RootTask = new PuloniaTask { Name = name }, Description = "导入自 " + Path.GetFullPath(file) };
                if (Get(data, "root_task") is JObject root)
                {
                    plan.RootTask = ConvertGear(root, result, file + "/root_task", 0, refCount: new int[1]);
                    if (plan.RootTask.TaskType != "group" || plan.RootTask.Source is not null)
                        plan.RootTask = new PuloniaTask { Name = name, Children = [plan.RootTask] };
                    plan.RootTask.RepeatCount = null;
                    ApplyGroupConfig(Get(data, "group_config") as JObject, plan.RootTask, result, file);
                    if (data.Properties().Any(p => Normalize(p.Name).Contains("trigger") || Normalize(p.Name).Contains("schedule")))
                        result.Differences.Add(file + "：d-v3 的 Quartz 调度不自动启用，请在触发方式中重建并核对。");
                }
                else if (Get(data, "projects") is JArray)
                    plan.RootTask = ConvertGroup(data, result, file);
                else if (Get(data, "task_enabled_list") is JObject)
                    await ConvertDragonAsync(json, plan, config, result, file, ct).ConfigureAwait(false);
                else throw new InvalidOperationException("未识别为旧配置组、一条龙或 d-v3 root_task 格式。");
                // 名称重复可保留，UI 在提交前添加后缀；身份由稳定新 ID 决定。
                CheckNodeCount(plan.RootTask);
                await FreezeResourcesAsync(plan.RootTask, result, file, ct).ConfigureAwait(false);
                PuloniaTaskValidator.ValidatePlan(plan);
                result.Plans.Add(plan);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Presets.RemoveRange(presetStart, result.Presets.Count - presetStart);
                result.Differences.Add($"{file}：未导入，{ex.Message}");
            }
        }
        return result;
    }

    /// <summary>限制文件大小并记录内容指纹，以便定位导入依据。</summary>
    private static async Task<string> ReadSourceAsync(string file, PuloniaTaskImportResult result, CancellationToken ct)
    {
        if (new FileInfo(file).Length > 8 * 1024 * 1024) throw new IOException("旧配置超过 8 MiB 限制。");
        var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
        result.Sources.Add(Path.GetFullPath(file) + " · SHA256 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
        return json;
    }

    /// <summary>旧配置组转换为可编辑分组，项目顺序沿用旧 Index 排序。</summary>
    private PuloniaTask ConvertGroup(JObject data, PuloniaTaskImportResult result, string location)
    {
        var group = new PuloniaTask { Name = Value<string>(data, "name") ?? "导入配置组" };
        ApplyGroupConfig(Get(data, "config") as JObject, group, result, location);
        var projects = Get(data, "projects") as JArray ?? throw new InvalidOperationException("缺少 projects。");
        if (projects.Count > 10000) throw new InvalidOperationException("配置组超过一万个节点。");
        if (projects.Any(p => p is not JObject)) throw new InvalidOperationException("projects 含非对象项，不能丢弃部分任务。");
        foreach (var project in projects.OfType<JObject>().OrderBy(p => Value<int?>(p, "index") ?? 0))
        {
            var type = (Value<string>(project, "type") ?? "").ToLowerInvariant();
            var name = Value<string>(project, "name") ?? "导入任务";
            var node = new PuloniaTask { TaskType = type, Name = name, IsEnabled = Value<string>(project, "status") != "Disabled" };
            var folder = Value<string>(project, "folder_name") ?? "";
            switch (type)
            {
                case "javascript":
                    node.Path = folder;
                    node.Parameters["settings"] = Get(project, "js_script_settings_object")?.DeepClone() ?? new JObject();
                    result.Differences.Add(location + "/" + name + "：JS HTTP / 通知许可使用新宿主规则，请核对脚本权限。");
                    break;
                case "pathing": node.Path = Path.Combine(folder, name); break;
                case "keymouse": node.Path = name; break;
                case "shell": SetShellCommand(node, name); break;
                default: Unsupported(node, project, result, location + "/" + name, "未知任务类型 " + type); break;
            }
            var schedule = Value<string>(project, "schedule");
            if (!string.IsNullOrEmpty(schedule))
            {
                // 旧周期是项目筛选/完成跳过规则，不能等价转换成主动调度触发器。
                node.IsEnabled = false;
                result.Differences.Add(location + "/" + name + "：旧周期 " + schedule + " 已保留在报告，节点暂时禁用；核对运行日程后启用。旧成功记录不计入新 CD。");
            }
            var count = Value<int?>(project, "run_num") ?? 1;
            if (count is < 1 or > 10000) throw new InvalidOperationException("执行次数超出 1—10000：" + name);
            group.Children.Add(count == 1 ? node : new PuloniaTask { Name = name + "（重复）", RepeatCount = count, Children = [node] });
        }
        CheckNodeCount(group);
        return group;
    }

    /// <summary>提取旧分组可表达的公共参数，旧调度字段单独列出。</summary>
    private static void ApplyGroupConfig(JObject? data, PuloniaTask group, PuloniaTaskImportResult result, string location)
    {
        if (data is null) return;
        var config = JsonSerializer.Deserialize<ScriptGroupConfig>(data.ToString(), LegacyOptions) ?? throw new InvalidOperationException("配置组设置无法解析。");
        foreach (var type in new[] { "pathing", "javascript", "shell" })
            group.ParameterOverrides.Add(new PuloniaTaskParameterOverride { TaskType = type, Values = PuloniaTaskCommonSettings.ToParameters(type, config) });
        result.Differences.Add(location + "：行走、战斗、食物与 Shell 设置已转为分组公共配置；旧任务周期、完成跳过、执行优先级及断点不迁入。");
    }

    /// <summary>递归迁移 d-v3 树，保留父子开关、顺序和展开状态。</summary>
    private PuloniaTask ConvertGear(JObject data, PuloniaTaskImportResult result, string location, int depth, int[] refCount)
    {
        if (depth > 64 || ++refCount[0] > 10000) throw new InvalidOperationException("d-v3 树超过层级或数量限制。");
        var type = (Value<string>(data, "task_type") ?? "group").ToLowerInvariant();
        var raw = Get(data, "parameters");
        JObject parameters;
        try { parameters = raw is JObject obj ? (JObject)obj.DeepClone() : string.IsNullOrWhiteSpace(raw?.Value<string>()) ? new JObject() : PuloniaTaskJson.Read<JObject>(raw!.Value<string>()!); }
        catch (Exception) { parameters = new JObject { ["raw"] = raw?.DeepClone() }; }
        var node = new PuloniaTask { Name = Value<string>(data, "name") ?? "导入任务", TaskType = type,
            IsEnabled = Value<bool?>(data, "is_enabled") ?? true, IsExpanded = Value<bool?>(data, "is_expanded") ?? true,
            Path = Value<string>(data, "path") };
        if (type is "group" or "root") node.TaskType = "group";
        else if (type == "javascript")
        {
            node.Path = Value<string>(parameters, "folder_name") ?? node.Path;
            node.Parameters["settings"] = Get(parameters, "context")?.DeepClone() ?? new JObject();
        }
        else if (type is "pathing" or "keymouse")
            node.Path = Value<string>(parameters, "path") ?? Value<string>(parameters, "file_path") ?? Value<string>(parameters, "raw") ?? node.Path;
        else if (type == "shell")
        {
            if (string.IsNullOrWhiteSpace(node.Path)) Unsupported(node, data, result, location, "旧 Shell 未明确保存命令");
            else SetShellCommand(node, node.Path);
        }
        else Unsupported(node, data, result, location, "反射方法或未知类型没有显式注册映射");
        if (type is "javascript" or "pathing" && Get(parameters, "pathing_party_config") is JObject party)
        {
            var config = JsonSerializer.Deserialize<PathingPartyConfig>(party.ToString(), LegacyOptions)!;
            foreach (var field in PuloniaTaskCommonSettings.ToParameters(type, new ScriptGroupConfig { PathingConfig = config }).Properties())
                node.Parameters[field.Name] = field.Value.DeepClone();
        }
        if (Value<bool?>(data, "is_directory") == true && type is "pathing" or "keymouse")
        {
            var definition = _definitions.First(d => d.TaskType == type);
            node.TaskType = "group";
            node.Source = new PuloniaTaskSource { Kind = "directory", TaskType = type, Path = Path.GetFullPath(node.Path!, definition.ResourceBaseDirectory!) };
            if (node.Parameters.Count > 0) node.ParameterOverrides.Add(new PuloniaTaskParameterOverride { TaskType = type, Values = node.Parameters });
            node.Parameters = new JObject(); node.Path = null;
        }
        if (Get(data, "children") is JArray children && children.Count > 0)
        {
            if (children.Any(child => child is not JObject)) throw new InvalidOperationException("children 含非对象项，不能只导入部分树。");
            if (node.TaskType != "group" || node.Source is not null) Unsupported(node, data, result, location, "叶子或目录引用含可编辑子项");
            else foreach (var child in children.OfType<JObject>()) node.Children.Add(ConvertGear(child, result, location + "/" + node.Name, depth + 1, refCount));
        }
        var known = new HashSet<string>(new[] { "foldername", "context", "pathingpartyconfig", "path", "filepath", "raw" });
        if (parameters.Properties().Any(p => !known.Contains(Normalize(p.Name))))
        {
            Unsupported(node, data, result, location, "含无法映射的参数：" + string.Join("、", parameters.Properties().Where(p => !known.Contains(Normalize(p.Name))).Select(p => p.Name)));
        }
        return node;
    }

    /// <summary>将旧一条龙转成计划；被隐式借用的功能设置提取为稳定预设。</summary>
    private async Task ConvertDragonAsync(string json, PuloniaTaskPlan plan, AllConfig config, PuloniaTaskImportResult result, string location, CancellationToken ct)
    {
        var dragon = PuloniaTaskJson.Read<OneDragonFlowConfig>(json);
        var order = dragon.TaskOrder.Count > 0 ? dragon.TaskOrder : dragon.TaskEnabledList.Keys.ToList();
        if (order.Count > 10000) throw new InvalidOperationException("旧一条龙超过一万个步骤。");
        var nodeCount = 1;
        foreach (var key in order)
        {
            if (!dragon.TaskEnabledList.TryGetValue(key, out var enabled)) continue;
            var name = dragon.TaskDefinitions.Count == 0 ? key : dragon.TaskDefinitions.GetValueOrDefault(key) ?? key;
            var node = new PuloniaTask { Name = name, IsEnabled = enabled };
            JObject? values = null;
            switch (name)
            {
                case "领取邮件": node.TaskType = "builtin.claim_mail"; break;
                case "合成树脂": node.TaskType = "builtin.craft_condensed_resin"; node.Parameters["country"] = dragon.CraftingBenchCountry; break;
                case "领取每日奖励": node.TaskType = "builtin.daily_rewards"; node.Parameters = new JObject { ["country"] = dragon.AdventurersGuildCountry, ["party_name"] = dragon.DailyRewardPartyName }; break;
                case "领取尘歌壶奖励": node.TaskType = "builtin.serenitea_pot_rewards"; break;
                case "自动秘境":
                    node.TaskType = "builtin.auto_domain"; values = PuloniaCombatTaskSettings.ToParameters(config.AutoDomainConfig);
                    values["party_name"] = dragon.PartyName; values["domain_name"] = dragon.DomainName;
                    values["sunday_selected_value"] = dragon.SundayEverySelectedValue;
                    values["strategy_name"] = string.IsNullOrEmpty(config.AutoFightConfig.StrategyName) ? "根据队伍自动选择" : config.AutoFightConfig.StrategyName;
                    values["max_artifact_star"] = config.AutoArtifactSalvageConfig.MaxArtifactStar;
                    if (dragon.WeeklyDomainEnabled) AddDragonWeek(values, JObject.FromObject(dragon), domain: true);
                    break;
                case "自动首领讨伐":
                    node.TaskType = "builtin.auto_boss";
                    var boss = new AutoBossConfig { BossName = dragon.AutoBossName, StrategyName = dragon.AutoBossStrategyName, TeamName = dragon.AutoBossTeamName,
                        SpecifyRunCount = dragon.AutoBossSpecifyRunCount, RunCount = dragon.AutoBossRunCount, UseTransientResin = dragon.AutoBossUseTransientResin,
                        UseFragileResin = dragon.AutoBossUseFragileResin, ReviveRetryCount = dragon.AutoBossReviveRetryCount, ReturnToStatueAfterEachRound = dragon.AutoBossReturnToStatueAfterEachRound,
                        RewardRecognitionEnabled = dragon.AutoBossRewardRecognitionEnabled, Timeout = dragon.AutoBossTimeout };
                    values = PuloniaCombatTaskSettings.ToParameters(boss);
                    values["fight_config"] = PuloniaCombatTaskSettings.ToParameters(config.AutoFightConfig);
                    if (dragon.AutoBossTotalRunCountLimit > 0) { node.IsEnabled = false; result.Differences.Add(location + "/" + name + "：旧累计上限 / 已完成次数无账号证据，未迁入；节点暂时禁用。"); }
                    break;
                case "自动幽境危战":
                    node.TaskType = "builtin.auto_stygian"; values = PuloniaCombatTaskSettings.ToParameters(config.AutoStygianOnslaughtConfig);
                    values["max_artifact_star"] = config.AutoArtifactSalvageConfig.MaxArtifactStar; break;
                case "自动地脉花":
                    node.TaskType = "builtin.auto_ley_line";
                    var ley = PuloniaCombatTaskSettings.FromParameters<AutoLeyLineOutcropConfig>(PuloniaCombatTaskSettings.ToParameters(config.AutoLeyLineOutcropConfig));
                    if (string.IsNullOrWhiteSpace(ley.FightConfig.StrategyName)) ley.FightConfig.CopyFromAutoFightConfig(config.AutoFightConfig);
                    if (string.IsNullOrWhiteSpace(ley.FightConfig.StrategyName)) ley.FightConfig.StrategyName = "根据队伍自动选择";
                    values = PuloniaCombatTaskSettings.ToParameters(ley);
                    if (dragon.LeyLineRunCount > 0) values["count"] = dragon.LeyLineRunCount;
                    values["is_resin_exhaustion_mode"] = dragon.LeyLineResinExhaustionMode; values["open_mode_count_min"] = dragon.LeyLineOpenModeCountMin;
                    values["one_dragon_mode"] = dragon.LeyLineOneDragonMode;
                    AddDragonWeek(values, JObject.FromObject(dragon), domain: false); break;
                default:
                    var directory = Global.Absolute("User/ScriptGroup");
                    var source = Path.GetFullPath(Path.Combine(directory, name + ".json"));
                    if (!source.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("配置组名称越过来源目录。");
                    if (File.Exists(source)) { node = ConvertGroup(PuloniaTaskJson.Read<JObject>(await ReadSourceAsync(source, result, ct).ConfigureAwait(false)), result, source); node.IsEnabled = enabled; }
                    else Unsupported(node, new JObject { ["name"] = name }, result, location + "/" + name, "旧配置组不存在");
                    break;
            }
            if (values is not null)
            {
                var preset = new PuloniaTaskPreset { Name = plan.Name + " · " + name, TaskType = node.TaskType, Values = values };
                result.Presets.Add(preset); node.PresetId = preset.Id;
            }
            plan.RootTask.Children.Add(node);
            nodeCount += CheckNodeCount(node);
            if (nodeCount > 10000) throw new InvalidOperationException("旧一条龙展开配置组后超过一万个节点。");
        }
        if (!string.IsNullOrEmpty(dragon.NextTaskId)) result.Differences.Add(location + "：旧从此执行标记未当作新历史断点导入。");
        if (!string.IsNullOrEmpty(dragon.CompletionAction)) result.Differences.Add(location + "：完成后操作“" + dragon.CompletionAction + "”未自动执行，请显式配置后处理步骤。");
        result.Differences.Add(location + "：秘境、首领、幽境和地脉花的隐式业务配置已冻结为本次导入预设；旧领取流程结果仍需核验。");
    }

    /// <summary>转换周安排并保留四点日切及空值回退语义。</summary>
    private static void AddDragonWeek(JObject values, JObject dragon, bool domain)
    {
        var weekly = new JObject();
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var anyLeyDay = days.Any(d => dragon.Value<bool?>("LeyLineRun" + d) == true);
        for (var i = 0; i < 7; i++)
        {
            var day = days[i]; var entry = new JObject();
            if (domain)
            {
                Copy(day + "PartyName", "party_name"); Copy(day + "DomainName", "domain_name");
                var selected = dragon.Value<string>(day + "SelectedValue");
                entry["sunday_selected_value"] = string.IsNullOrWhiteSpace(selected) || selected == "0" ? dragon.Value<string>("SundayWeeklySelectedValue") : selected;
            }
            else
            {
                entry["enabled"] = !anyLeyDay || dragon.Value<bool?>("LeyLineRun" + day) == true;
                Copy("LeyLine" + day + "Type", "ley_line_outcrop_type"); Copy("LeyLine" + day + "Country", "country");
            }
            weekly[i.ToString()] = entry;
            void Copy(string source, string target) { var value = dragon.Value<string>(source); if (!string.IsNullOrWhiteSpace(value)) entry[target] = value; }
        }
        values["weekday_overrides"] = weekly;
    }

    /// <summary>读取当前资源指纹，失败时保留节点和参数并禁用。</summary>
    private async Task FreezeResourcesAsync(PuloniaTask node, PuloniaTaskImportResult result, string location, CancellationToken ct)
    {
        try
        {
            var version = await _versions.ReadCurrentVersionAsync(node, _definitions, ct).ConfigureAwait(false);
            if (node.Source?.Kind == "directory") node.Source.Version = version; else node.ResourceVersion = version;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            node.IsEnabled = false; result.Differences.Add(location + "/" + node.Name + "：资源不可用，节点暂时禁用，" + ex.Message);
        }
        foreach (var child in node.Children) await FreezeResourcesAsync(child, result, location + "/" + node.Name, ct).ConfigureAwait(false);
    }

    /// <summary>保留无法映射的原始数据，禁止误当作可执行能力。</summary>
    private static void Unsupported(PuloniaTask node, JObject raw, PuloniaTaskImportResult result, string location, string reason)
    {
        node.TaskType = "legacy.unsupported"; node.IsEnabled = false; node.Source = null; node.Children.Clear(); node.ParameterOverrides.Clear();
        node.Parameters = new JObject { ["legacy_data"] = raw.DeepClone() };
        result.Differences.Add(location + "：" + reason + "；原数据保留为禁用步骤。");
    }

    /// <summary>旧 Shell 字符串转换为明确的隐藏命令进程参数。</summary>
    private static void SetShellCommand(PuloniaTask node, string command) => node.Parameters = new JObject
        { ["file_name"] = "cmd.exe", ["arguments"] = new JArray("/d", "/c", command) };

    /// <summary>导入和展开前限制总节点数，避免先构造巨大计划再交给存储拒绝。</summary>
    private static int CheckNodeCount(PuloniaTask root)
    {
        var pending = new Stack<PuloniaTask>(); pending.Push(root); var count = 0;
        while (pending.TryPop(out var node))
        {
            if (++count > 10000) throw new InvalidOperationException("导入计划超过一万个节点。");
            foreach (var child in node.Children) pending.Push(child);
        }
        return count;
    }

    /// <summary>兼容旧文件的 CamelCase、PascalCase 与 snake_case 字段名。</summary>
    private static JToken? Get(JObject obj, string name) => obj.Properties().FirstOrDefault(p => Normalize(p.Name) == Normalize(name))?.Value;
    /// <summary>从旧对象读取可空字段，缺失值保持缺省语义。</summary>
    private static T? Value<T>(JObject obj, string name) => Get(obj, name) is { Type: not JTokenType.Null } token ? token.ToObject<T>() : default;
    /// <summary>字段名标准化仅用于旧格式识别，不改写用户参数名称。</summary>
    private static string Normalize(string name) => name.Replace("_", "").ToLowerInvariant();
}
