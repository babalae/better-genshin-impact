namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>从账号切换脚本迁移的明确登录界面状态，不执行动态动作字符串。</summary>
internal enum PuloniaGenshinAccountScreenState
{
    /// <summary>未识别或正在加载，不能猜测点击。</summary>
    Unknown,
    /// <summary>游戏主界面。</summary>
    MainUi,
    /// <summary>派蒙菜单。</summary>
    Menu,
    /// <summary>退出至登录界面的确认框。</summary>
    LogoutConfirmation,
    /// <summary>点击进入游戏的门口界面。</summary>
    Gate,
    /// <summary>退出登录账号的确认框。</summary>
    ExitAccount,
    /// <summary>已收起列表的账号选择页。</summary>
    AccountSelection,
    /// <summary>已展开的登录账号列表。</summary>
    AccountList,
    /// <summary>输入账号密码的表单。</summary>
    PasswordForm,
    /// <summary>适龄提示。</summary>
    AgeConfirmation,
    /// <summary>无账号登录的门口界面。</summary>
    NoAccount
}
