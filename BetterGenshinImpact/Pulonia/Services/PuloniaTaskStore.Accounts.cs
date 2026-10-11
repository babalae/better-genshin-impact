using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>复用计划存储的写锁、修订校验与原子替换保存原神账号目录。</summary>
public sealed partial class PuloniaTaskStore
{
    /// <summary>读取独立账号草稿；首次使用不要求创建任何账号。</summary>
    public async Task<PuloniaGenshinAccountSettings> LoadAccountSettingsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await ReadAccountSettingsAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>校验全部账号后一次耐久保存，失败时不替换界面草稿。</summary>
    public async Task<PuloniaGenshinAccountSettings> SaveAccountSettingsAsync(PuloniaGenshinAccountSettings settings,
        CancellationToken ct = default)
    {
        var candidate = PuloniaTaskJson.Read<PuloniaGenshinAccountSettings>(PuloniaTaskJson.Write(settings));
        ValidateAccountSettings(candidate);
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
            return candidate;
        }
        finally { _gate.Release(); }
    }

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
        var directory = Path.GetFullPath(Path.Combine(RootDirectory, "account-images")) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        if (!resolved.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("账号图片必须位于 User/Pulonia/account-images 目录。");
        return resolved;
    }

    /// <summary>将图片内容固定到版本目录，后续替换账号图片不改变运行快照。</summary>
    public PuloniaGenshinAccountProfile FreezeAccount(PuloniaGenshinAccountProfile account)
    {
        var candidate = PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(PuloniaTaskJson.Write(account));
        if (candidate.ImagePath is null) return candidate;
        var source = GetAccountImagePath(candidate.ImagePath);
        var bytes = File.ReadAllBytes(source);
        var version = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var relative = Path.Combine("account-images", "versions", version + ".png");
        var target = GetAccountImagePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // 不可变内容使用排他创建；并发提交同一图片时已有文件可直接复用。
        try
        {
            using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush(true);
        }
        catch (IOException) when (File.Exists(target))
        {
            if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) throw;
        }
        candidate.ImagePath = relative;
        candidate.ImageVersion = version;
        return candidate;
    }
}
