namespace BetterGenshinImpact.ViewModel.Message;

/// <summary>录制状态变化，始终在 UI 线程广播。</summary>
/// <param name="IsRecording">是否处于启动或录制阶段。</param>
public sealed record KeyMouseRecordStateChangedMessage(bool IsRecording);

/// <summary>录制保存成功，通知发起方选择结果，其他界面刷新列表。</summary>
/// <param name="Path">已保存的录制文件绝对路径。</param>
/// <param name="Requester">仍然存活的发起方。</param>
public sealed record KeyMouseRecordSavedMessage(string Path, object? Requester);
