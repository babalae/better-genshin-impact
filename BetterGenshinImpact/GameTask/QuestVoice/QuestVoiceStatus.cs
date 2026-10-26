using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;

namespace BetterGenshinImpact.GameTask.QuestVoice;

/// <summary>自动配音流程单步节点状态。</summary>
public enum QuestVoiceStageState
{
    /// <summary>尚未开始（灰色）。</summary>
    Pending = 0,
    /// <summary>正在处理（蓝色）。</summary>
    InProgress = 1,
    /// <summary>已完成（绿色）。</summary>
    Completed = 2,
    /// <summary>异常（红色）。</summary>
    Failed = 3
}

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
    /// <summary>实际选择的声线及路由命中/兜底原因。</summary>
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

    /// <summary>运行控制区显示的总体状态（已停止 / 等待对白 / 运行中 / 异常）。</summary>
    public string OverallStatus { get; init; } = "已停止";
    /// <summary>总体状态对应的视觉指示分类。</summary>
    public QuestVoiceStageState OverallStatusState { get; init; } = QuestVoiceStageState.Pending;
    /// <summary>当前对白识别状态。</summary>
    public string DialogueState { get; init; } = "未运行";
    /// <summary>当前阶段简短摘要。</summary>
    public string StageSummary { get; init; } = "当前阶段：未启动";
    /// <summary>步骤 1：对话界面检测阶段状态。</summary>
    public QuestVoiceStageState SceneStage { get; init; } = QuestVoiceStageState.Pending;
    /// <summary>步骤 2：OCR 识别阶段状态。</summary>
    public QuestVoiceStageState OcrStage { get; init; } = QuestVoiceStageState.Pending;
    /// <summary>步骤 3：角色识别阶段状态。</summary>
    public QuestVoiceStageState SpeakerStage { get; init; } = QuestVoiceStageState.Pending;
    /// <summary>步骤 4：声线路由阶段状态。</summary>
    public QuestVoiceStageState RoutingStage { get; init; } = QuestVoiceStageState.Pending;
    /// <summary>步骤 5：TTS 合成阶段状态。</summary>
    public QuestVoiceStageState TtsStage { get; init; } = QuestVoiceStageState.Pending;
    /// <summary>步骤 6：音频播放阶段状态。</summary>
    public QuestVoiceStageState PlaybackStage { get; init; } = QuestVoiceStageState.Pending;
    /// <summary>最近发生的简要运行事件列表（最多保留 8 条）。</summary>
    public IReadOnlyList<string> RecentEvents { get; init; } = Array.Empty<string>();

    /// <summary>步骤 1 状态文本。</summary>
    public string SceneStageText => FormatStage(SceneStage);
    /// <summary>步骤 2 状态文本。</summary>
    public string OcrStageText => FormatStage(OcrStage);
    /// <summary>步骤 3 状态文本。</summary>
    public string SpeakerStageText => FormatStage(SpeakerStage);
    /// <summary>步骤 4 状态文本。</summary>
    public string RoutingStageText => FormatStage(RoutingStage);
    /// <summary>步骤 5 状态文本。</summary>
    public string TtsStageText => FormatStage(TtsStage);
    /// <summary>步骤 6 状态文本。</summary>
    public string PlaybackStageText => FormatStage(PlaybackStage);
    /// <summary>是否存在需要显示的错误消息。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    /// <summary>是否已有最近事件。</summary>
    public bool HasRecentEvents => RecentEvents.Count > 0;

    /// <summary>将阶段状态枚举映射为简短中文状态标签。</summary>
    public static string FormatStage(QuestVoiceStageState state) => state switch
    {
        QuestVoiceStageState.InProgress => "进行中",
        QuestVoiceStageState.Completed => "已完成",
        QuestVoiceStageState.Failed => "异常",
        _ => "未开始"
    };

    /// <summary>生成实际声线路由展示文本，明确区分映射命中、固定默认声线与兜底原因。</summary>
    public static string FormatRoutedVoice(VoiceRoutingResult? routing, string defaultVoiceName)
    {
        if (routing is null)
        {
            return $"{defaultVoiceName}（默认声线）";
        }
        if (routing.FallbackReason is { } reason)
        {
            return $"{routing.Voice.Name}（兜底：{reason}）";
        }
        var hitKind = routing.Speaker.Layer switch
        {
            RoutingLayer.Protagonist => "旅行者映射命中",
            RoutingLayer.Narrator => "旁白映射命中",
            RoutingLayer.Canonical => "角色映射命中",
            RoutingLayer.Alias => "角色别名命中",
            RoutingLayer.Correction => "角色纠错命中",
            _ => "默认声线"
        };
        return $"{routing.Voice.Name}（{hitKind}）";
    }
}
