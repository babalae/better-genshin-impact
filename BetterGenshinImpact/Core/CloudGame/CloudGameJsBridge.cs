using BetterGenshinImpact.Core.Simulator.Cloud;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.CloudGame;

/// <summary>
/// C# 与云原神页面输入脚本之间的稳定桥接层。
/// C# 只依赖 <c>window.__bgiCloudInput</c> 协议，不直接耦合附件脚本的内部实现。
/// </summary>
/// <param name="hostWindow">用于执行页面脚本的当前云会话 WebView2 宿主。</param>
public sealed class CloudGameJsBridge(CloudGameHostWindow hostWindow) : ICloudJsBridge
{
    /// <summary>
    /// 注入页面的稳定包装脚本。
    /// 页面内使用 Promise 链再次保证批次间严格顺序，避免异步 RTC 输入交叉执行。
    /// </summary>
    internal const string DispatcherScript =
        """
        (() => {
          // 同一文档只安装一次包装层，避免重复注入后破坏已有 Promise 队列。
          if (window.__bgiCloudInput) return;
          // pending 将所有 dispatch 串成严格有序的 Promise 链。
          let pending = Promise.resolve();
          async function run(commands) {
            // 附件脚本是底层实现，稳定包装层只在此处读取它。
            const api = window.__ysInputInject;
            if (!api) throw new Error("YSInputInject is not installed");
            // 保留每条底层命令的返回值，便于 ExecuteScriptAsync 完整等待异步调用。
            const results = [];
            for (const command of commands || []) {
              switch (command.type) {
                case "move":
                  results.push(await api.mouseMove(command.dx || 0, command.dy || 0, command.x, command.y));
                  break;
                case "mouseDown":
                  results.push(await api.mouseDown(command.button, command.x, command.y));
                  break;
                case "mouseUp":
                  results.push(await api.mouseUp(command.button, command.x, command.y));
                  break;
                case "click":
                  results.push(await api.click(command.button, command.holdMs || 0, command.x, command.y));
                  break;
                case "doubleClick":
                  results.push(await api.doubleClick(command.button, 70, command.holdMs || 0, command.x, command.y));
                  break;
                case "scroll":
                  results.push(await api.scroll(command.delta || 0));
                  break;
                case "keyDown":
                  results.push(await api.keyDown(command.code));
                  break;
                case "keyUp":
                  results.push(await api.keyUp(command.code));
                  break;
                case "tapKey":
                  results.push(await api.tapKey(command.code, command.holdMs || 0));
                  break;
                case "text":
                  results.push(await api.sendIme(command.text || ""));
                  break;
                default:
                  throw new Error(`unsupported command: ${command.type}`);
              }
            }
            return results;
          }
          window.__bgiCloudInput = {
            dispatch(commands) {
              // 新批次必须等待上一批次完成，RTC 异步调用不会交叉。
              pending = pending.then(() => run(commands));
              return pending;
            },
            status() {
              return window.__ysInputInject?.status();
            },
            releaseAll() {
              // 即使上一批失败，释放输入仍必须继续执行。
              pending = pending.catch(() => {}).then(() => window.__ysInputInject?.releaseAll());
              return pending;
            },
            reset() {
              // reset 与 releaseAll 一样不能被旧的失败 Promise 阻断。
              pending = pending.catch(() => {}).then(() => window.__ysInputInject?.resetInput());
              return pending;
            }
          };
        })();
        """;

    /// <summary>
    /// 检查输入脚本、webpack Runtime、ClientCore、RTC DataChannel 和游戏数据通道状态。
    /// </summary>
    public async Task<CloudGameHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // status() 内部会访问 webpack Runtime 和模块 53638，因此一次调用即可覆盖全部私有 API 检查。
        const string script =
            """
            (() => {
              try {
                if (!window.__ysInputInject || !window.__bgiCloudInput) {
                  return { installed: false, error: "输入脚本尚未注入" };
                }
                // status() 会解析 webpack Runtime、ClientCore 和当前 RTC 连接。
                const status = window.__bgiCloudInput.status();
                return {
                  installed: true,
                  version: status?.version,
                  rtcDataChannelState: status?.rtcDataChannelState,
                  gameDataStarted: status?.gameDataStarted,
                  stateMachine: status?.stateMachine == null ? null : String(status.stateMachine)
                };
              } catch (error) {
                return {
                  installed: !!window.__ysInputInject,
                  error: String(error?.message || error)
                };
              }
            })()
            """;
        // WebView2 ExecuteScriptAsync 返回 JSON 编码字符串，直接反序列化为健康状态。
        var json = await hostWindow.ExecuteScriptAsync(script, cancellationToken);
        return JsonConvert.DeserializeObject<CloudGameHealth>(json) ?? new CloudGameHealth
        {
            Error = "无法解析云原神连接状态"
        };
    }

    /// <summary>
    /// 将一个有序命令批次发送给页面包装层执行。
    /// </summary>
    public async Task DispatchAsync(IReadOnlyList<CloudInputCommand> commands, CancellationToken cancellationToken = default)
    {
        if (commands.Count == 0)
        {
            return;
        }

        // 先由 Newtonsoft.Json 按 CloudInputCommand 的协议字段生成页面参数。
        var payload = JsonConvert.SerializeObject(commands);

        // 直接调用稳定包装层；底层附件脚本的 API 变化只需在 DispatcherScript 内适配。
        var script = $"window.__bgiCloudInput.dispatch({payload})";
        await hostWindow.ExecuteScriptAsync(script, cancellationToken);
    }

    /// <summary>
    /// 请求页面脚本释放全部已按下的键盘键和鼠标按钮。
    /// </summary>
    public async Task ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        await hostWindow.ExecuteScriptAsync("window.__bgiCloudInput?.releaseAll()", cancellationToken);
    }

    /// <summary>
    /// 重置页面输入脚本的默认坐标、按键及按钮状态。
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await hostWindow.ExecuteScriptAsync("window.__bgiCloudInput?.reset()", cancellationToken);
    }
}
