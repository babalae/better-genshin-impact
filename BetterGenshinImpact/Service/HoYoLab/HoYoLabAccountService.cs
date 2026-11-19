using System;

namespace BetterGenshinImpact.Service.HoYoLab;

/// <summary>
/// HoYoLAB 账号登录状态。启动时从加密文件恢复，登录/退出时通知订阅者。
/// </summary>
public class HoYoLabAccountService
{
    /// <summary>
    /// 会话存储
    /// </summary>
    private readonly HoYoLabSessionStore _store;

    /// <summary>
    /// 当前会话；未登录时为 null
    /// </summary>
    private HoYoLabSession? _session;

    /// <summary>
    /// 依赖注入入口，使用默认会话文件
    /// </summary>
    public HoYoLabAccountService() : this(new HoYoLabSessionStore(HoYoLabSessionStore.DefaultFilePath))
    {
    }

    /// <summary>
    /// 使用指定存储创建服务，并恢复已保存的会话
    /// </summary>
    /// <param name="store">会话存储</param>
    public HoYoLabAccountService(HoYoLabSessionStore store)
    {
        _store = store;
        _session = store.Load();
    }

    /// <summary>
    /// 登录状态变化（登录、退出、会话过期）
    /// </summary>
    public event EventHandler? SessionChanged;

    /// <summary>
    /// 是否已登录
    /// </summary>
    public bool IsLoggedIn => _session != null;

    /// <summary>
    /// 账号昵称，可能为空
    /// </summary>
    public string? Nickname => _session?.Nickname;

    /// <summary>
    /// 请求使用的 Cookie 请求头；未登录时为 null
    /// </summary>
    public string? CookieHeader => _session?.CookieHeader;

    /// <summary>
    /// 保存新的登录会话
    /// </summary>
    /// <param name="cookieHeader">Cookie 请求头</param>
    /// <param name="nickname">账号昵称</param>
    public void SetSession(string cookieHeader, string? nickname)
    {
        _session = new HoYoLabSession(cookieHeader, nickname);
        _store.Save(_session);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 退出登录并删除本地会话文件
    /// </summary>
    public void Logout()
    {
        _session = null;
        _store.Delete();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
