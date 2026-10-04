using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Service.ExternalAccess;

/// <summary>
/// 外部访问服务生命周期状态。
/// </summary>
public enum ExternalAccessServiceStatus
{
    /// <summary>全部外部访问能力均未启用。</summary>
    Disabled,

    /// <summary>服务正在启动。</summary>
    Starting,

    /// <summary>服务正在运行。</summary>
    Running,

    /// <summary>服务启动或运行失败。</summary>
    Failed,

    /// <summary>服务已停止。</summary>
    Stopped
}

/// <summary>
/// 向设置页面公开外部访问服务的只读运行状态。
/// </summary>
public sealed class ExternalAccessState : ObservableObject
{
    /// <summary>
    /// 当前生命周期状态。
    /// </summary>
    private ExternalAccessServiceStatus _status = ExternalAccessServiceStatus.Disabled;

    /// <summary>
    /// 当前状态的补充说明。
    /// </summary>
    private string _message = "外部访问服务未启用";

    /// <summary>
    /// 当前服务监听地址。
    /// </summary>
    private string _address = "未启动";

    /// <summary>
    /// 当前生命周期状态。
    /// </summary>
    public ExternalAccessServiceStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>
    /// 当前状态的本地化展示文本。
    /// </summary>
    public string StatusText => Status switch
    {
        ExternalAccessServiceStatus.Disabled => "未启用",
        ExternalAccessServiceStatus.Starting => "正在启动",
        ExternalAccessServiceStatus.Running => "运行中",
        ExternalAccessServiceStatus.Failed => "启动失败",
        ExternalAccessServiceStatus.Stopped => "已停止",
        _ => "未知"
    };

    /// <summary>
    /// 当前状态的补充说明。
    /// </summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>
    /// 当前服务监听地址。
    /// </summary>
    public string Address
    {
        get => _address;
        private set => SetProperty(ref _address, value);
    }

    /// <summary>
    /// 原子更新设置页面展示的服务状态。
    /// </summary>
    /// <param name="status">新的生命周期状态。</param>
    /// <param name="message">状态补充说明。</param>
    /// <param name="address">当前监听地址。</param>
    public void Update(ExternalAccessServiceStatus status, string message, string address)
    {
        Status = status;
        Message = message;
        Address = address;
    }
}
