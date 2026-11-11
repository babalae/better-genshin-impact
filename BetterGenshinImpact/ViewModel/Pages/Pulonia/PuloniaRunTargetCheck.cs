using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 提交前确认的结果：是否继续提交，以及本实例的运行是否允许把其他在线实例挤下线（统筹设计 4.6）。
/// </summary>
/// <param name="AllowDisplaceOnlineInstance">用户已确认切换会把其他在线实例挤下线；只对本实例的本地运行生效</param>
public sealed record PuloniaRunTargetConfirmation(bool AllowDisplaceOnlineInstance)
{
    /// <summary>没有需要确认的情况，直接提交。</summary>
    public static PuloniaRunTargetConfirmation Proceed { get; } = new(false);
}

/// <summary>
/// 一个目标实例在提交前的情况：实例键、显示名、当前账户（提交前重新读取）与本次预期的运行账户。
/// </summary>
/// <param name="InstanceKey">实例键</param>
/// <param name="InstanceName">实例显示名</param>
/// <param name="IsLocal">是否在本实例本地提交</param>
/// <param name="CurrentAccountId">实例当前账户；“未确定”时为 null</param>
/// <param name="RunAccountId">预期运行账户：计划指定账户时为该账户，否则为实例当前账户</param>
internal sealed record PuloniaRunTargetState(string InstanceKey, string InstanceName, bool IsLocal, string? CurrentAccountId,
    string? RunAccountId)
{
    /// <summary>云原神实例不能切换账户。</summary>
    public bool IsWebView => InstanceKey.StartsWith(PuloniaInstanceKey.WebViewPrefix, StringComparison.Ordinal);
}

/// <summary>
/// 提交前检查的结论：需要提示但允许坚持提交的问题，以及本实例运行需要确认的“挤下线”说明。
/// </summary>
/// <param name="Problems">问题说明，每条一句</param>
/// <param name="LocalDisplacement">本实例运行切换时会把其他在线实例挤下线的说明；没有时为 null</param>
internal sealed record PuloniaRunTargetFindings(IReadOnlyList<string> Problems, string? LocalDisplacement);

/// <summary>
/// “运行于”的提交前检查（统筹设计 4.5、4.6、9.4），任务计划页与新一条龙页共用：
/// 按每个目标实例计算预期运行账户，检查账户重复、账户已被占用、当前账户未确定、云原神不能切换，以及切换会把其他在线实例挤下线。
/// 有问题时说明原因，用户可以坚持提交，也可以返回在“运行于”中修改各实例的当前账户；只有一个实例且没有问题时不弹任何框。
/// 不含游戏节点的计划不确定运行账户，不检查。
/// </summary>
public sealed class PuloniaRunTargetCheck
{
    /// <summary>账户服务：账户目录、各实例当前账户、账户占用与在线判断。</summary>
    private readonly IPuloniaAccountService _accounts;

    /// <summary>任务类型 → 是否需要游戏会话。</summary>
    private readonly IReadOnlyDictionary<string, bool> _requiresGameSession;

    /// <summary>
    /// 建立检查。
    /// </summary>
    /// <param name="accounts">账户服务</param>
    /// <param name="definitions">任务能力定义，用于判断计划是否含游戏节点</param>
    public PuloniaRunTargetCheck(IPuloniaAccountService accounts, IReadOnlyList<PuloniaTaskDefinition> definitions)
    {
        _accounts = accounts;
        _requiresGameSession = definitions.GroupBy(definition => definition.TaskType, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().RequiresGameSession, StringComparer.Ordinal);
    }

    /// <summary>
    /// 提交前确认：读取最新的账户目录与各实例当前账户，按规则检查；有问题时提示，用户选择返回时调用 <paramref name="returnToChoice"/>。
    /// </summary>
    /// <param name="document">要运行的计划</param>
    /// <param name="targets">目标实例</param>
    /// <param name="returnToChoice">用户选择返回修改时调用，例如展开“运行于”</param>
    /// <returns>继续提交时的确认结果；用户取消时为 null</returns>
    public async Task<PuloniaRunTargetConfirmation?> ConfirmAsync(PuloniaTaskPlanDocumentViewModel document,
        IReadOnlyList<PuloniaRunTargetOptionViewModel> targets, Action returnToChoice)
    {
        if (targets.Count == 0 || !RequiresRunAccount(document))
            return PuloniaRunTargetConfirmation.Proceed;

        // 1. 读取最新的账户目录、各目标实例的当前账户与在线实例；读不出时不阻止提交，运行开始时会按实际情况处理
        IReadOnlyList<PuloniaGenshinAccountProfile> accounts;
        List<PuloniaRunTargetState> states;
        IReadOnlyList<(string InstanceKey, string? CurrentAccountId)> online;
        try
        {
            accounts = (await _accounts.LoadAccountsAsync()).Accounts;
            states = [];
            var planAccountId = document.Plan.AccountId;
            foreach (var target in targets)
            {
                var current = await _accounts.GetCurrentAccountIdAsync(target.InstanceKey);
                states.Add(new PuloniaRunTargetState(target.InstanceKey, target.InstanceName, target.IsLocal, current,
                    planAccountId ?? current));
            }
            online = await ListOnlineInstancesAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return PuloniaRunTargetConfirmation.Proceed;
        }

        // 2. 按规则检查
        var findings = Evaluate(document.Plan.AccountId, states, accounts, online,
            accountId => _accounts.GetAccountOccupant(accountId));

        // 3. 有问题：说明并询问是否坚持提交；选择返回时展开“运行于”，在那里就地修改各实例的当前账户
        if (findings.Problems.Count > 0)
        {
            var message = "提交前发现以下情况，相关的运行开始时可能进入待处理：\n\n"
                          + string.Join("\n", findings.Problems.Select(problem => "• " + problem))
                          + "\n\n仍然提交吗？选择“否”可以先修改各实例的当前账户（在“运行于”或标题栏中修改）。";
            if (await ThemedMessageBox.ShowAsync(message, "检查运行账户", MessageBoxButton.YesNo,
                    ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                returnToChoice();
                return null;
            }
        }

        // 4. 本实例的运行切换时会把其他在线实例挤下线：确认后本次本地请求带上确认标记
        if (findings.LocalDisplacement is { } displacement)
        {
            if (await ThemedMessageBox.ShowAsync(displacement + "\n\n是否继续？", "切换账户", MessageBoxButton.YesNo,
                    ThemedMessageBox.MessageBoxIcon.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return null;
            return new PuloniaRunTargetConfirmation(true);
        }
        return PuloniaRunTargetConfirmation.Proceed;
    }

    /// <summary>
    /// 检查规则（纯逻辑，不读文件）：
    /// 当前账户未确定（跟随当前账户时）；几个目标的预期运行账户相同；预期运行账户正被其他实例占用；
    /// 指定账户而目标是云原神、且其当前账户不是该账户；指定账户需要切换，而另一个在线实例的当前账户就是该账户（会挤下线）。
    /// </summary>
    /// <param name="planAccountId">计划的运行账户设置；为空表示跟随当前账户</param>
    /// <param name="targets">各目标实例的情况</param>
    /// <param name="accounts">账户目录</param>
    /// <param name="online">在线实例及其当前账户（含本实例）</param>
    /// <param name="occupantOf">读取账户当前的占用者；没有被占用时返回 null</param>
    /// <returns>问题与本实例需要确认的挤下线说明</returns>
    internal static PuloniaRunTargetFindings Evaluate(string? planAccountId, IReadOnlyList<PuloniaRunTargetState> targets,
        IReadOnlyList<PuloniaGenshinAccountProfile> accounts, IReadOnlyList<(string InstanceKey, string? CurrentAccountId)> online,
        Func<string, PuloniaLockOwner?> occupantOf)
    {
        var problems = new List<string>();
        string? localDisplacement = null;
        string Name(string? accountId) => accounts.FirstOrDefault(account => account.Id == accountId)?.Name ?? "未确定";

        // 1. 跟随当前账户：当前账户未确定的实例无法确定运行账户
        foreach (var target in targets.Where(target => target.RunAccountId is null))
            problems.Add($"{target.InstanceName}的当前账户未确定，请先为它选择当前账户。");

        // 2. 几个目标为同一账户运行：同一账户不能同时在两个实例运行，后开始的会进入待处理
        foreach (var group in targets.Where(target => target.RunAccountId is not null)
                     .GroupBy(target => target.RunAccountId!, StringComparer.Ordinal).Where(group => group.Count() > 1))
            problems.Add($"{string.Join("、", group.Select(target => target.InstanceName))}都会为“{Name(group.Key)}”运行，"
                         + "同一账户不能同时在两个实例运行，后开始的会进入待处理。");

        // 3. 预期运行账户正被其他实例占用；被目标实例自己占用时只是在它的队列中排在后面，不提示
        foreach (var target in targets.Where(target => target.RunAccountId is not null))
        {
            if (occupantOf(target.RunAccountId!) is { } occupant && occupant.InstanceKey != target.InstanceKey)
                problems.Add($"账户“{Name(target.RunAccountId)}”正在{PuloniaAccountService.DescribeOccupant(occupant)}运行，"
                             + $"{target.InstanceName}的运行开始时会进入待处理。");
        }

        // 4. 指定账户：云原神不能切换；其他实例切换时可能把在线实例挤下线
        if (planAccountId is not null)
        {
            foreach (var target in targets.Where(target => target.CurrentAccountId != planAccountId))
            {
                if (target.IsWebView)
                {
                    problems.Add($"{target.InstanceName}的当前账户是“{Name(target.CurrentAccountId)}”，云原神实例不能切换到“{Name(planAccountId)}”。");
                    continue;
                }
                var displaced = online.Where(instance => instance.InstanceKey != target.InstanceKey
                                                         && instance.CurrentAccountId == planAccountId)
                    .Select(instance => PuloniaInstanceKey.Describe(instance.InstanceKey)).ToArray();
                if (displaced.Length == 0)
                    continue;
                var text = $"{string.Join("、", displaced)}的当前账户是“{Name(planAccountId)}”且正在线，"
                           + $"{target.InstanceName}切换到“{Name(planAccountId)}”会把它挤下线。";
                // 本实例的运行可以在这里确认；下发到子实例的运行不能确认，切换前会进入待处理
                if (target.IsLocal)
                    localDisplacement = text;
                else
                    problems.Add(text + "下发的运行不会挤下线，会进入待处理。");
            }
        }
        return new PuloniaRunTargetFindings(problems, localDisplacement);
    }

    /// <summary>
    /// 在线实例及其当前账户（含本实例）：按实例设置枚举，在线判断读取各实例的在线标记。读不出的设置文件跳过。
    /// </summary>
    private async Task<IReadOnlyList<(string InstanceKey, string? CurrentAccountId)>> ListOnlineInstancesAsync()
    {
        var settings = await _accounts.ListInstanceSettingsAsync();
        return settings.Items
            .Select(entry => (Key: PuloniaInstanceKey.TryFromDirectoryName(entry.DirectoryName), entry.Settings.CurrentAccountId))
            .Where(entry => entry.Key is not null && _accounts.IsInstanceOnline(entry.Key))
            .Select(entry => (entry.Key!, entry.CurrentAccountId))
            .ToArray();
    }

    /// <summary>
    /// 计划是否会确定运行账户：含启用的游戏节点，或含启用的引用（计划引用、目录引用展开后可能有游戏节点）。
    /// </summary>
    /// <param name="document">要运行的计划</param>
    private bool RequiresRunAccount(PuloniaTaskPlanDocumentViewModel document)
        => document.EnumerateNodes().Any(node => node.IsEffectivelyEnabled
            && (node.IsLeaf
                ? _requiresGameSession.GetValueOrDefault(node.TaskType)
                : node.Model.Source is not null));
}
