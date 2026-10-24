namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 账号登录凭据的保存位置，只由账号存储在目录保存成功后调用。
/// 生产使用 Windows 凭据管理器；测试替换为内存实现，避免接触用户真实凭据。
/// </summary>
internal interface IPuloniaCredentialVault
{
    /// <summary>写入或覆盖同名凭据，失败时抛出异常。</summary>
    /// <param name="target">凭据目标名</param>
    /// <param name="userName">登录账号</param>
    /// <param name="password">登录密码</param>
    void Write(string target, string userName, string password);

    /// <summary>尽力删除凭据；凭据不存在或删除失败都不抛出异常，遗留条目不再被账号目录引用。</summary>
    /// <param name="target">凭据目标名</param>
    void Delete(string target);
}
