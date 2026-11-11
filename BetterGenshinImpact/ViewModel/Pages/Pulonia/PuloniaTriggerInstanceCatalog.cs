using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 触发设置使用的实例目录（统筹设计 9.5）：列出可选为执行实例的全部实例（主实例、桌面分身、已建立的云原神实例，不要求在线），
/// 判断实例是否在线，并读取其他实例 state.json 中的定时触发游标，显示“已处理到 04:30”。
/// 云原神实例来自云原神实例目录与 Pulonia 的实例目录；只读，不写任何文件。
/// </summary>
public sealed class PuloniaTriggerInstanceCatalog
{
    /// <summary>Pulonia 存储，提供实例目录位置。</summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>账户服务：本实例的实例键、各实例当前账户与在线判断。</summary>
    private readonly IPuloniaAccountService _accounts;

    /// <summary>跨实例运行视图，读取其他实例的触发游标；测试中可为空。</summary>
    private readonly PuloniaRunOverview? _overview;

    /// <summary>云原神实例目录；测试中可为空，只列出 Pulonia 实例目录中的云原神实例。</summary>
    private readonly WebViewInstanceStore? _webViews;

    /// <summary>
    /// 建立实例目录。
    /// </summary>
    /// <param name="store">Pulonia 存储</param>
    /// <param name="accounts">账户服务</param>
    /// <param name="overview">跨实例运行视图；为空时不显示其他实例的游标</param>
    /// <param name="webViews">云原神实例目录；为空时只按 Pulonia 实例目录列出云原神实例</param>
    public PuloniaTriggerInstanceCatalog(PuloniaTaskStore store, IPuloniaAccountService accounts,
        PuloniaRunOverview? overview = null, WebViewInstanceStore? webViews = null)
    {
        _store = store;
        _accounts = accounts;
        _overview = overview;
        _webViews = webViews;
    }

    /// <summary>本实例的实例键。</summary>
    public string CurrentInstanceKey => _accounts.CurrentInstanceKey;

    /// <summary>账户服务，供保存前按 4.5 检查各执行实例的当前账户。</summary>
    public IPuloniaAccountService Accounts => _accounts;

    /// <summary>
    /// 可选为执行实例的全部实例，按主实例、桌面分身、云原神实例（按名称）排列；同步读取目录名，开销很小。
    /// </summary>
    /// <param name="extraKeys">额外要列出的实例键（例如已保存触发器中的执行实例，即使实例已删除也要能看到并取消）</param>
    /// <returns>实例键与显示名</returns>
    public IReadOnlyList<(string Key, string Name)> ListInstances(IEnumerable<string> extraKeys)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { PuloniaInstanceKey.Primary, PuloniaInstanceKey.ChildSession };
        // 1. Pulonia 实例目录：使用过 Pulonia 的实例都有设置文件
        try
        {
            var root = Path.Combine(_store.RootDirectory, "instances");
            if (Directory.Exists(root))
                foreach (var directory in Directory.EnumerateDirectories(root))
                    if (PuloniaInstanceKey.TryFromDirectoryName(Path.GetFileName(directory)) is { } key)
                        keys.Add(key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        // 2. 已建立的云原神实例：从未启动过的实例也列出
        try
        {
            foreach (var name in _webViews?.List() ?? [])
            {
                try { keys.Add(PuloniaInstanceKey.ForWebView(name)); }
                catch (PuloniaTaskValidationException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        // 3. 已保存的执行实例
        foreach (var key in extraKeys)
            keys.Add(key);
        return keys.OrderBy(Order).ThenBy(key => key, StringComparer.Ordinal)
            .Select(key => (key, PuloniaInstanceKey.Describe(key))).ToArray();

        static int Order(string key) => key switch
        {
            PuloniaInstanceKey.Primary => 0,
            PuloniaInstanceKey.ChildSession => 1,
            _ => 2
        };
    }

    /// <summary>实例是否在线：本实例总在线，其他实例按在线标记判断。</summary>
    /// <param name="instanceKey">实例键</param>
    public bool IsOnline(string instanceKey) => _accounts.IsInstanceOnline(instanceKey);

    /// <summary>
    /// 实例是否仍存在：主实例与桌面分身总存在；云原神实例在云原神实例目录中才存在（没有目录服务时不判断）。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    public bool Exists(string instanceKey)
    {
        if (instanceKey is PuloniaInstanceKey.Primary or PuloniaInstanceKey.ChildSession || _webViews is null)
            return true;
        try
        {
            return _webViews.List().Any(name => WebViewKey(name) == instanceKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }

        // 云原神实例名对应的实例键；名称不合法时为 null
        static string? WebViewKey(string name)
        {
            try { return PuloniaInstanceKey.ForWebView(name); }
            catch (PuloniaTaskValidationException) { return null; }
        }
    }

    /// <summary>
    /// 其他实例的定时触发游标，键为实例键；没有跨实例运行视图或读取失败时为空。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PuloniaTaskTriggerState>>> ListOtherCursorsAsync(
        CancellationToken ct = default)
    {
        if (_overview is null)
            return new Dictionary<string, IReadOnlyList<PuloniaTaskTriggerState>>();
        try
        {
            return await _overview.ListOtherTriggerCursorsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return new Dictionary<string, IReadOnlyList<PuloniaTaskTriggerState>>();
        }
    }
}
