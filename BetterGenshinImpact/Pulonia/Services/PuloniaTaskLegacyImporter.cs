using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
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
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
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
            try
            {
                var json = await ReadSourceAsync(file, result, ct).ConfigureAwait(false);
                var data = PuloniaTaskJson.Read<JObject>(json);
                var name = Value<string>(data, "name") ?? Path.GetFileNameWithoutExtension(file);
                var plan = new PuloniaTaskPlan { Name = name, RootTask = new PuloniaTask { Name = name }, Description = "导入自 " + Path.GetFullPath(file) };
                if (Get(data, "root_task") is JObject root)
                {
                    plan.RootTask = ConvertGear(root, config, result, file + "/root_task", 0, refCount: new int[1]);
                    if (plan.RootTask.TaskType != "group" || plan.RootTask.Source is not null)
                        plan.RootTask = new PuloniaTask { Name = name, Children = [plan.RootTask] };
                    plan.RootTask.RepeatCount = null;
                    ApplyGroupConfig(Get(data, "group_config") as JObject, plan.RootTask, config, result, file);
                    if (data.Properties().Any(p => Normalize(p.Name).Contains("trigger") || Normalize(p.Name).Contains("schedule")))
                        result.Differences.Add(file + "：d-v3 的 Quartz 调度不自动启用，请在触发方式中重建并核对。");
                }
                else if (Get(data, "projects") is JArray)
                    plan.RootTask = ConvertGroup(data, config, result, file);
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
    private PuloniaTask ConvertGroup(JObject data, AllConfig config, PuloniaTaskImportResult result, string location)
    {
        var group = new PuloniaTask { Name = Value<string>(data, "name") ?? "导入配置组" };
        ApplyGroupConfig(Get(data, "config") as JObject, group, config, result, location);
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

    /// <summary>提取旧分组可表达的设置写入分组共享设置，旧调度字段单独列出。</summary>
    private static void ApplyGroupConfig(JObject? data, PuloniaTask group, AllConfig global, PuloniaTaskImportResult result, string location)
    {
        if (data is null) return;
        var config = JsonSerializer.Deserialize<ScriptGroupConfig>(data.ToString(), LegacyOptions) ?? throw new InvalidOperationException("配置组设置无法解析。");
        MergeSettings(group.Settings, ToSharedSettings(config, global, true, result, location));
        result.Differences.Add(location + "：行走、战斗、食物与 Shell 设置已转为分组共享设置（只保留与默认值不同的项）；旧任务周期、完成跳过、执行优先级及断点不迁入。");
    }

    /// <summary>旧配置组设置转为共享设置，转换说明以位置前缀写入差异报告。</summary>
    private static JObject ToSharedSettings(ScriptGroupConfig source, AllConfig global, bool includeShell,
        PuloniaTaskImportResult result, string location)
    {
        var notes = new List<string>();
        var settings = PuloniaLegacySettingsConverter.FromGroupConfig(source, global, includeShell, notes);
        result.Differences.AddRange(notes.Select(note => location + "：" + note));
        return settings;
    }

    /// <summary>把共享设置并入节点，已有同名项被覆盖。</summary>
    private static void MergeSettings(JObject target, JObject values)
    {
        foreach (var property in values.Properties())
            target[property.Name] = property.Value.DeepClone();
    }

    /// <summary>递归迁移 d-v3 树，保留父子开关和顺序；展开状态属于编辑器界面，不随计划导入。</summary>
    private PuloniaTask ConvertGear(JObject data, AllConfig global, PuloniaTaskImportResult result, string location, int depth, int[] refCount)
    {
        if (depth > 64 || ++refCount[0] > 10000) throw new InvalidOperationException("d-v3 树超过层级或数量限制。");
        var type = (Value<string>(data, "task_type") ?? "group").ToLowerInvariant();
        var raw = Get(data, "parameters");
        JObject parameters;
        try { parameters = raw is JObject obj ? (JObject)obj.DeepClone() : string.IsNullOrWhiteSpace(raw?.Value<string>()) ? new JObject() : PuloniaTaskJson.Read<JObject>(raw!.Value<string>()!); }
        catch (Exception) { parameters = new JObject { ["raw"] = raw?.DeepClone() }; }
        var node = new PuloniaTask { Name = Value<string>(data, "name") ?? "导入任务", TaskType = type,
            IsEnabled = Value<bool?>(data, "is_enabled") ?? true,
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
            // 行走设置写成节点共享设置；目录引用转为分组后，设置随节点保留在引用分组上。
            var config = JsonSerializer.Deserialize<PathingPartyConfig>(party.ToString(), LegacyOptions)!;
            MergeSettings(node.Settings, ToSharedSettings(new ScriptGroupConfig { PathingConfig = config }, global, false, result, location));
        }
        if (Value<bool?>(data, "is_directory") == true && type is "pathing" or "keymouse")
        {
            var definition = _definitions.First(d => d.TaskType == type);
            node.TaskType = "group";
            node.Source = new PuloniaTaskSource { Kind = "directory", TaskType = type, Path = Path.GetFullPath(node.Path!, definition.ResourceBaseDirectory!) };
            node.Parameters = new JObject(); node.Path = null;
        }
        if (Get(data, "children") is JArray children && children.Count > 0)
        {
            if (children.Any(child => child is not JObject)) throw new InvalidOperationException("children 含非对象项，不能只导入部分树。");
            if (node.TaskType != "group" || node.Source is not null) Unsupported(node, data, result, location, "叶子或目录引用含可编辑子项");
            else foreach (var child in children.OfType<JObject>()) node.Children.Add(ConvertGear(child, global, result, location + "/" + node.Name, depth + 1, refCount));
        }
        var known = new HashSet<string>(new[] { "foldername", "context", "pathingpartyconfig", "path", "filepath", "raw" });
        if (parameters.Properties().Any(p => !known.Contains(Normalize(p.Name))))
        {
            Unsupported(node, data, result, location, "含无法映射的参数：" + string.Join("、", parameters.Properties().Where(p => !known.Contains(Normalize(p.Name))).Select(p => p.Name)));
        }
        return node;
    }

    /// <summary>将旧一条龙转成计划；被隐式借用的功能设置按导入时的值写入任务参数。</summary>
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
            // 战斗类任务借用了其他页面的全局配置，按导入时的值固定；一条龙自己的字段同样直接写入节点参数
            JObject? values = null;
            switch (name)
            {
                case "领取邮件": node.TaskType = PuloniaSoloTaskExecutor.ToTaskType(ClaimMailRewardsTask.TaskId); break;
                case "合成树脂":
                    node.TaskType = PuloniaSoloTaskExecutor.ToTaskType(GoToCraftingBenchTask.TaskId);
                    node.Parameters = ToParameters(new CraftCondensedResinParam { Country = dragon.CraftingBenchCountry, MinResinToKeep = ResinUsageLegacyMapper.NormalizeMinResinToKeep(dragon.MinResinToKeep) });
                    result.Differences.Add(location + "/" + name + "：保留原粹树脂数取自本次导入的一条龙配置，运行时不再读取当前选中的一条龙配置。");
                    break;
                case "领取每日奖励":
                    node.TaskType = PuloniaSoloTaskExecutor.ToTaskType(ClaimDailyRewardsTask.TaskId);
                    node.Parameters = ToParameters(new DailyRewardsParam { Country = dragon.AdventurersGuildCountry, PartyName = dragon.DailyRewardPartyName });
                    break;
                case "领取尘歌壶奖励":
                    node.TaskType = PuloniaSoloTaskExecutor.ToTaskType(GoToSereniteaPotTask.TaskId);
                    node.Parameters = ToParameters(new SereniteaPotParam { TeleportType = dragon.SereniteaPotTpType, SecretTreasureObjects = [.. dragon.SecretTreasureObjects] });
                    result.Differences.Add(location + "/" + name + "：进壶方式与洞天百宝购买清单取自本次导入的一条龙配置，运行时不再读取当前选中的一条龙配置。");
                    break;
                case "自动秘境":
                    node.TaskType = PuloniaSoloTaskExecutor.ToTaskType(AutoDomainTask.TaskId);
                    values = ToParameters(CreateDomainParam(config, dragon));
                    // 新一条龙不支持按星期切换配置，旧的每周秘境配置不导入
                    if (dragon.WeeklyDomainEnabled)
                        result.Differences.Add(location + "/" + name + "：每周秘境配置未导入，新一条龙每天使用同一套秘境配置。");
                    break;
                case "自动首领讨伐":
                    node.TaskType = PuloniaSoloTaskExecutor.ToTaskType(AutoBossTask.TaskId);
                    values = ToParameters(CreateBossParam(config, dragon));
                    if (dragon.AutoBossTotalRunCountLimit > 0)
                    {
                        values[PuloniaSoloTaskExecutor.BossTotalRunCountLimitParameter] = dragon.AutoBossTotalRunCountLimit;
                        node.IsEnabled = false;
                        result.Differences.Add(location + "/" + name + "：累计上限已迁入；旧已完成次数无账号证据，未迁入，节点暂时禁用，请核对后启用。");
                    }
                    break;
                case "自动幽境危战":
                    node.TaskType = PuloniaSoloTaskExecutor.ToTaskType(AutoStygianOnslaughtTask.TaskId);
                    values = ToParameters(CreateStygianParam(config));
                    break;
                case "自动地脉花":
                    node.TaskType = PuloniaSoloTaskExecutor.ToTaskType(AutoLeyLineOutcropTask.TaskId);
                    values = ToParameters(CreateLeyLineParam(config, dragon));
                    // 新一条龙不支持按星期运行，旧的运行日期、类型和国家安排不导入
                    if (HasLeyLineWeekSettings(dragon))
                        result.Differences.Add(location + "/" + name + "：每周运行安排未导入，新一条龙每天执行并使用同一套地脉花类型与国家。");
                    break;
                default:
                    var directory = Global.Absolute("User/ScriptGroup");
                    var source = Path.GetFullPath(Path.Combine(directory, name + ".json"));
                    if (!source.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("配置组名称越过来源目录。");
                    if (File.Exists(source)) { node = ConvertGroup(PuloniaTaskJson.Read<JObject>(await ReadSourceAsync(source, result, ct).ConfigureAwait(false)), config, result, source); node.IsEnabled = enabled; }
                    else Unsupported(node, new JObject { ["name"] = name }, result, location + "/" + name, "旧配置组不存在");
                    break;
            }
            if (values is not null)
                node.Parameters = values;
            plan.RootTask.Children.Add(node);
            nodeCount += CheckNodeCount(node);
            if (nodeCount > 10000) throw new InvalidOperationException("旧一条龙展开配置组后超过一万个节点。");
        }
        if (!string.IsNullOrEmpty(dragon.NextTaskId)) result.Differences.Add(location + "：旧从此执行标记未当作新历史断点导入。");
        if (!string.IsNullOrEmpty(dragon.CompletionAction)) result.Differences.Add(location + "：完成后操作“" + dragon.CompletionAction + "”未自动执行，请显式配置后处理步骤。");
        result.Differences.Add(location + "：秘境、首领、幽境和地脉花的隐式业务配置已按导入时的全局配置写入各任务参数，战斗策略按名称保存并在运行时解析；旧领取流程结果仍需核验。");
    }

    /// <summary>按独立任务参数合同（SoloTaskJson）把强类型参数转换为节点使用的参数 JSON。</summary>
    private static JObject ToParameters<T>(T param) => PuloniaTaskJson.Read<JObject>(SoloTaskJson.Serialize(param));

    /// <summary>
    /// 自动秘境：业务设置来自导入时的秘境配置，队伍、秘境和周日奖励来自一条龙；
    /// 树脂设置复制迁移后的统一模型，策略保存名称，运行时再解析为路径。
    /// </summary>
    private static AutoDomainParam CreateDomainParam(AllConfig config, OneDragonFlowConfig dragon)
    {
        var domain = config.AutoDomainConfig;
        var strategy = string.IsNullOrEmpty(config.AutoFightConfig.StrategyName) ? CombatStrategyResolver.AutoSelectName : config.AutoFightConfig.StrategyName;
        // 内部构造不加载全局业务默认值，全部字段在这里显式填写
        var param = new AutoDomainParam(strategy)
        {
            PartyName = dragon.PartyName,
            DomainName = dragon.DomainName,
            SundaySelectedValue = dragon.SundayEverySelectedValue,
            AutoArtifactSalvage = domain.AutoArtifactSalvage,
            MaxArtifactStar = config.AutoArtifactSalvageConfig.MaxArtifactStar,
            ResinUsage = domain.EnsureResinUsage().Clone(),
            RewardRecognitionEnabled = domain.RewardRecognitionEnabled,
            FightEndDelay = domain.FightEndDelay,
            ShortMovement = domain.ShortMovement,
            WalkToF = domain.WalkToF,
            LeftRightMoveTimes = domain.LeftRightMoveTimes,
            AutoEat = domain.AutoEat,
            ReviveRetryCount = domain.ReviveRetryCount
        };
        // 培养计划设置同样来自导入时的秘境配置
        param.CopyTrainingGuideSettings(domain);
        return param;
    }

    /// <summary>
    /// 自动首领讨伐：首领设置全部来自一条龙，战斗配置冻结导入时的全局自动战斗配置。
    /// 策略路径保存策略名，后续修改策略名或路径都由任务按 CombatStrategyResolver 解析。
    /// </summary>
    private static AutoBossParam CreateBossParam(AllConfig config, OneDragonFlowConfig dragon)
    {
        var strategy = string.IsNullOrWhiteSpace(dragon.AutoBossStrategyName) ? CombatStrategyResolver.AutoSelectName : dragon.AutoBossStrategyName;
        var param = AutoBossParam.CreateWithoutDefaultConfig(strategy);
        param.BossName = dragon.AutoBossName;
        param.StrategyName = strategy;
        param.TeamName = dragon.AutoBossTeamName;
        param.ResinUsage = ResinUsageLegacyMapper.FromBoss(dragon.AutoBossSpecifyRunCount,
            dragon.AutoBossRunCount, dragon.AutoBossUseTransientResin, dragon.AutoBossUseFragileResin);
        param.ReviveRetryCount = dragon.AutoBossReviveRetryCount;
        param.ReturnToStatueAfterEachRound = dragon.AutoBossReturnToStatueAfterEachRound;
        param.RewardRecognitionEnabled = dragon.AutoBossRewardRecognitionEnabled;
        param.Timeout = dragon.AutoBossTimeout;
        // 随后立即序列化，引用全局对象不会被保存下来，任务参数里得到的是导入时的副本
        param.FightConfig = config.AutoFightConfig;
        return param;
    }

    /// <summary>
    /// 自动幽境危战：业务设置来自导入时的幽境配置；策略路径留空，运行时按策略名解析，导入的计划可以跨机器使用。
    /// </summary>
    private static AutoStygianOnslaughtParam CreateStygianParam(AllConfig config)
    {
        var stygian = config.AutoStygianOnslaughtConfig;
        return new AutoStygianOnslaughtParam(stygian)
        {
            CombatScriptBagPath = string.Empty,
            StrategyName = string.IsNullOrWhiteSpace(stygian.StrategyName) ? CombatStrategyResolver.AutoSelectName : stygian.StrategyName,
            MaxArtifactStar = config.AutoArtifactSalvageConfig.MaxArtifactStar
        };
    }

    /// <summary>
    /// 自动地脉花：业务设置来自导入时的地脉花配置，次数、树脂耗尽、取小值和一条龙模式来自一条龙；
    /// 地脉花战斗配置没有策略时补全为全局自动战斗配置，最后仍为空则按队伍自动选择。
    /// </summary>
    private static AutoLeyLineOutcropParam CreateLeyLineParam(AllConfig config, OneDragonFlowConfig dragon)
    {
        var source = config.AutoLeyLineOutcropConfig;
        // 先复制战斗配置，补全策略时不能改写全局配置
        var fight = SoloTaskJson.Deserialize<AutoLeyLineOutcropFightConfig>(
            SoloTaskJson.Serialize(source.FightConfig ?? new AutoLeyLineOutcropFightConfig()));
        if (string.IsNullOrWhiteSpace(fight.StrategyName)) fight.CopyFromAutoFightConfig(config.AutoFightConfig);
        if (string.IsNullOrWhiteSpace(fight.StrategyName)) fight.StrategyName = CombatStrategyResolver.AutoSelectName;
        var param = new AutoLeyLineOutcropParam(source)
        {
            FightConfig = fight,
            OneDragonMode = dragon.LeyLineOneDragonMode
        };
        // 一条龙保存模式与次数，补充开关仍来自导入时的地脉花配置。
        param.ResinUsage = ResinUsageLegacyMapper.FromLeyLine(dragon.LeyLineResinExhaustionMode,
            dragon.LeyLineOpenModeCountMin, dragon.LeyLineRunCount > 0 ? dragon.LeyLineRunCount : param.Count,
            source.EnsureResinUsage().Allowed.Transient, source.EnsureResinUsage().Allowed.Fragile);
        return param;
    }

    /// <summary>旧一条龙是否为地脉花设置了按星期的运行日期、类型或国家；用于提示这些设置未导入。</summary>
    private static bool HasLeyLineWeekSettings(OneDragonFlowConfig dragon)
    {
        bool[] runDays =
        [
            dragon.LeyLineRunMonday, dragon.LeyLineRunTuesday, dragon.LeyLineRunWednesday, dragon.LeyLineRunThursday,
            dragon.LeyLineRunFriday, dragon.LeyLineRunSaturday, dragon.LeyLineRunSunday
        ];
        string[] dayValues =
        [
            dragon.LeyLineMondayType, dragon.LeyLineMondayCountry, dragon.LeyLineTuesdayType, dragon.LeyLineTuesdayCountry,
            dragon.LeyLineWednesdayType, dragon.LeyLineWednesdayCountry, dragon.LeyLineThursdayType, dragon.LeyLineThursdayCountry,
            dragon.LeyLineFridayType, dragon.LeyLineFridayCountry, dragon.LeyLineSaturdayType, dragon.LeyLineSaturdayCountry,
            dragon.LeyLineSundayType, dragon.LeyLineSundayCountry
        ];
        return runDays.Any(run => run) || dayValues.Any(value => !string.IsNullOrWhiteSpace(value));
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
        node.TaskType = "legacy.unsupported"; node.IsEnabled = false; node.Source = null; node.Children.Clear();
        node.Settings = new JObject(); node.ProfileId = null;
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
