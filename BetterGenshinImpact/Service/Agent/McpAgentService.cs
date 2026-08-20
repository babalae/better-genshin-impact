using System.ClientModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;

#pragma warning disable MAAI001 // Microsoft Agent Framework 1.18 的 Compaction API 仍标记为 evaluation。

namespace BetterGenshinImpact.Service.Agent;

/// <summary>
/// 基于 Microsoft Agent Framework 的 BetterGI Agent。
/// Agent Framework 负责会话与 function-calling 循环，本服务只负责配置和连接本机 MCP 工具。
/// </summary>
public sealed class McpAgentService(IConfigService configService)
{
    private readonly SemaphoreSlim _conversationLock = new(1, 1);
    private readonly McpSettingsCatalog _settingsCatalog = new();
    private AgentRuntime? _runtime;

    private static string ConversationFile => Global.Absolute(@"User\Agent\conversation.json");
    private static string SessionFile => Global.Absolute(@"User\Agent\session.json");

    public IReadOnlyList<AgentConversationMessage> LoadConversation()
    {
        try
        {
            if (!File.Exists(ConversationFile)) return [];
            var messages = JsonSerializer.Deserialize<List<AgentConversationMessage>>(
                       File.ReadAllText(ConversationFile), ConfigService.JsonOptions)
                   ?? [];
            return LimitPersistedConversation(messages, configService.Get().AgentConfig);
        }
        catch
        {
            return [];
        }
    }

    public void SaveConversation(IEnumerable<AgentConversationMessage> messages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConversationFile)!);
        var persisted = LimitPersistedConversation(messages, configService.Get().AgentConfig);
        File.WriteAllText(ConversationFile, JsonSerializer.Serialize(persisted, ConfigService.JsonOptions));
    }

    public async Task ClearConversationAsync(CancellationToken cancellationToken = default)
    {
        await _conversationLock.WaitAsync(cancellationToken);
        try
        {
            if (_runtime is not null)
            {
                await _runtime.DisposeAsync();
                _runtime = null;
            }
            if (File.Exists(ConversationFile)) File.Delete(ConversationFile);
            if (File.Exists(SessionFile)) File.Delete(SessionFile);
        }
        finally
        {
            _conversationLock.Release();
        }
    }

    internal static IReadOnlyList<AgentConversationMessage> LimitPersistedConversation(
        IEnumerable<AgentConversationMessage> messages,
        AgentConfig config)
    {
        var maxMessages = Math.Clamp(config.MaxPersistedMessages, 10, 500);
        var maxTotalCharacters = Math.Clamp(config.MaxPersistedCharacters, 10000, 2_000_000);
        var maxMessageCharacters = Math.Clamp(config.MaxPersistedMessageCharacters, 1000, 200_000);
        var limited = messages
            .Where(x => x.Role is "user" or "assistant")
            .Select(x => new AgentConversationMessage(x.Role, LimitMessage(x.Content, maxMessageCharacters)))
            .TakeLast(maxMessages)
            .ToList();
        var total = limited.Sum(x => x.Content.Length);
        while (limited.Count > 2 && total > maxTotalCharacters)
        {
            total -= limited[0].Content.Length;
            limited.RemoveAt(0);
        }
        return limited;
    }

    private static string LimitMessage(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters) return value;
        var marker = $"\n\n[BetterGI 会话缓存已省略中间内容；原始长度 {value.Length}]\n\n";
        var contentBudget = Math.Max(2, maxCharacters - marker.Length);
        var headLength = (int)(contentBudget * 0.65);
        var tailLength = contentBudget - headLength;
        return value[..headLength] + marker + value[^tailLength..];
    }

    public string GetOrCreateUserPromptFile()
    {
        var userFile = Global.Absolute(@"User\Agent\system-prompt.md");
        if (File.Exists(userFile)) return userFile;
        Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
        var bundledFile = Global.Absolute(@"Assets\Config\AgentSystemPrompt.md");
        var content = File.Exists(bundledFile)
            ? File.ReadAllText(bundledFile)
            : "你是 BetterGI 内置 Agent。使用本机 MCP 工具读取真实状态后完成用户请求，不要猜路径或设置。";
        File.WriteAllText(userFile, content);
        return userFile;
    }

    public AgentServiceStatus GetStatus()
    {
        var config = configService.Get().AgentConfig;
        var userPrompt = Global.Absolute(@"User\Agent\system-prompt.md");
        return new AgentServiceStatus(
            !string.IsNullOrWhiteSpace(config.BaseUrl),
            !string.IsNullOrWhiteSpace(config.ApiKey),
            config.BaseUrl,
            config.Model,
            LoadConversation().Count,
            _runtime?.HasConversation == true,
            File.Exists(userPrompt) ? userPrompt : Global.Absolute(@"Assets\Config\AgentSystemPrompt.md"),
            config.CompactionTriggerMessages,
            config.CompactionPreserveRecentGroups,
            config.HardTruncationMessages,
            File.Exists(SessionFile) ? new FileInfo(SessionFile).Length : 0);
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var config = configService.Get().AgentConfig;
        var endpoints = ResolveEndpoints(config.BaseUrl);
        using var httpClient = CreateExternalHttpClient(config.ApiKey, endpoints.Provider);
        using var response = await httpClient.GetAsync(endpoints.Models, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (endpoints.Provider == AgentProviderKind.Anthropic
                && endpoints.ApiRoot.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase))
                return ["deepseek-v4-flash", "deepseek-v4-pro"];
            throw new InvalidOperationException($"模型列表请求失败 ({(int)response.StatusCode})：{TrimError(body)}");
        }
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("外部接口 /models 响应没有 data 数组；请手动填写模型 ID。");
        return data.EnumerateArray()
            .Where(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(x => x.GetProperty("id").GetString()!)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<AgentChatResult> ChatAsync(
        IReadOnlyList<AgentConversationMessage> history,
        string userMessage,
        CancellationToken cancellationToken = default) =>
        ChatStreamingAsync(history, userMessage, null, cancellationToken);

    public async Task<AgentChatResult> ChatStreamingAsync(
        IReadOnlyList<AgentConversationMessage> history,
        string userMessage,
        Func<AgentStreamEvent, CancellationToken, ValueTask>? onEvent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) throw new ArgumentException("消息不能为空。", nameof(userMessage));
        await _conversationLock.WaitAsync(cancellationToken);
        try
        {
            var visibleHistory = history
                .Where(x => x.Role is "user" or "assistant")
                .TakeLast(99)
                .ToArray();
            SaveConversation(visibleHistory.Append(new AgentConversationMessage("user", userMessage)));
            var config = configService.Get().AgentConfig;
            var endpoints = ResolveEndpoints(config.BaseUrl);
            var model = config.Model;
            if (string.IsNullOrWhiteSpace(model))
            {
                try
                {
                    model = (await GetModelsAsync(cancellationToken)).FirstOrDefault();
                }
                catch when (endpoints.Provider == AgentProviderKind.Anthropic
                            && endpoints.ApiRoot.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase))
                {
                    model = "deepseek-v4-flash";
                }
                if (string.IsNullOrWhiteSpace(model))
                    throw new InvalidOperationException("外部接口没有返回可用模型。");
                config.Model = model;
                configService.Save();
            }
            var runtimeKey = $"{endpoints.Provider}\n{endpoints.ApiRoot}\n{config.ApiKey}\n{model}\n{Math.Clamp(config.MaxToolRounds, 1, 30)}\n{config.CompactionTriggerMessages}\n{config.CompactionPreserveRecentGroups}\n{config.HardTruncationMessages}";
            if (_runtime is null || !_runtime.Key.Equals(runtimeKey, StringComparison.Ordinal))
            {
                if (_runtime is not null) await _runtime.DisposeAsync();
                _runtime = await CreateRuntime(
                    runtimeKey,
                    endpoints,
                    model,
                    config.ApiKey,
                    config.MaxToolRounds,
                    config.CompactionTriggerMessages,
                    config.CompactionPreserveRecentGroups,
                    config.HardTruncationMessages,
                    cancellationToken);
            }

            AgentResponse response;
            var runOptions = _runtime.CreateRunOptions(BuildSystemPrompt(_runtime.ToolCount));
            IAsyncEnumerable<AgentResponseUpdate> responseStream;
            if (!_runtime.HasConversation)
            {
                var messages = history.TakeLast(40)
                    .Select(item => new ChatMessage(
                        item.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
                        item.Content))
                    .Append(new ChatMessage(ChatRole.User, userMessage))
                    .ToArray();
                responseStream = _runtime.Agent.RunStreamingAsync(messages, _runtime.Session, runOptions, cancellationToken);
                _runtime.HasConversation = true;
            }
            else
            {
                responseStream = _runtime.Agent.RunStreamingAsync(userMessage, _runtime.Session, runOptions, cancellationToken);
            }
            if (onEvent is not null)
                await onEvent(new AgentStreamEvent("started", null, $"正在使用 {model}", null), cancellationToken);

            var updates = new List<AgentResponseUpdate>();
            await foreach (var update in responseStream.WithCancellation(cancellationToken))
            {
                updates.Add(update);
                var functionCalls = update.Contents.OfType<FunctionCallContent>().ToArray();
                if (functionCalls.Length > 0 && onEvent is not null)
                {
                    await onEvent(new AgentStreamEvent("reset", null, null, null), cancellationToken);
                    await onEvent(new AgentStreamEvent(
                        "tool_activity",
                        null,
                        "正在执行本地工具",
                        functionCalls.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()), cancellationToken);
                }
                if (!string.IsNullOrEmpty(update.Text) && onEvent is not null)
                    await onEvent(new AgentStreamEvent("delta", update.Text, null, null), cancellationToken);
            }
            response = updates.ToAgentResponse();
            var toolCallCount = response.Messages
                .SelectMany(x => x.Contents)
                .OfType<FunctionCallContent>()
                .Count();
            var finalText = GetFinalAssistantText(response);
            SaveConversation(visibleHistory
                .Append(new AgentConversationMessage("user", userMessage))
                .Append(new AgentConversationMessage("assistant", finalText)));
            await PersistSessionAsync(_runtime, cancellationToken);
            if (onEvent is not null)
                await onEvent(new AgentStreamEvent("final", finalText, "完成", null), cancellationToken);
            return new AgentChatResult(finalText, model, toolCallCount, _runtime.ToolCount);
        }
        catch
        {
            if (_runtime is not null)
            {
                await _runtime.DisposeAsync();
                _runtime = null;
            }
            throw;
        }
        finally
        {
            _conversationLock.Release();
        }
    }

    private async Task<AgentRuntime> CreateRuntime(
        string key,
        AgentEndpoints endpoints,
        string model,
        string? apiKey,
        int maxToolRounds,
        int compactionTriggerMessages,
        int preserveRecentGroups,
        int hardTruncationMessages,
        CancellationToken cancellationToken)
    {
        var mcpClient = await CreateLocalMcpClient(cancellationToken);
        try
        {
            var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
            var tools = mcpTools
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => (AITool)x.First())
                .ToArray();
            var triggerMessages = Math.Clamp(compactionTriggerMessages, 20, 500);
            var preservedGroups = Math.Clamp(preserveRecentGroups, 4, Math.Max(4, triggerMessages / 2));
            var hardLimit = Math.Clamp(hardTruncationMessages, triggerMessages + 10, 1000);
            IChatClient? functionInvokingClient = null;
            IDisposable? providerClient = null;
            ChatClientAgent agent;

            if (endpoints.Provider == AgentProviderKind.Anthropic)
            {
                var anthropicClient = new AnthropicClient(new ClientOptions
                {
                    BaseUrl = endpoints.ApiRoot.ToString().TrimEnd('/'),
                    ApiKey = string.IsNullOrWhiteSpace(apiKey) ? "local-no-key" : apiKey.Trim(),
                    Timeout = TimeSpan.FromMinutes(5),
                });
                providerClient = anthropicClient as IDisposable;
                var agentOptions = CreateAgentOptions(model, tools, null);
                agent = anthropicClient.AsAIAgent(
                    agentOptions,
                    clientFactory: innerClient =>
                    {
                        agentOptions.AIContextProviders =
                        [
                            new CompactionProvider(
                                CreateCompactionStrategy(innerClient, triggerMessages, preservedGroups, hardLimit),
                                "BetterGI.Agent.Compaction",
                                null),
                        ];
                        functionInvokingClient = CreateFunctionInvokingClient(innerClient, maxToolRounds);
                        return functionInvokingClient;
                    });
            }
            else
            {
                var openAiClient = new OpenAIClient(
                    new ApiKeyCredential(string.IsNullOrWhiteSpace(apiKey) ? "local-no-key" : apiKey.Trim()),
                    new OpenAIClientOptions { Endpoint = endpoints.ApiRoot });
                var baseChatClient = openAiClient.GetChatClient(model).AsIChatClient();
                functionInvokingClient = CreateFunctionInvokingClient(baseChatClient, maxToolRounds);
                agent = new ChatClientAgent(
                    functionInvokingClient,
                    CreateAgentOptions(
                        model,
                        tools,
                        new CompactionProvider(
                            CreateCompactionStrategy(baseChatClient, triggerMessages, preservedGroups, hardLimit),
                            "BetterGI.Agent.Compaction",
                            null)));
            }

            if (functionInvokingClient is null)
                throw new InvalidOperationException("Agent Provider 未能创建 function-invoking chat client。");
            var sessionResult = await RestoreOrCreateSession(agent, key, cancellationToken);
            var runtime = new AgentRuntime(key, model, tools, mcpClient, functionInvokingClient, providerClient, agent, sessionResult.Session)
            {
                HasConversation = sessionResult.Restored,
            };
            return runtime;
        }
        catch
        {
            await mcpClient.DisposeAsync();
            throw;
        }
    }

    private ChatClientAgentOptions CreateAgentOptions(
        string model,
        IReadOnlyList<AITool> tools,
        CompactionProvider? compactionProvider)
    {
        return new ChatClientAgentOptions
        {
            Id = "bettergi-local-agent",
            Name = "BetterGI Agent",
            Description = "通过本机 MCP 管理 BetterGI 设置、仓库、脚本、调度器和游戏任务。",
            UseProvidedChatClientAsIs = true,
            AIContextProviders = compactionProvider is null ? [] : [compactionProvider],
            ChatOptions = new ChatOptions
            {
                ModelId = model,
                Instructions = BuildSystemPrompt(tools.Count),
                ToolMode = ChatToolMode.Auto,
                Tools = [.. tools],
                MaxOutputTokens = 8192,
            },
        };
    }

    private static IChatClient CreateFunctionInvokingClient(IChatClient innerClient, int maxToolRounds) =>
        new ChatClientBuilder(innerClient)
            .UseFunctionInvocation(configure: client =>
            {
                client.MaximumIterationsPerRequest = Math.Clamp(maxToolRounds, 1, 30) + 1;
                client.MaximumConsecutiveErrorsPerRequest = 3;
                client.IncludeDetailedErrors = true;
                client.AllowConcurrentInvocation = false;
            })
            .Build();

    private static PipelineCompactionStrategy CreateCompactionStrategy(
        IChatClient summaryClient,
        int triggerMessages,
        int preservedGroups,
        int hardLimit) =>
        new(
        [
            new ToolResultCompactionStrategy(
                CompactionTriggers.MessagesExceed(Math.Max(16, triggerMessages / 2)),
                preservedGroups,
                null),
            new SummarizationCompactionStrategy(
                summaryClient,
                CompactionTriggers.MessagesExceed(triggerMessages),
                preservedGroups,
                """
                将较早的 BetterGI Agent 对话压缩成简洁、可继续执行的中文记忆。必须保留：
                - 用户明确目标、偏好、确认和限制；
                - 已选择的脚本、仓库精确路径、groupName、folderName、projectIndex；
                - 已修改设置的精确 path、旧值/新值和验证结果；
                - 已完成、仍在运行、失败或待处理的任务状态；
                - 避免重复搜索或重复执行所需的工具结论。
                不要保留大段原始 JSON、重复日志、完整工具 Schema 或已经失效的临时候选。
                """,
                null),
            new TruncationCompactionStrategy(
                CompactionTriggers.MessagesExceed(hardLimit),
                preservedGroups,
                null),
        ]);

    private static async Task<SessionRestoreResult> RestoreOrCreateSession(
        ChatClientAgent agent,
        string runtimeKey,
        CancellationToken cancellationToken)
    {
        var keyHash = ComputeKeyHash(runtimeKey);
        try
        {
            if (File.Exists(SessionFile))
            {
                var envelope = JsonSerializer.Deserialize<AgentSessionEnvelope>(
                    File.ReadAllText(SessionFile), ConfigService.JsonOptions);
                if (envelope is not null && envelope.RuntimeKeyHash == keyHash)
                {
                    var session = await agent.DeserializeSessionAsync(
                        envelope.Session,
                        ConfigService.JsonOptions,
                        cancellationToken);
                    return new SessionRestoreResult(session, true);
                }
            }
        }
        catch
        {
            // Session 格式或 Provider 配置变化时回退为新会话；可见对话仍会用于恢复。
        }
        return new SessionRestoreResult(await agent.CreateSessionAsync(cancellationToken: cancellationToken), false);
    }

    private async Task PersistSessionAsync(AgentRuntime runtime, CancellationToken cancellationToken)
    {
        var session = await runtime.Agent.SerializeSessionAsync(
            runtime.Session,
            ConfigService.JsonOptions,
            cancellationToken);
        var envelope = new AgentSessionEnvelope(ComputeKeyHash(runtime.Key), session);
        var json = JsonSerializer.Serialize(envelope, ConfigService.JsonOptions);
        var maxCharacters = Math.Clamp(
            configService.Get().AgentConfig.MaxSerializedSessionCharacters,
            50000,
            5_000_000);
        if (json.Length > maxCharacters)
        {
            if (File.Exists(SessionFile)) File.Delete(SessionFile);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(SessionFile)!);
        var tempFile = SessionFile + ".tmp";
        try
        {
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, SessionFile, true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static string ComputeKeyHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string GetFinalAssistantText(AgentResponse response)
    {
        foreach (var message in response.Messages.Reverse())
        {
            if (message.Role != ChatRole.Assistant || message.Contents.OfType<FunctionCallContent>().Any()) continue;
            var text = string.Join("", message.Contents.OfType<TextContent>().Select(x => x.Text));
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return response.Text.Trim();
    }

    private static async Task<McpClient> CreateLocalMcpClient(CancellationToken cancellationToken)
    {
        var port = CommandLineOptions.Instance.McpPort;
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(15),
            Name = "BetterGI Microsoft Agent Framework",
        });
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static HttpClient CreateExternalHttpClient(string? apiKey, AgentProviderKind provider)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (provider == AgentProviderKind.Anthropic)
            {
                client.DefaultRequestHeaders.Add("x-api-key", apiKey.Trim());
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
            else
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }
        }
        return client;
    }

    private static AgentEndpoints ResolveEndpoints(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("请先填写有效的 http/https 外部接口地址。");
        var value = uri.ToString().TrimEnd('/');
        var isDeepSeekOfficial = uri.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase);
        var provider = isDeepSeekOfficial || uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(x => x.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
            ? AgentProviderKind.Anthropic
            : AgentProviderKind.OpenAI;
        string apiRoot;
        if (isDeepSeekOfficial && !uri.AbsolutePath.Contains("anthropic", StringComparison.OrdinalIgnoreCase))
            apiRoot = $"{uri.Scheme}://{uri.Authority}/anthropic";
        else if (provider == AgentProviderKind.Anthropic && value.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            apiRoot = value[..^"/messages".Length];
        else if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            apiRoot = value[..^"/chat/completions".Length];
        else
            apiRoot = value;
        return new AgentEndpoints(provider, new Uri(apiRoot), new Uri(apiRoot + "/models"));
    }

    private string BuildSystemPrompt(int toolCount)
    {
        var userFile = Global.Absolute(@"User\Agent\system-prompt.md");
        var bundledFile = Global.Absolute(@"Assets\Config\AgentSystemPrompt.md");
        var promptFile = File.Exists(userFile) ? userFile : bundledFile;
        var prompt = File.Exists(promptFile)
            ? File.ReadAllText(promptFile)
            : "你是 BetterGI 内置 Agent。必须通过本机 MCP 工具读取真实状态，不要猜测。";
        if (prompt.Length > 100_000)
            throw new InvalidOperationException($"Agent 系统提示词过大（{prompt.Length} 字符），请控制在 100000 字符以内：{promptFile}");

        var config = configService.Get();
        var settingEntries = _settingsCatalog.Build(config);
        var settingSections = settingEntries.Select(x => x.Section).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var installedJsCount = Directory.Exists(Global.ScriptPath()) ? Directory.GetDirectories(Global.ScriptPath()).Length : 0;
        var scriptGroupFolder = Global.Absolute(@"User\ScriptGroup");
        var scriptGroupCount = Directory.Exists(scriptGroupFolder) ? Directory.GetFiles(scriptGroupFolder, "*.json").Length : 0;
        string repositoryContext;
        try
        {
            var repository = McpRepositoryIndex.LoadCurrent();
            repositoryContext = $"当前仓库索引时间={repository.RepositoryTime ?? "unknown"}，节点数={repository.Nodes.Count}，动态根分类={string.Join(',', repository.Nodes.Where(x => x.Depth == 0).Select(x => x.Path))}";
        }
        catch (Exception ex)
        {
            repositoryContext = $"当前仓库索引不可用：{ex.GetBaseException().Message}";
        }

        var taskContext = TaskContext.Instance();
        var currentProject = taskContext.CurrentScriptProject;
        var runtimeContext = $"""

            <bettergi_runtime_context>
            此段由 BetterGI 在每次请求时动态生成；如与固定提示词冲突，以工具实时结果为准。
            BetterGI 版本：{Global.Version}
            本次 Agent 可用 MCP 工具数：{toolCount}
            设置目录：{settingEntries.Count} 个叶子项，{settingSections} 个业务分区
            本地内容：已安装 JS 目录 {installedJsCount} 个，调度配置组文件 {scriptGroupCount} 个
            仓库：{repositoryContext}
            截图器已初始化：{taskContext.IsInitialized}
            独立任务正在运行：{TaskControl.TaskSemaphore.CurrentCount == 0}
            当前调度项目：{(currentProject is null ? "无" : $"组={currentProject.GroupInfo?.Name}, index={currentProject.Index}, type={currentProject.Type}, name={currentProject.Name}, folder={currentProject.FolderName}")}
            当前暂停：{RunnerContext.Instance.IsSuspend}
            当前取消请求：{CancellationContext.Instance.IsCancellationRequested}
            系统提示词来源：{promptFile}
            </bettergi_runtime_context>
            """;
        return prompt.Trim() + runtimeContext;
    }

    private static string TrimError(string value) => value.Length <= 4000 ? value : value[..4000] + "…";

    private sealed record AgentEndpoints(AgentProviderKind Provider, Uri ApiRoot, Uri Models);

    private enum AgentProviderKind
    {
        OpenAI,
        Anthropic,
    }

    private sealed record AgentSessionEnvelope(string RuntimeKeyHash, JsonElement Session);

    private sealed record SessionRestoreResult(AgentSession Session, bool Restored);

    private sealed class AgentRuntime(
        string key,
        string model,
        IReadOnlyList<AITool> tools,
        McpClient mcpClient,
        IChatClient chatClient,
        IDisposable? providerClient,
        ChatClientAgent agent,
        AgentSession session) : IAsyncDisposable
    {
        public string Key { get; } = key;
        public ChatClientAgent Agent { get; } = agent;
        public AgentSession Session { get; } = session;
        public int ToolCount => tools.Count;
        public bool HasConversation { get; set; }

        public ChatClientAgentRunOptions CreateRunOptions(string instructions) => new()
        {
            ChatOptions = new ChatOptions
            {
                ModelId = model,
                Instructions = instructions,
                ToolMode = ChatToolMode.Auto,
            },
        };

        public async ValueTask DisposeAsync()
        {
            chatClient.Dispose();
            providerClient?.Dispose();
            await mcpClient.DisposeAsync();
        }
    }
}

public sealed record AgentConversationMessage(string Role, string Content);

public sealed record AgentChatResult(string Content, string Model, int ToolCallCount, int AvailableToolCount);

public sealed record AgentStreamEvent(
    string Type,
    string? Delta,
    string? Message,
    IReadOnlyList<string>? Tools);

public sealed record AgentHttpChatRequest(string Message, bool ResetConversation = false);

public sealed record AgentServiceStatus(
    bool Configured,
    bool ApiKeyConfigured,
    string BaseUrl,
    string Model,
    int PersistedMessageCount,
    bool SessionActive,
    string SystemPromptFile,
    int CompactionTriggerMessages,
    int CompactionPreserveRecentGroups,
    int HardTruncationMessages,
    long SerializedSessionBytes);

#pragma warning restore MAAI001
