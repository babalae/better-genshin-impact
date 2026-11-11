using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 实例设置 <c>instances/&lt;实例目录名&gt;/settings.json</c> 的读写：保存实例当前账户。
/// 原子替换写入，后写者覆盖；与其他 Pulonia 写入共用存储门和 <c>.writer.lock</c>。
/// 同一实例目录下的运行状态 <c>state.json</c> 只由该实例写入，读写见 <see cref="LoadStateAsync"/>、<see cref="SaveStateAsync"/>。
/// </summary>
public sealed partial class PuloniaTaskStore
{
    /// <summary>实例目录的父目录名，相对于 User/Pulonia。</summary>
    private const string InstancesDirectoryName = "instances";
    /// <summary>实例设置文件名。</summary>
    private const string InstanceSettingsFileName = "settings.json";
    /// <summary>实例运行状态文件名，与实例设置同在实例目录下。</summary>
    internal const string StateFileName = "state.json";

    /// <summary>
    /// 读取实例设置。文件不存在时（实例首次使用）把当前账户设为账户列表第一个账户并立即写入；
    /// 账户目录也不存在时一并建立只含“我的账户”的目录。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>实例设置的独立副本</returns>
    public async Task<PuloniaInstanceSettings> LoadInstanceSettingsAsync(string instanceKey, CancellationToken ct = default)
    {
        var path = GetInstanceSettingsPath(instanceKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await ReadIfExistsAsync(path, ct).ConfigureAwait(false) is { } json)
                return ReadInstanceSettings(path, json);
            // 首次使用：在写锁内重新检查，避免两个进程同时初始化时互相覆盖。
            using var writerLock = await AcquireWriterLockAsync(ct).ConfigureAwait(false);
            if (await ReadIfExistsAsync(path, ct).ConfigureAwait(false) is { } raced)
                return ReadInstanceSettings(path, raced);
            var accounts = await EnsureAccountSettingsCoreAsync(ct).ConfigureAwait(false);
            var settings = new PuloniaInstanceSettings { CurrentAccountId = accounts.Accounts[0].Id };
            await WriteAtomicallyAsync(path, PuloniaTaskJson.Write(settings), ct).ConfigureAwait(false);
            return settings;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 原子替换保存实例当前账户；后写者覆盖。账户 ID 不为空时必须是账户目录中现有的账户。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="currentAccountId">新的当前账户 ID；为空表示“未确定”</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="InvalidOperationException">账户不存在</exception>
    public async Task SaveInstanceCurrentAccountAsync(string instanceKey, string? currentAccountId, CancellationToken ct = default)
    {
        var path = GetInstanceSettingsPath(instanceKey);
        if (currentAccountId is not null)
            PuloniaTaskValidator.ValidateId(currentAccountId, "instance/current_account_id");
        var json = PuloniaTaskJson.Write(new PuloniaInstanceSettings { CurrentAccountId = currentAccountId });
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = await AcquireWriterLockAsync(ct).ConfigureAwait(false);
            // 与账户目录在同一写锁内核对引用，避免写入已删除的账户。
            if (currentAccountId is not null)
            {
                var accounts = await EnsureAccountSettingsCoreAsync(ct).ConfigureAwait(false);
                if (accounts.Accounts.All(account => account.Id != currentAccountId))
                    throw new InvalidOperationException("要设为当前账户的账户已不存在，请重新选择。");
            }
            await WriteAtomicallyAsync(path, json, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 列出 <c>instances/*/settings.json</c> 中可读取的实例设置；读不出的文件记入失败，文件保留不动。
    /// 供删除账户前检查“是否为某个实例的当前账户”。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>各实例目录的设置与读取失败的文件</returns>
    public async Task<PuloniaTaskFileListResult<PuloniaInstanceSettingsEntry>> ListInstanceSettingsAsync(CancellationToken ct = default)
    {
        var root = Path.Combine(RootDirectory, InstancesDirectoryName);
        if (!Directory.Exists(root))
            return PuloniaTaskFileListResult<PuloniaInstanceSettingsEntry>.Empty;
        var items = new List<PuloniaInstanceSettingsEntry>();
        var failures = new List<PuloniaTaskFileLoadFailure>();
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, InstanceSettingsFileName);
            try
            {
                // 实例目录中可能只有运行状态等其他文件，没有设置文件时略过。
                if (await ReadIfExistsAsync(path, ct).ConfigureAwait(false) is { } json)
                    items.Add(new PuloniaInstanceSettingsEntry(Path.GetFileName(directory), ReadInstanceSettings(path, json)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(new PuloniaTaskFileLoadFailure(path, null, ex.Message));
            }
        }
        return new PuloniaTaskFileListResult<PuloniaInstanceSettingsEntry>(items.AsReadOnly(), failures.AsReadOnly());
    }

    /// <summary>实例设置文件路径；实例键先经格式校验再参与路径计算。</summary>
    /// <param name="instanceKey">实例键</param>
    private string GetInstanceSettingsPath(string instanceKey)
        => Path.Combine(GetInstanceDirectory(instanceKey), InstanceSettingsFileName);

    /// <summary>
    /// 实例运行状态文件路径 <c>instances/&lt;实例目录名&gt;/state.json</c>，备份为同目录的 <c>state.json.bak</c>。
    /// 实例键先经格式校验再参与路径计算。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <returns>完整路径</returns>
    internal string GetStatePath(string instanceKey)
        => Path.Combine(GetInstanceDirectory(instanceKey), StateFileName);

    /// <summary>实例目录 <c>instances/&lt;实例目录名&gt;</c>；实例键先经格式校验再参与路径计算。</summary>
    /// <param name="instanceKey">实例键</param>
    private string GetInstanceDirectory(string instanceKey)
        => Path.Combine(RootDirectory, InstancesDirectoryName, PuloniaInstanceKey.ToDirectoryName(instanceKey));

    /// <summary>严格读取并校验实例设置。</summary>
    /// <param name="path">文件路径，用于错误定位</param>
    /// <param name="json">文件内容</param>
    private static PuloniaInstanceSettings ReadInstanceSettings(string path, string json)
    {
        var settings = PuloniaTaskJson.Read<PuloniaInstanceSettings>(json);
        if (settings.SchemaVersion != PuloniaInstanceSettings.CurrentSchemaVersion)
            throw new PuloniaTaskValidationException(path, $"不支持实例设置格式版本 {settings.SchemaVersion}。");
        if (settings.CurrentAccountId is not null)
            PuloniaTaskValidator.ValidateId(settings.CurrentAccountId, path);
        return settings;
    }
}
