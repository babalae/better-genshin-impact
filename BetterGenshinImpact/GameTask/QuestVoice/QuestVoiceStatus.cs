namespace BetterGenshinImpact.GameTask.QuestVoice;

/// <summary>不可变显示快照；由实际运行事件更新，页面不推断设备就绪。</summary>
public sealed record QuestVoiceStatus
{
    /// <summary>会话正在接收采样。</summary>
    public bool IsRunning { get; init; }
    /// <summary>启停处理中。</summary>
    public bool IsBusy { get; init; }
    /// <summary>不确定的远端请求需要用户恢复后端。</summary>
    public bool BackendUnknown { get; init; }
    /// <summary>运行环境状态。</summary>
    public string Game { get; init; } = "未绑定游戏；请先在启动页启用截图";
    /// <summary>会话状态。</summary>
    public string Session { get; init; } = "已停止";
    /// <summary>捕获状态。</summary>
    public string Capture { get; init; } = "等待 BetterGI 已有帧";
    /// <summary>OCR 状态。</summary>
    public string Ocr { get; init; } = "未运行";
    /// <summary>TTS 状态。</summary>
    public string Tts { get; init; } = "未检测";
    /// <summary>实际播放状态。</summary>
    public string Playback { get; init; } = "空闲";
    /// <summary>OCR 姓名和规范角色名。</summary>
    public string Character { get; init; } = "—";
    /// <summary>实际选择的声线及兜底原因。</summary>
    public string Voice { get; init; } = "—";
    /// <summary>最近正式接受的正文。</summary>
    public string Dialogue { get; init; } = "尚未识别到稳定对白";
    /// <summary>实际选中的布局标识。</summary>
    public string Layout { get; init; } = "等待场景判定";
    /// <summary>静态配置检查结果。</summary>
    public string Validation { get; init; } = "尚未校验";
    /// <summary>路由目录摘要。</summary>
    public string Routing { get; init; } = "未读取";
    /// <summary>最近一条故障。</summary>
    public string Error { get; init; } = "";
    /// <summary>简短诊断；完整日志使用 BetterGI 主日志。</summary>
    public string Diagnostics { get; init; } = "完整日志见 BetterGI 主日志（QuestVoice）。";
}
