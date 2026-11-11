using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>复用计划存储的写锁、修订校验与原子替换保存账户目录，并在目录保存成功后提交登录凭据。</summary>
public sealed partial class PuloniaTaskStore
{
    /// <summary>账户图片目录名，相对于 User/Pulonia。</summary>
    private const string AccountImageDirectoryName = "account-images";
    /// <summary>账户目录文件名，相对于 User/Pulonia。</summary>
    private const string AccountSettingsFileName = "accounts.json";
    /// <summary>Windows 凭据目标名前缀，后接账户 ID；同一账户的目标名固定，改密码时覆盖原条目。</summary>
    private const string CredentialTargetPrefix = "BetterGenshinImpact.Pulonia.Account.";

    /// <summary>登录凭据的保存位置；默认使用 Windows 凭据管理器，测试可替换为内存实现。</summary>
    internal IPuloniaCredentialVault CredentialVault { get; init; } = new PuloniaWindowsCredentialVault();

    /// <summary>账户目录文件的完整路径。</summary>
    private string AccountSettingsPath => Path.Combine(RootDirectory, AccountSettingsFileName);

    /// <summary>
    /// 读取账户目录。文件尚不存在时（首次使用）在写锁内建立只含“我的账户”的目录并落盘，
    /// 保证各实例首次读取当前账户时引用的是同一个已保存的账户。
    /// </summary>
    public async Task<PuloniaGenshinAccountSettings> LoadAccountSettingsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(AccountSettingsPath))
                return await ReadAccountSettingsAsync(ct).ConfigureAwait(false);
            using var writerLock = await AcquireWriterLockAsync(ct).ConfigureAwait(false);
            return await EnsureAccountSettingsCoreAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>校验全部账户后一次耐久保存，不写入新的登录凭据；失败时不替换界面草稿。</summary>
    /// <param name="settings">要保存的账户目录草稿，调用方对象不会被修改</param>
    /// <param name="ct">取消令牌</param>
    public async Task<PuloniaGenshinAccountSettings> SaveAccountSettingsAsync(PuloniaGenshinAccountSettings settings,
        CancellationToken ct = default)
        => (await SaveAccountSettingsAsync(settings, new Dictionary<string, (string UserName, string Password)>(), ct)
            .ConfigureAwait(false)).Settings;

    /// <summary>
    /// 校验后先耐久保存账户目录，成功后再提交登录凭据：写入本次填写的凭据，删除目录不再引用的旧凭据。
    /// 目录保存失败时不触碰任何凭据，不会留下孤立条目。
    /// </summary>
    /// <remarks>
    /// 运行快照只记录凭据目标名，排队或续跑的运行在登录时读取当前凭据：改密码后旧运行使用新密码；
    /// 账户被移除或改用不使用密码的登录方式后，旧运行也无法再用密码登录。
    /// 删除账户前的引用检查（被计划指定、是某实例的当前账户）由账户应用服务负责，存储层只保证目录结构合法。
    /// </remarks>
    /// <param name="settings">要保存的账户目录草稿，调用方对象不会被修改</param>
    /// <param name="credentials">以账户 ID 为键的新登录凭据；未列出的账户保留已保存凭据</param>
    /// <param name="ct">取消令牌；目录写入完成后不再响应，确保凭据与目录一致</param>
    /// <returns>保存后的目录及凭据写入失败说明</returns>
    public async Task<PuloniaGenshinAccountSaveResult> SaveAccountSettingsAsync(PuloniaGenshinAccountSettings settings,
        IReadOnlyDictionary<string, (string UserName, string Password)> credentials, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        // 先复制凭据草稿：调用方（如关闭中的窗口）可能在保存期间清空自己的草稿，提交阶段必须与目录使用同一份内容。
        credentials = new Dictionary<string, (string UserName, string Password)>(credentials, StringComparer.Ordinal);
        var candidate = PuloniaTaskJson.Read<PuloniaGenshinAccountSettings>(PuloniaTaskJson.Write(settings));
        ValidateAccountSettings(candidate);
        ApplyCredentialReferences(candidate, credentials);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var writerLock = await AcquireWriterLockAsync(ct).ConfigureAwait(false);
            var current = await ReadAccountSettingsAsync(ct).ConfigureAwait(false);
            if (candidate.Revision != current.Revision)
                throw new InvalidOperationException("账户目录已被其他编辑修改，请重新读取账户页后再修改。");
            candidate.Revision = checked(current.Revision + 1);
            await WriteAtomicallyAsync(AccountSettingsPath, PuloniaTaskJson.Write(candidate), ct).ConfigureAwait(false);
            // 目录已耐久保存，此后才提交凭据并清理被删除账户的图片与账户进度；仍在写锁内，避免与下一次保存交错。
            var credentialError = CommitCredentials(current, candidate, credentials);
            DeleteRemovedAccountImages(current, candidate);
            DeleteRemovedAccountProgress(current, candidate);
            return new PuloniaGenshinAccountSaveResult(candidate, credentialError);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 按本次凭据草稿和登录方式确定每个账户的凭据引用：填写了凭据的账户指向固定目标名，
    /// 不使用密码的登录方式（账户图片、列表文字）不保留引用，保存成功后删除其旧凭据。
    /// </summary>
    /// <param name="candidate">已通过校验的目录副本</param>
    /// <param name="credentials">以账户 ID 为键的新登录凭据</param>
    private static void ApplyCredentialReferences(PuloniaGenshinAccountSettings candidate,
        IReadOnlyDictionary<string, (string UserName, string Password)> credentials)
    {
        foreach (var (id, credential) in credentials)
        {
            var account = candidate.Accounts.SingleOrDefault(item => item.Id == id)
                          ?? throw new InvalidOperationException("登录凭据对应的账户不存在。");
            if (string.IsNullOrWhiteSpace(credential.UserName) || string.IsNullOrEmpty(credential.Password))
                throw new InvalidOperationException("保存登录凭据时必须同时填写账号和密码；无需更新凭据时请留空。");
            if (!UsesPassword(account.LoginMode))
                throw new InvalidOperationException($"账户“{account.Name}”的登录方式不使用密码，请清空账号和密码，或改用自动或账号密码方式。");
            account.CredentialReference = GetCredentialTarget(account.Id);
        }
        foreach (var account in candidate.Accounts.Where(item => !UsesPassword(item.LoginMode)))
            account.CredentialReference = null;
    }

    /// <summary>
    /// 目录保存成功后提交凭据：先写入新凭据（同名覆盖），再删除旧目录引用而新目录不再引用的凭据。
    /// </summary>
    /// <param name="previous">保存前的耐久目录</param>
    /// <param name="saved">刚保存的目录</param>
    /// <param name="credentials">以账户 ID 为键的新登录凭据</param>
    /// <returns>写入失败说明；全部成功时为空</returns>
    private string? CommitCredentials(PuloniaGenshinAccountSettings previous, PuloniaGenshinAccountSettings saved,
        IReadOnlyDictionary<string, (string UserName, string Password)> credentials)
    {
        var failures = new List<string>();
        foreach (var (id, credential) in credentials)
        {
            var account = saved.Accounts.Single(item => item.Id == id);
            try { CredentialVault.Write(account.CredentialReference!, credential.UserName, credential.Password); }
            catch (Exception ex) { failures.Add($"“{account.Name}”：{ex.Message}"); }
        }
        // 账户被移除或改用无密码方式后，旧凭据不再被目录引用；删除是尽力而为，遗留条目不影响登录。
        var referenced = saved.Accounts.Select(item => item.CredentialReference).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var target in previous.Accounts.Select(item => item.CredentialReference).OfType<string>()
                     .Where(target => !referenced.Contains(target)).Distinct(StringComparer.Ordinal))
            CredentialVault.Delete(target);
        return failures.Count == 0
            ? null
            : "以下账户的登录凭据未能写入，请重新填写后保存：" + string.Join("；", failures);
    }

    /// <summary>
    /// 目录保存成功后，尽力删除被移除账户不再被引用的账户图片：它当前引用的图片，以及以其账户 ID 为前缀、此前被替换下来的旧图片。
    /// 只删除 <c>account-images</c> 目录内的文件；仍被目录中其他账户引用的图片保留。删除失败不影响保存结果。
    /// </summary>
    /// <remarks>
    /// 排队或待续跑的运行快照可能仍引用被删除账户的图片；账户已从目录删除，这类运行本就不能再正确执行，不额外处理。
    /// 尚未保存就从界面草稿中移除的新账户所导入的图片不经过这里，暂不清理。
    /// </remarks>
    /// <param name="previous">保存前的耐久目录</param>
    /// <param name="saved">刚保存的目录</param>
    private void DeleteRemovedAccountImages(PuloniaGenshinAccountSettings previous, PuloniaGenshinAccountSettings saved)
    {
        // 1. 找出本次被移除的账户；没有时不访问磁盘
        var savedIds = saved.Accounts.Select(account => account.Id).ToHashSet(StringComparer.Ordinal);
        var removed = previous.Accounts.Where(account => !savedIds.Contains(account.Id)).ToArray();
        if (removed.Length == 0)
            return;
        // 2. 逐个账户删除不再被目录引用的图片
        foreach (var account in removed)
        {
            foreach (var path in ListRemovableAccountImages(account, saved.Accounts))
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// 删除账户时会一并删除的图片：它当前引用的图片，加上以其账户 ID 为前缀、此前被替换下来的旧图片；
    /// 仍被 <paramref name="remaining"/> 中其他账户引用的图片不算在内。只读取目录，不删除。
    /// </summary>
    /// <param name="account">要删除的账户</param>
    /// <param name="remaining">删除后仍在目录中的账户</param>
    /// <returns>图片的完整路径</returns>
    internal IReadOnlyList<string> ListRemovableAccountImages(PuloniaGenshinAccountProfile account,
        IEnumerable<PuloniaGenshinAccountProfile> remaining)
    {
        // 1. 目录中仍被引用的图片一律保留
        var referenced = remaining.Where(item => item.Id != account.Id)
            .Select(item => TryResolveAccountImagePath(item.ImagePath)).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 2. 候选：当前引用的图片，加上按命名规则属于该账户的旧图片（账户 ID 已通过格式校验，不含通配符）
        var directory = Path.Combine(RootDirectory, AccountImageDirectoryName);
        var candidates = new List<string>();
        if (TryResolveAccountImagePath(account.ImagePath) is { } current && File.Exists(current))
            candidates.Add(current);
        if (Directory.Exists(directory))
            candidates.AddRange(Directory.EnumerateFiles(directory, account.Id + "-*.png")
                .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                .Select(path => TryResolveAccountImagePath(Path.Combine(AccountImageDirectoryName, Path.GetFileName(path))))
                .OfType<string>());
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !referenced.Contains(path)).ToArray();
    }

    /// <summary>把账户图片的相对路径解析为完整路径；为空或不在账户图片目录内时返回 null，不抛出异常。</summary>
    /// <param name="relativePath">相对于 User/Pulonia 的图片路径</param>
    private string? TryResolveAccountImagePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;
        try { return GetAccountImagePath(relativePath); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>登录方式是否可能使用密码；自动方式把密码作为图片和列表文字之后的备用。</summary>
    /// <param name="mode">账户的登录方式</param>
    private static bool UsesPassword(PuloniaGenshinAccountLoginMode mode)
        => mode is PuloniaGenshinAccountLoginMode.Automatic or PuloniaGenshinAccountLoginMode.Password;

    /// <summary>账户固定的 Windows 凭据目标名。</summary>
    /// <param name="accountId">账户 ID</param>
    internal static string GetCredentialTarget(string accountId) => CredentialTargetPrefix + accountId;

    /// <summary>
    /// 读取耐久目录；文件不存在时返回尚未落盘的初始目录（只在保存流程中使用），损坏时明确报错，不能偷偷换成新目录。
    /// </summary>
    private async Task<PuloniaGenshinAccountSettings> ReadAccountSettingsAsync(CancellationToken ct)
    {
        var json = await ReadIfExistsAsync(AccountSettingsPath, ct).ConfigureAwait(false);
        var settings = json is not null
            ? PuloniaTaskJson.Read<PuloniaGenshinAccountSettings>(json)
            : PuloniaGenshinAccountSettings.CreateInitial(PuloniaServerTime.GetDefaultServerTimeZone());
        ValidateAccountSettings(settings);
        return settings;
    }

    /// <summary>
    /// 账户目录不存在时建立只含“我的账户”的初始目录并落盘，存在时直接读取。调用方必须持有 <see cref="_gate"/> 和写锁。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>已保存的账户目录</returns>
    private async Task<PuloniaGenshinAccountSettings> EnsureAccountSettingsCoreAsync(CancellationToken ct)
    {
        // 等待写锁期间其他进程可能已经建立目录，拿到写锁后重新检查。
        if (File.Exists(AccountSettingsPath))
            return await ReadAccountSettingsAsync(ct).ConfigureAwait(false);
        var initial = PuloniaGenshinAccountSettings.CreateInitial(PuloniaServerTime.GetDefaultServerTimeZone());
        ValidateAccountSettings(initial);
        await WriteAtomicallyAsync(AccountSettingsPath, PuloniaTaskJson.Write(initial), ct).ConfigureAwait(false);
        return initial;
    }

    /// <summary>
    /// 按账户目录规则校验结构：格式版本、至少一个账户、ID 不重复、名称必填且不重复、
    /// UID 为空或完整 9 到 10 位且不重复、最多一个账户没有 UID、服务器时区与登录方式有效。
    /// </summary>
    /// <param name="settings">待校验的目录</param>
    /// <exception cref="InvalidOperationException">目录不合法，消息可直接展示给用户</exception>
    internal static void ValidateAccountSettings(PuloniaGenshinAccountSettings settings)
    {
        if (settings.SchemaVersion != PuloniaGenshinAccountSettings.CurrentSchemaVersion)
            throw new InvalidOperationException($"不支持账户目录格式版本 {settings.SchemaVersion}。");
        if (settings.Revision < 0 || settings.Accounts is null || settings.Accounts.Any(a => a is null))
            throw new InvalidOperationException("账户目录格式无效。");
        if (settings.Accounts.Count == 0)
            throw new InvalidOperationException("账户目录至少需要一个账户。");
        if (settings.Accounts.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != settings.Accounts.Count)
            throw new InvalidOperationException("账户身份不能重复。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var uids = new HashSet<string>(StringComparer.Ordinal);
        PuloniaGenshinAccountProfile? withoutUid = null;
        foreach (var account in settings.Accounts)
        {
            PuloniaTaskValidator.ValidateId(account.Id, "account/id");
            if (string.IsNullOrWhiteSpace(account.Name))
                throw new InvalidOperationException("账户名称不能为空。");
            if (!Enum.IsDefined(account.LoginMode) || account.ListText is null)
                throw new InvalidOperationException($"账户“{account.Name}”的登录方式无效。");
            if (!Enum.IsDefined(account.ServerTimeZone))
                throw new InvalidOperationException($"账户“{account.Name}”的服务器时区无效。");
            if (!names.Add(account.Name.Trim()))
                throw new InvalidOperationException($"账户名称“{account.Name.Trim()}”重复，请为每个账户起不同的名称。");
            if (account.Uid is null)
            {
                // 最多一个账户可以不填 UID（初始的“我的账户”）；之后新增的账户必须填写。
                if (withoutUid is not null)
                    throw new InvalidOperationException(
                        $"账户“{withoutUid.Name}”和“{account.Name}”都没有填写 UID；目录中最多一个账户可以不填 UID。");
                withoutUid = account;
            }
            else if (!Regex.IsMatch(account.Uid, "^[0-9]{9,10}\\z"))
                throw new InvalidOperationException($"账户“{account.Name}”的 UID 需要是完整的 9 或 10 位数字。");
            else if (!uids.Add(account.Uid))
                throw new InvalidOperationException($"UID {account.Uid} 已用于其他账户，同一个 UID 只能建立一个账户。");
        }
    }

    /// <summary>解析账户图片路径，只允许访问账户图片目录内的文件。</summary>
    public string GetAccountImagePath(string relativePath)
    {
        var directory = Path.GetFullPath(Path.Combine(RootDirectory, AccountImageDirectoryName)) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        if (!resolved.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("账户图片必须位于 User/Pulonia/account-images 目录。");
        return resolved;
    }

    /// <summary>
    /// 为账户新建一张图片 <c>account-images\&lt;账户ID&gt;-&lt;yyyyMMdd-HHmmss&gt;.png</c>，返回相对于 User/Pulonia 的路径。
    /// 同名文件已存在时依次追加 <c>-2</c>、<c>-3</c>……；以排他方式创建，永远不覆盖已有文件。
    /// </summary>
    /// <remarks>
    /// 图片文件每次新建、不覆盖，已有运行快照引用的旧图片内容不会变化，因此运行快照只需记录 ImagePath，
    /// 不再另存版本副本或在运行前复核内容指纹。账户仍在时被替换的旧图片暂不清理；账户被删除时，
    /// 保存目录后一并删除它不再被引用的图片（见 <see cref="DeleteRemovedAccountImages"/>）。
    /// 自动采集与手动导入共用此方法。
    /// </remarks>
    /// <param name="accountId">图片所属账户 ID，作为文件名前缀</param>
    /// <param name="png">PNG 编码后的图片内容</param>
    /// <param name="ct">取消令牌</param>
    public async Task<string> CreateAccountImageAsync(string accountId, byte[] png, CancellationToken ct = default)
    {
        PuloniaTaskValidator.ValidateId(accountId, "account/id");
        ArgumentNullException.ThrowIfNull(png);
        Directory.CreateDirectory(Path.Combine(RootDirectory, AccountImageDirectoryName));
        var baseName = accountId + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        for (var index = 1; ; index++)
        {
            ct.ThrowIfCancellationRequested();
            // 第一个候选不带序号，同秒重名时从 -2 开始依次尝试。
            var fileName = (index == 1 ? baseName : baseName + "-" + index.ToString(CultureInfo.InvariantCulture)) + ".png";
            var relative = Path.Combine(AccountImageDirectoryName, fileName);
            var path = GetAccountImagePath(relative);
            FileStream stream;
            try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            try
            {
                using (stream)
                {
                    await stream.WriteAsync(png, ct).ConfigureAwait(false);
                    stream.Flush(true);
                }
            }
            catch
            {
                // 文件刚由本方法排他创建，尚未被任何账户引用；写入中断时删除残缺文件，删除失败则留待人工清理。
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
            return relative;
        }
    }
}
