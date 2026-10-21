using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 在独立任务运行期间临时抑制当前 Windows 用户的通知横幅，并在任务结束后恢复原设置。
/// </summary>
public sealed class WindowsNotificationSuppressionService
{
    /// <summary>
    /// Windows 当前用户通知设置的注册表路径。
    /// </summary>
    private const string NotificationSettingsRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    /// <summary>
    /// Windows 用于控制全局通知横幅的设置值名称；0 表示开启勿扰。
    /// </summary>
    private const string ToastsEnabledValueName = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";

    /// <summary>
    /// 全局通知横幅启用值。
    /// </summary>
    private const int ToastsEnabled = 1;

    /// <summary>
    /// 全局通知横幅禁用值。
    /// </summary>
    private const int ToastsDisabled = 0;

    /// <summary>
    /// 向所有顶层窗口广播消息的特殊窗口句柄。
    /// </summary>
    private static readonly nint BroadcastWindow = new(0xFFFF);

    /// <summary>
    /// 通知应用重新读取用户设置的 Windows 消息。
    /// </summary>
    private const uint WindowsSettingChangedMessage = 0x001A;

    /// <summary>
    /// 当前应用配置服务。
    /// </summary>
    private readonly IConfigService _configService;

    /// <summary>
    /// Windows 通知抑制日志。
    /// </summary>
    private readonly ILogger<WindowsNotificationSuppressionService> _logger;

    /// <summary>
    /// 创建 Windows 通知抑制服务。
    /// </summary>
    /// <param name="configService">当前应用配置服务。</param>
    /// <param name="logger">Windows 通知抑制日志。</param>
    public WindowsNotificationSuppressionService(
        IConfigService configService,
        ILogger<WindowsNotificationSuppressionService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// 按当前配置尽力开启 Windows 勿扰；成功修改设置时返回用于恢复的作用域。
    /// </summary>
    /// <returns>任务结束时需要释放的恢复作用域；无需恢复或开启失败时为 <see langword="null"/>。</returns>
    public IDisposable? TrySuppressForSoloTask()
    {
        if (!_configService.Get().OtherConfig.SuppressWindowsNotificationsDuringSoloTask)
        {
            return null;
        }

        try
        {
            using var settingsKey = Registry.CurrentUser.CreateSubKey(
                NotificationSettingsRegistryPath,
                writable: true);
            if (settingsKey is null)
            {
                _logger.LogWarning("无法打开 Windows 通知设置，独立任务将继续运行");
                return null;
            }

            var hadOriginalValue = settingsKey.GetValueNames().Contains(
                ToastsEnabledValueName,
                StringComparer.OrdinalIgnoreCase);
            var originalValue = hadOriginalValue
                ? settingsKey.GetValue(ToastsEnabledValueName, ToastsEnabled)
                : null;
            var originalKind = hadOriginalValue
                ? settingsKey.GetValueKind(ToastsEnabledValueName)
                : RegistryValueKind.None;

            if (originalValue is int currentValue && currentValue == ToastsDisabled)
            {
                // 用户原本已经关闭通知，本轮不取得设置所有权，结束时也不应重新开启。
                return null;
            }

            settingsKey.SetValue(ToastsEnabledValueName, ToastsDisabled, RegistryValueKind.DWord);
            settingsKey.Flush();
            BroadcastNotificationSettingsChanged();
            _logger.LogInformation("独立任务运行期间已开启 Windows 勿扰");
            return new NotificationSuppressionScope(
                hadOriginalValue,
                originalValue,
                originalKind,
                _logger);
        }
        catch (Exception exception)
        {
            // 通知抑制属于辅助功能，权限或系统兼容问题不能阻止独立任务执行。
            _logger.LogWarning(exception, "开启 Windows 勿扰失败，独立任务将继续运行");
            return null;
        }
    }

    /// <summary>
    /// 广播通知设置变化，使通知平台和已运行应用尽快读取新值。
    /// </summary>
    private static void BroadcastNotificationSettingsChanged()
    {
        _ = SendNotifyMessage(
            BroadcastWindow,
            WindowsSettingChangedMessage,
            nint.Zero,
            NotificationSettingsRegistryPath);
    }

    /// <summary>
    /// 异步向指定窗口发送消息，避免某个无响应窗口阻塞独立任务启动或清理。
    /// </summary>
    /// <param name="windowHandle">目标窗口句柄。</param>
    /// <param name="message">Windows 消息标识。</param>
    /// <param name="wParam">消息的整数参数。</param>
    /// <param name="lParam">消息的字符串参数。</param>
    /// <returns>消息是否成功放入目标线程的消息队列。</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendNotifyMessage(
        nint windowHandle,
        uint message,
        nint wParam,
        string lParam);

    /// <summary>
    /// 只恢复由当前独立任务修改、且此后没有被用户再次更改的 Windows 通知设置。
    /// </summary>
    private sealed class NotificationSuppressionScope : IDisposable
    {
        /// <summary>
        /// 任务开始前是否存在通知设置值。
        /// </summary>
        private readonly bool _hadOriginalValue;

        /// <summary>
        /// 任务开始前的通知设置值。
        /// </summary>
        private readonly object? _originalValue;

        /// <summary>
        /// 任务开始前的通知设置值类型。
        /// </summary>
        private readonly RegistryValueKind _originalKind;

        /// <summary>
        /// Windows 通知抑制日志。
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// 防止任务清理与进程退出同时重复恢复设置。
        /// </summary>
        private int _disposeState;

        /// <summary>
        /// 创建 Windows 通知设置恢复作用域。
        /// </summary>
        /// <param name="hadOriginalValue">任务开始前是否存在通知设置值。</param>
        /// <param name="originalValue">任务开始前的通知设置值。</param>
        /// <param name="originalKind">任务开始前的通知设置值类型。</param>
        /// <param name="logger">Windows 通知抑制日志。</param>
        public NotificationSuppressionScope(
            bool hadOriginalValue,
            object? originalValue,
            RegistryValueKind originalKind,
            ILogger logger)
        {
            _hadOriginalValue = hadOriginalValue;
            _originalValue = originalValue;
            _originalKind = originalKind;
            _logger = logger;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        /// <summary>
        /// 在通知设置仍保持本轮写入值时恢复任务开始前的状态。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            try
            {
                using var settingsKey = Registry.CurrentUser.OpenSubKey(
                    NotificationSettingsRegistryPath,
                    writable: true);
                if (settingsKey is null)
                {
                    _logger.LogWarning("无法重新打开 Windows 通知设置，未能恢复独立任务开始前的状态");
                    return;
                }

                var currentValue = settingsKey.GetValue(ToastsEnabledValueName, ToastsEnabled);
                if (currentValue is not int currentIntValue || currentIntValue != ToastsDisabled)
                {
                    // 任务期间用户或其他程序已经修改通知设置，保留其新选择。
                    _logger.LogInformation("Windows 通知设置已在任务期间发生变化，跳过自动恢复");
                    return;
                }

                if (_hadOriginalValue && _originalValue is not null)
                {
                    settingsKey.SetValue(ToastsEnabledValueName, _originalValue, _originalKind);
                }
                else
                {
                    settingsKey.DeleteValue(ToastsEnabledValueName, throwOnMissingValue: false);
                }

                settingsKey.Flush();
                BroadcastNotificationSettingsChanged();
                _logger.LogInformation("独立任务结束，已恢复 Windows 通知设置");
            }
            catch (Exception exception)
            {
                // 恢复失败不应破坏 TaskRunner 的按键释放、遮罩清理和任务锁释放。
                _logger.LogWarning(exception, "恢复 Windows 通知设置失败");
            }
        }

        /// <summary>
        /// 应用正常退出或调用 <see cref="Environment.Exit(int)"/> 时尽力恢复通知设置。
        /// </summary>
        /// <param name="sender">进程退出事件来源。</param>
        /// <param name="eventArgs">进程退出事件参数。</param>
        private void OnProcessExit(object? sender, EventArgs eventArgs)
        {
            Dispose();
        }
    }
}
