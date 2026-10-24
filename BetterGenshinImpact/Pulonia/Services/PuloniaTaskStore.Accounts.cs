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

/// <summary>复用计划存储的写锁、修订校验与原子替换保存原神账号目录，并在目录保存成功后提交登录凭据。</summary>
public sealed partial class PuloniaTaskStore
{
    /// <summary>账号图片目录名，相对于 User/Pulonia。</summary>
    private const string AccountImageDirectoryName = "account-images";
    /// <summary>Windows 凭据目标名前缀，后接账号 ID；同一账号的目标名固定，改密码时覆盖原条目。</summary>
    private const string CredentialTargetPrefix = "BetterGenshinImpact.Pulonia.Account.";

    /// <summary>登录凭据的保存位置；默认使用 Windows 凭据管理器，测试可替换为内存实现。</summary>
    internal IPuloniaCredentialVault CredentialVault { get; init; } = new PuloniaWindowsCredentialVault();

    /// <summary>读取独立账号草稿；首次使用不要求创建任何账号。</summary>
    public async Task<PuloniaGenshinAccountSettings> LoadAccountSettingsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await ReadAccountSettingsAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>校验全部账号后一次耐久保存，不写入新的登录凭据；失败时不替换界面草稿。</summary>
    /// <param name="settings">要保存的账号目录草稿，调用方对象不会被修改</param>
    /// <param name="ct">取消令牌</param>
    public async Task<PuloniaGenshinAccountSettings> SaveAccountSettingsAsync(PuloniaGenshinAccountSettings settings,
        CancellationToken ct = default)
        => (await SaveAccountSettingsAsync(settings, new Dictionary<string, (string UserName, string Password)>(), ct)
            .ConfigureAwait(false)).Settings;

    /// <summary>
    /// 校验后先耐久保存账号目录，成功后再提交登录凭据：写入本次填写的凭据，删除目录不再引用的旧凭据。
    /// 目录保存失败时不触碰任何凭据，不会留下孤立条目。
    /// </summary>
    /// <remarks>
    /// 运行快照只记录凭据目标名，排队或续跑的运行在登录时读取当前凭据：改密码后旧运行使用新密码；
    /// 账号被移除或改用不使用密码的登录方式后，旧运行也无法再用密码登录。
    /// </remarks>
    /// <param name="settings">要保存的账号目录草稿，调用方对象不会被修改</param>
    /// <param name="credentials">以账号 ID 为键的新登录凭据；未列出的账号保留已保存凭据</param>
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
            using var writerLock = AcquireWriterLock();
            var current = await ReadAccountSettingsAsync(ct).ConfigureAwait(false);
            if (candidate.Revision != current.Revision)
                throw new InvalidOperationException("账号目录已被其他编辑修改，请重新打开账号管理。");
            candidate.Revision = checked(current.Revision + 1);
            await WriteAtomicallyAsync(Path.Combine(RootDirectory, "accounts.json"), PuloniaTaskJson.Write(candidate), ct)
                .ConfigureAwait(false);
            // 目录已耐久保存，此后才提交凭据；仍在写锁内，避免与下一次保存交错。
            return new PuloniaGenshinAccountSaveResult(candidate, CommitCredentials(current, candidate, credentials));
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 按本次凭据草稿和登录方式确定每个账号的凭据引用：填写了凭据的账号指向固定目标名，
    /// 不使用密码的登录方式（账号图片、列表文字）不保留引用，保存成功后删除其旧凭据。
    /// </summary>
    /// <param name="candidate">已通过校验的目录副本</param>
    /// <param name="credentials">以账号 ID 为键的新登录凭据</param>
    private static void ApplyCredentialReferences(PuloniaGenshinAccountSettings candidate,
        IReadOnlyDictionary<string, (string UserName, string Password)> credentials)
    {
        foreach (var (id, credential) in credentials)
        {
            var account = candidate.Accounts.SingleOrDefault(item => item.Id == id && !item.IsDefault)
                          ?? throw new InvalidOperationException("登录凭据对应的账号不存在，或为不需要登录资料的默认账号。");
            if (string.IsNullOrWhiteSpace(credential.UserName) || string.IsNullOrEmpty(credential.Password))
                throw new InvalidOperationException("保存登录凭据时必须同时填写账号和密码；无需更新凭据时请留空。");
            if (!UsesPassword(account.LoginMode))
                throw new InvalidOperationException($"账号“{account.Name}”的登录方式不使用密码，请清空账号和密码，或改用自动或账号密码方式。");
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
    /// <param name="credentials">以账号 ID 为键的新登录凭据</param>
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
        // 账号被移除或改用无密码方式后，旧凭据不再被目录引用；删除是尽力而为，遗留条目不影响登录。
        var referenced = saved.Accounts.Select(item => item.CredentialReference).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var target in previous.Accounts.Select(item => item.CredentialReference).OfType<string>()
                     .Where(target => !referenced.Contains(target)).Distinct(StringComparer.Ordinal))
            CredentialVault.Delete(target);
        return failures.Count == 0
            ? null
            : "以下账号的登录凭据未能写入，请重新填写后保存：" + string.Join("；", failures);
    }

    /// <summary>登录方式是否可能使用密码；自动方式把密码作为图片和列表文字之后的备用。</summary>
    /// <param name="mode">账号的登录方式</param>
    private static bool UsesPassword(PuloniaGenshinAccountLoginMode mode)
        => mode is PuloniaGenshinAccountLoginMode.Automatic or PuloniaGenshinAccountLoginMode.Password;

    /// <summary>账号固定的 Windows 凭据目标名。</summary>
    /// <param name="accountId">账号 ID</param>
    internal static string GetCredentialTarget(string accountId) => CredentialTargetPrefix + accountId;

    /// <summary>读取耐久资料；损坏时明确报错，不能偷偷换成默认账号。</summary>
    private async Task<PuloniaGenshinAccountSettings> ReadAccountSettingsAsync(CancellationToken ct)
    {
        var path = Path.Combine(RootDirectory, "accounts.json");
        var settings = File.Exists(path)
            ? PuloniaTaskJson.Read<PuloniaGenshinAccountSettings>(await File.ReadAllTextAsync(path, Utf8WithoutBom, ct).ConfigureAwait(false))
            : new PuloniaGenshinAccountSettings();
        ValidateAccountSettings(settings);
        return settings;
    }

    /// <summary>校验身份、UID 和默认账号，避免数据错误导致选择错误账号。</summary>
    internal static void ValidateAccountSettings(PuloniaGenshinAccountSettings settings)
    {
        if (settings.Revision < 0 || settings.Accounts is null || settings.Accounts.Any(a => a is null))
            throw new InvalidOperationException("账号目录格式无效。");
        if (settings.Accounts.Count(a => a.IsDefault) != 1 || settings.Accounts.Select(a => a.Id).Distinct().Count() != settings.Accounts.Count)
            throw new InvalidOperationException("账号身份不能重复，默认账号必须且只能存在一个。");
        foreach (var account in settings.Accounts)
        {
            PuloniaTaskValidator.ValidateId(account.Id, "account/id");
            if (string.IsNullOrWhiteSpace(account.Name) || !Enum.IsDefined(account.LoginMode) || account.ListText is null)
                throw new InvalidOperationException("账号名称或登录方式无效。");
            if (account.IsDefault)
            {
                if (account.Uid is not null || account.ImagePath is not null || account.CredentialReference is not null || account.ListText.Length != 0)
                    throw new InvalidOperationException("默认账号使用当前登录态，不能设置登录资料或目标 UID。");
            }
            else if (!Regex.IsMatch(account.Uid ?? "", "^[0-9]{9,10}$"))
                throw new InvalidOperationException($"账号“{account.Name}”需要填写完整的 9 或 10 位 UID。");
        }
        if (settings.Accounts.Where(a => !a.IsDefault).Select(a => a.Uid).Distinct().Count() != settings.Accounts.Count - 1)
            throw new InvalidOperationException("同一个 UID 只需建立一份账号资料，可以供多个配置复用。");
    }

    /// <summary>解析账号图片路径，只允许访问账号图片目录内的文件。</summary>
    public string GetAccountImagePath(string relativePath)
    {
        var directory = Path.GetFullPath(Path.Combine(RootDirectory, AccountImageDirectoryName)) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        if (!resolved.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("账号图片必须位于 User/Pulonia/account-images 目录。");
        return resolved;
    }

    /// <summary>
    /// 为账号新建一张图片 <c>account-images\&lt;账号ID&gt;-&lt;yyyyMMdd-HHmmss&gt;.png</c>，返回相对于 User/Pulonia 的路径。
    /// 同名文件已存在时依次追加 <c>-2</c>、<c>-3</c>……；以排他方式创建，永远不覆盖已有文件。
    /// </summary>
    /// <remarks>
    /// 图片文件每次新建、不覆盖，已有运行快照引用的旧图片内容不会变化，因此运行快照只需记录 ImagePath，
    /// 不再另存版本副本或在运行前复核内容指纹。被替换或随账号删除而不再引用的旧图片暂不清理。
    /// 自动采集与手动导入共用此方法。
    /// </remarks>
    /// <param name="accountId">图片所属账号 ID，作为文件名前缀</param>
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
                // 文件刚由本方法排他创建，尚未被任何账号引用；写入中断时删除残缺文件，删除失败则留待人工清理。
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
            return relative;
        }
    }
}
