namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>刷至耗尽时使用补充树脂的确认内容。</summary>
/// <param name="Title">确认标题。</param>
/// <param name="Message">确认正文。</param>
public sealed record ResinConfirmation(string Title, string Message);
