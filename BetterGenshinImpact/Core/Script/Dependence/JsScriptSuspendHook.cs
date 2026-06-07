using System;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using Microsoft.ClearScript;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// 为 JS 环境提供自动注册的暂停状态获取与事件 Hook。
/// </summary>
public class JsScriptSuspendHook : ISuspendable
{
    private readonly ILogger _logger;

    // 允许 JS 随时绑定回调
    public ScriptObject? OnSuspend { get; set; }
    public ScriptObject? OnResume { get; set; }

    // 获取全局的暂停是否生效状态
    public bool IsSuspended => RunnerContext.Instance.IsSuspend;

    public JsScriptSuspendHook()
    {
        _logger = App.GetLogger<JsScriptSuspendHook>();
    }

    public void Suspend()
    {
        try
        {
            OnSuspend?.InvokeAsFunction();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行 JS Suspend Hook 时发生异常");
        }
    }

    public void Resume()
    {
        try
        {
            OnResume?.InvokeAsFunction();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行 JS Resume Hook 时发生异常");
        }
    }
}
