using System;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// Worker 对外可见的状态。Offline 由 Controller 在无法连接时使用，Worker 自身不会上报。
/// </summary>
public enum WorkerState
{
    Offline,
    Starting,
    Idle,
    RunningTask,
    Stopping,
    Error
}

/// <summary>
/// worker.status / task.status 的响应
/// </summary>
public sealed class WorkerStatusResponse
{
    public string WorkerSid { get; init; } = string.Empty;

    public int SessionId { get; init; }

    public int ProcessId { get; init; }

    public string InstanceId { get; init; } = string.Empty;

    public WorkerState State { get; init; }

    public string? CurrentTask { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Worker 自己的截图器（运行环境 + 遮罩叠加层）是否正在运行
    /// </summary>
    public bool CaptureRunning { get; init; }
}

/// <summary>
/// task.start 的任务类型。Worker 只接收类型 + 标识，任务内容（配置组文件、内置功能）都在 Worker 侧解析。
/// </summary>
public static class WorkerTaskTypes
{
    /// <summary>配置组（单个）</summary>
    public const string ScriptGroup = "scriptGroup";

    /// <summary>多个配置组连续执行（可循环）</summary>
    public const string ScriptGroups = "scriptGroups";

    /// <summary>按「任务进度」继续执行</summary>
    public const string TaskProgress = "taskProgress";

    /// <summary>一条龙</summary>
    public const string OneDragon = "oneDragon";

    /// <summary>内置独立任务（自动战斗、自动秘境等），标识见 <see cref="WorkerSoloTaskKeys"/></summary>
    public const string Solo = "solo";

    /// <summary>JS 脚本文件夹（同安装目录可直接调用）</summary>
    public const string ScriptFolder = "scriptFolder";

    /// <summary>地图追踪文件（同安装目录可直接调用）</summary>
    public const string PathingFile = "pathingFile";

    /// <summary>启动截图器并等待进入游戏主界面</summary>
    public const string StartGame = "startGame";
}

/// <summary>
/// 内置独立任务的标识。两侧共用，Controller 只发标识，Worker 用自己的配置构造任务。
/// </summary>
public static class WorkerSoloTaskKeys
{
    public const string AutoGeniusInvokation = "autoGeniusInvokation";
    public const string AutoWood = "autoWood";
    public const string AutoFight = "autoFight";
    public const string AutoDomain = "autoDomain";
    public const string AutoBoss = "autoBoss";
    public const string AutoStygianOnslaught = "autoStygianOnslaught";
    public const string AutoMusicGame = "autoMusicGame";
    public const string AutoAlbum = "autoAlbum";
    public const string AutoCook = "autoCook";
    public const string AutoCombo = "autoCombo";
    public const string AutoComboRun = "autoComboRun";
    public const string AutoFishing = "autoFishing";
    public const string AutoLeyLineOutcrop = "autoLeyLineOutcrop";
    public const string ArtifactSalvage = "artifactSalvage";
}

/// <summary>
/// Worker 回传给 Controller 的提示级别
/// </summary>
public enum WorkerNoticeLevel
{
    Information,
    Success,
    Warning,
    Error
}

/// <summary>
/// Worker 侧产生的用户提示（原本是 Toast）。无头 Worker 没有稳定的窗口承载 Toast，
/// 统一回传给 Controller 显示，Controller 断开时该提示只在 Worker 日志里留痕。
/// </summary>
public sealed class WorkerNotice
{
    /// <summary>Worker 进程内单调递增，便于 Controller 去重</summary>
    public long Sequence { get; init; }

    public WorkerNoticeLevel Level { get; init; }

    public string Message { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Worker 日志（原本写入遮罩叠加层日志框的内容）的显示位置。
/// <para>
/// 取值顺序与启动页下拉框的选项顺序一一对应，由单元测试守护；新增选项必须追加在末尾，
/// 否则会改变已落盘的 OtherConfig.WorkerLogDisplayMode 数值含义。
/// </para>
/// </summary>
public enum WorkerLogDisplayMode
{
    /// <summary>不显示</summary>
    None = 0,

    /// <summary>使用通知渠道：按聚合间隔合并后走通知设置里启用的渠道</summary>
    Notification = 1,

    /// <summary>游戏内叠加层：Worker 自己的遮罩日志框，改动前的行为</summary>
    GameOverlay = 2,

    /// <summary>远程独立窗口：在 Worker 侧弹出独立日志窗口</summary>
    RemoteWindow = 3,

    /// <summary>本地独立窗口：日志回传给 Controller，在控制侧弹出独立日志窗口</summary>
    LocalWindow = 4
}

/// <summary>
/// worker.logMode 请求：Controller 告诉 Worker 把日志显示在哪里
/// </summary>
public sealed class WorkerLogModeRequest
{
    /// <summary>
    /// 目标显示位置。为 null 表示请求缺少该字段（旧版 Controller 或手工构造的请求），Worker 会拒绝
    /// </summary>
    public WorkerLogDisplayMode? Mode { get; init; }

    /// <summary>走通知渠道时的聚合间隔（秒）</summary>
    public int NotificationIntervalSeconds { get; init; }
}

/// <summary>
/// worker.logMode 响应：Worker 实际生效的设置
/// </summary>
public sealed class WorkerLogModeResponse
{
    public WorkerLogDisplayMode Mode { get; init; }

    public int NotificationIntervalSeconds { get; init; }
}

/// <summary>
/// worker.log 批次：Worker → Controller 的日志行
/// </summary>
public sealed class WorkerLogBatch
{
    /// <summary>Worker 进程内单调递增，便于 Controller 排查丢批</summary>
    public long Sequence { get; init; }

    public string[] Lines { get; init; } = [];
}

/// <summary>
/// task.start 请求
/// </summary>
public sealed class WorkerTaskStartRequest
{
    /// <summary>
    /// 任务类型，见 <see cref="WorkerTaskTypes"/>
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// 单个标识：配置组名 / JS 脚本文件夹名 / 地图追踪文件名 / 一条龙配置名 / 独立任务标识
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// scriptGroups：按顺序连续执行的配置组名
    /// </summary>
    public string[]? Names { get; init; }

    /// <summary>
    /// scriptGroups：是否循环执行
    /// </summary>
    public bool Loop { get; init; }

    /// <summary>
    /// taskProgress：任务进度名称（latest 表示最近一次）
    /// </summary>
    public string? ProgressName { get; init; }

    /// <summary>
    /// pathingFile：相对安装目录的目录
    /// </summary>
    public string? Directory { get; init; }
}

/// <summary>
/// task.start 响应
/// </summary>
public sealed class WorkerTaskStartResponse
{
    public string Task { get; init; } = string.Empty;

    public WorkerState State { get; init; }
}

/// <summary>
/// task.stop / task.pause / task.resume 响应
/// </summary>
public sealed class WorkerCommandResponse
{
    public WorkerState State { get; init; }
}

/// <summary>Worker 当前游戏画面截图（JPEG Base64）。</summary>
public sealed class WorkerScreenshotResponse
{
    public string? ScreenshotBase64 { get; init; }
}

/// <summary>
/// Controller 连接或请求 Worker 失败。
/// </summary>
public sealed class WorkerUnavailableException : System.Exception
{
    public WorkerUnavailableException(string errorCode, string message, System.Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// worker_offline：Worker 未运行或管道不可达；connection_failed：连接或对端校验失败；request_failed：Worker 拒绝请求
    /// </summary>
    public string ErrorCode { get; }
}
