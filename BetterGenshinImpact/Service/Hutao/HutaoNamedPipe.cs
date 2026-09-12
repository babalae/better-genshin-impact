using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using BetterGenshinImpact.Service.Model.Hutao;

namespace BetterGenshinImpact.Service.Hutao;

/// <summary>
/// BetterGI 侧的命名管道<b>客户端</b>,连接胡桃的服务端 <c>Snap.Hutao.PrivateNamedPipe</c>,用于 B→S 请求(拉养成项目、转发日志)。
/// 方向与 <see cref="BGINamedPipe"/> 相反:后者是 BetterGI 作为服务端接收胡桃请求。
/// </summary>
internal sealed class HutaoNamedPipe
{
    private const string HutaoPipeName = "Snap.Hutao.Remastered.PrivateNamedPipe";

    private readonly object gate = new();

    /// <summary>
    /// 尽力而为转发日志：管道忙或连接失败时直接丢弃，绝不阻塞日志线程。
    /// 高并发下 Serilog 会在多线程同时调用 Emit，这里必须用 TryEnter 快速失败，
    /// 否则共享管道会被并发读写打乱导致卡死。
    /// </summary>
    public bool TryRedirectLog(string log)
    {
        if (!Monitor.TryEnter(gate))
        {
            return false;
        }

        try
        {
            using NamedPipeClientStream stream = CreateStream();
            if (!stream.TryConnectOnce())
            {
                return false;
            }

            try
            {
                PipeRequest<string> logRequest = new() { Kind = PipeRequestKind.Log, Data = log };
                stream.WritePacketWithJsonContent(HutaoPipeProtocol.Version, PipePacketType.Request, PipePacketCommand.BetterGenshinImpactToSnapHutaoRequest, logRequest);
                stream.ReadPacket(out _, out PipeResponse<JsonElement>? _);
                return true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException or JsonException)
            {
                return false;
            }
            finally
            {
                TerminateSession(stream);
            }
        }
        finally
        {
            Monitor.Exit(gate);
        }
    }

    public AutomationCultivationProject? TryQueryCurrentCultivationProject()
    {
        lock (gate)
        {
            using NamedPipeClientStream stream = CreateStream();
            if (!stream.TryConnectOnce())
            {
                return null;
            }

            try
            {
                PipeRequest<object?> request = new() { Kind = PipeRequestKind.QueryCurrentCultivationProject };
                stream.WritePacketWithJsonContent(HutaoPipeProtocol.Version, PipePacketType.Request, PipePacketCommand.BetterGenshinImpactToSnapHutaoRequest, request);
                stream.ReadPacket(out _, out PipeResponse<AutomationCultivationProject>? response);
                return response is { Kind: PipeResponseKind.Object } ? response.Data : null;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException or JsonException)
            {
                return null;
            }
            finally
            {
                TerminateSession(stream);
            }
        }
    }

    /// <summary>
    /// 发送一条 B→S 请求并等待胡桃回执。任务管理类请求在胡桃侧统一返回 <see cref="PipeResponseKind.None"/>,
    /// 这里只关心"是否成功走完一次收发",不关心回执内容。
    /// </summary>
    private bool SendRequest<TData>(PipeRequestKind kind, TData data)
    {
        lock (gate)
        {
            using NamedPipeClientStream stream = CreateStream();
            if (!stream.TryConnectOnce())
            {
                return false;
            }

            try
            {
                PipeRequest<TData> request = new() { Kind = kind, Data = data };
                stream.WritePacketWithJsonContent(HutaoPipeProtocol.Version, PipePacketType.Request, PipePacketCommand.BetterGenshinImpactToSnapHutaoRequest, request);
                stream.ReadPacket(out _, out PipeResponse<JsonElement>? _);
                return true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException or JsonException)
            {
                return false;
            }
            finally
            {
                TerminateSession(stream);
            }
        }
    }

    public bool CreateOneShotTask(AutomationTaskDefinition definition)
    {
        return SendRequest(PipeRequestKind.CreateOneShotTask, definition);
    }

    public bool CreateSteppedTask(SteppedAutomationTaskDefinition definition)
    {
        return SendRequest(PipeRequestKind.CreateSteppedTask, definition);
    }

    public bool RemoveTask(string id)
    {
        return SendRequest(PipeRequestKind.RemoveTask, id);
    }

    public bool UpdateTaskDefinition(string id, string name, string description)
    {
        return SendRequest(PipeRequestKind.UpdateTaskDefinition, new AutomationTaskDefinition { Id = id, Name = name, Description = description });
    }

    public bool UpdateTaskStepDefinition(string id, int index, string description)
    {
        return SendRequest(PipeRequestKind.UpdateTaskStepDefinition, new UpdateAutomationTaskStepDefinition { Id = id, Index = index, Description = description });
    }

    public bool UpdateTaskStepIndex(string id, int index)
    {
        return SendRequest(PipeRequestKind.UpdateTaskStepIndex, new UpdateAutomationTaskStepIndex { Id = id, Index = index });
    }

    public bool AddTaskStepDefinition(string id, string description)
    {
        return SendRequest(PipeRequestKind.AddTaskStepDefinition, new AddAutomationTaskStepDefinition { Id = id, Description = description });
    }

    public bool BeginSwitchToNextGameAccount()
    {
        // 胡桃侧该 handler 目前是 TODO 占位(直接返回 None),Data 传空串即可。
        return SendRequest(PipeRequestKind.BeginSwitchToNextGameAccount, string.Empty);
    }

    public bool IsHutaoRunning()
    {
        // 探测方法会在 UI 线程被周期性调用,任何异常都必须吞掉并视为"不在线",否则会崩溃应用。
        try
        {
            lock (gate)
            {
                using NamedPipeClientStream stream = CreateStream();
                if (!stream.TryConnectOnce())
                {
                    return false;
                }

                TerminateSession(stream);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static NamedPipeClientStream CreateStream()
    {
        // 注意:.NET 的 NamedPipeClientStream/PipeStream 不支持 ReadTimeout/WriteTimeout
        // (CanTimeout 为 false,设置会抛 InvalidOperationException)。连接超时只能靠 Connect(timeout)。
        return new(".", HutaoPipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);
    }

    private static void TerminateSession(NamedPipeClientStream stream)
    {
        try
        {
            stream.WritePacket(HutaoPipeProtocol.Version, PipePacketType.SessionTermination, PipePacketCommand.None);
            stream.Flush();
        }
        catch
        {
            // 忽略：会话可能已经因异常而失效。
        }
    }
}
