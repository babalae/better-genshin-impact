using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Agent;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class AgentPageViewModel(
    IConfigService configService,
    McpAgentService agentService,
    AgentSettingsDialogService settingsDialogService) : ViewModel
{
    public AgentConfig Config { get; } = configService.Get().AgentConfig;

    public ObservableCollection<AgentMessageItem> Messages { get; } = new(
        agentService.LoadConversation().Select(x => new AgentMessageItem(
            x.Role,
            x.Role == "assistant" ? "Agent" : "你",
            x.Content)));

    public ObservableCollection<string> ModelOptions { get; } = [];

    public ObservableCollection<AgentActivityItem> Activities { get; } = [];

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshModelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "填写外部接口地址和 API Key 后即可对话；模型留空会自动发现。";

    private CancellationTokenSource? _requestCancellation;

    private bool CanRefreshModels() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefreshModels))]
    private async Task RefreshModelsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "正在读取外部模型列表…";
        try
        {
            var models = await agentService.GetModelsAsync();
            ModelOptions.Clear();
            foreach (var model in models) ModelOptions.Add(model);
            if (string.IsNullOrWhiteSpace(Config.Model) && ModelOptions.Count > 0)
            {
                Config.Model = ModelOptions[0];
                configService.Save();
            }

            StatusText = $"已发现 {ModelOptions.Count} 个模型。";
        }
        catch (Exception ex)
        {
            StatusText = $"读取模型失败：{ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var prompt = InputText.Trim();
        if (IsBusy || string.IsNullOrWhiteSpace(prompt)) return;

        var history = Messages
            .Where(x => x.Role is "user" or "assistant")
            .Select(x => new AgentConversationMessage(x.Role, x.Content))
            .ToArray();
        Activities.Clear();
        Messages.Add(new AgentMessageItem("user", "你", prompt));
        var assistantMessage = new AgentMessageItem("assistant", "Agent", string.Empty) { IsStreaming = true };
        Messages.Add(assistantMessage);
        ClearCommand.NotifyCanExecuteChanged();
        InputText = string.Empty;
        IsBusy = true;
        StatusText = "Agent 正在思考并调用本地 BetterGI 工具…";
        _requestCancellation = new CancellationTokenSource();
        try
        {
            var result = await agentService.ChatStreamingAsync(
                history,
                prompt,
                async (streamEvent, streamCancellationToken) =>
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        switch (streamEvent.Type)
                        {
                            case "started":
                                StatusText = streamEvent.Message ?? "Agent 正在响应…";
                                break;
                            case "reset":
                                if (!string.IsNullOrWhiteSpace(assistantMessage.Content))
                                    AddActivity("过程", assistantMessage.Content.Trim());
                                assistantMessage.Content = string.Empty;
                                break;
                            case "tool_activity":
                                var tools = streamEvent.Tools is { Count: > 0 }
                                    ? string.Join("、", streamEvent.Tools)
                                    : "本地工具";
                                AddActivity("工具", tools);
                                StatusText = $"正在调用：{tools}";
                                break;
                            case "delta":
                                assistantMessage.Content += streamEvent.Delta;
                                StatusText = "正在生成回答…";
                                break;
                            case "final":
                                assistantMessage.Content = streamEvent.Delta ?? assistantMessage.Content;
                                assistantMessage.IsStreaming = false;
                                break;
                        }
                    }).Task;
                    if (streamEvent.Type == "delta")
                        await Task.Delay(10, streamCancellationToken);
                },
                _requestCancellation.Token);
            assistantMessage.DisplayRole = $"Agent · {result.Model}";
            assistantMessage.Content = result.Content;
            assistantMessage.IsStreaming = false;
            StatusText = $"完成：调用本地工具 {result.ToolCallCount} 次，可用工具 {result.AvailableToolCount} 个。";
        }
        catch (OperationCanceledException)
        {
            Messages.Remove(assistantMessage);
            Messages.Add(new AgentMessageItem("system", "系统", "本次外部 Agent 请求已取消。若 BetterGI 游戏任务已经启动，请另行使用停止任务工具。"));
            StatusText = "请求已取消。";
        }
        catch (Exception ex)
        {
            var error = ex.GetBaseException().Message;
            Messages.Remove(assistantMessage);
            Messages.Add(new AgentMessageItem("system", "错误", error));
            StatusText = $"Agent 失败：{error}";
        }
        finally
        {
            _requestCancellation.Dispose();
            _requestCancellation = null;
            IsBusy = false;
        }
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _requestCancellation?.Cancel();
    }

    private bool CanClear() => !IsBusy && Messages.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAsync()
    {
        if (IsBusy) return;
        await agentService.ClearConversationAsync();
        Messages.Clear();
        Activities.Clear();
        ClearCommand.NotifyCanExecuteChanged();
        StatusText = "对话记录已清空。";
    }

    [RelayCommand]
    private void OpenSystemPrompt()
    {
        try
        {
            var file = agentService.GetOrCreateUserPromptFile();
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            StatusText = $"已打开自定义提示词：{file}。保存后下一次消息自动生效。";
        }
        catch (Exception ex)
        {
            StatusText = $"打开提示词失败：{ex.GetBaseException().Message}";
        }
    }

    [RelayCommand]
    private void OpenSettings() => settingsDialogService.Show();

    private void AddActivity(string kind, string content)
    {
        var normalized = content.Length <= 500 ? content : content[..500] + "…";
        Activities.Add(new AgentActivityItem(kind, normalized));
        while (Activities.Count > 30) Activities.RemoveAt(0);
    }
}

public sealed record AgentActivityItem(string Kind, string Content);

public partial class AgentMessageItem : ObservableObject
{
    public string Role { get; }

    [ObservableProperty] private string _displayRole;

    [ObservableProperty] private string _content;

    [ObservableProperty] private bool _isStreaming;

    public AgentMessageItem(string role, string displayRole, string content)
    {
        Role = role;
        _displayRole = displayRole;
        _content = content;
    }
}