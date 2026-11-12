using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 服务控制管理器（SCM）的最小封装，只覆盖诊断修复需要的四个动作。
///
/// 没有引入 System.ServiceProcess 包：那套 API 改不了启动类型，也等不到状态迁移完成，
/// 这里直接调 advapi32，行为更可控。
/// </summary>
internal static class ChildSessionServiceNativeMethods
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceEnumerateDependents = 0x0008;

    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceActive = 0x00000001;
    private const int ErrorMoreData = 234;

    private const uint ServiceNoChange = 0xFFFFFFFF;
    private const uint ServiceDemandStart = 0x00000003;

    private const int ServiceStopped = 1;
    private const int ServiceStopPending = 3;
    private const int ServiceRunning = 4;

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 查询服务状态码（SERVICE_STATUS.dwCurrentState）。查询失败返回 false。
    /// </summary>
    internal static bool TryQueryState(string serviceName, out int state)
    {
        state = 0;

        using var handle = ServiceHandle.Open(serviceName, ServiceQueryStatus);
        if (!handle.IsValid)
        {
            return false;
        }

        if (!QueryServiceStatus(handle.Value, out var status))
        {
            return false;
        }

        state = (int)status.CurrentState;
        return true;
    }

    /// <summary>
    /// 启动服务并等待它进入运行状态。
    /// </summary>
    internal static bool TryStart(string serviceName, out string error)
    {
        error = string.Empty;

        using var handle = ServiceHandle.Open(serviceName, ServiceStart | ServiceQueryStatus);
        if (!handle.IsValid)
        {
            error = $"无法打开服务 {serviceName}（错误 {Marshal.GetLastWin32Error()}）。";
            return false;
        }

        if (QueryServiceStatus(handle.Value, out var status) && status.CurrentState == ServiceRunning)
        {
            return true;
        }

        if (!StartService(handle.Value, 0, IntPtr.Zero))
        {
            var code = Marshal.GetLastWin32Error();
            // 1056 = 服务已在运行
            if (code != 1056)
            {
                error = $"启动服务 {serviceName} 失败（错误 {code}：{new Win32Exception(code).Message}）。";
                return false;
            }
        }

        if (WaitForState(handle.Value, ServiceRunning))
        {
            return true;
        }

        error = $"服务 {serviceName} 未能在 {StateTimeout.TotalSeconds:0} 秒内进入运行状态。";
        return false;
    }

    /// <summary>
    /// 停止服务并等待它停下来。服务本来就没运行时视为成功。
    /// </summary>
    internal static bool TryStop(string serviceName, out string error)
    {
        error = string.Empty;

        using var handle = ServiceHandle.Open(serviceName, ServiceStop | ServiceQueryStatus);
        if (!handle.IsValid)
        {
            error = $"无法打开服务 {serviceName}（错误 {Marshal.GetLastWin32Error()}）。";
            return false;
        }

        if (!QueryServiceStatus(handle.Value, out var status))
        {
            error = $"无法查询服务 {serviceName} 状态。";
            return false;
        }

        if (status.CurrentState == ServiceStopped)
        {
            return true;
        }

        if (!ControlService(handle.Value, ServiceControlStop, out _))
        {
            var code = Marshal.GetLastWin32Error();
            if (code != 1062)
            {
                error = $"停止服务 {serviceName} 失败（错误 {code}：{new Win32Exception(code).Message}）。";
                return false;
            }
        }

        if (WaitForState(handle.Value, ServiceStopped))
        {
            return true;
        }

        error = $"服务 {serviceName} 未能在 {StateTimeout.TotalSeconds:0} 秒内停止。";
        return false;
    }

    /// <summary>
    /// 把启动类型改为「手动」（Windows 对 TermService 的默认值）。
    /// </summary>
    internal static bool TrySetStartTypeToManual(string serviceName, out string error)
    {
        error = string.Empty;

        using var handle = ServiceHandle.Open(serviceName, ServiceChangeConfig);
        if (!handle.IsValid)
        {
            error = $"无法打开服务 {serviceName} 以修改启动类型（错误 {Marshal.GetLastWin32Error()}）。";
            return false;
        }

        if (!ChangeServiceConfig(
                handle.Value,
                ServiceNoChange,
                ServiceDemandStart,
                ServiceNoChange,
                null,
                null,
                IntPtr.Zero,
                null,
                null,
                null,
                null))
        {
            var code = Marshal.GetLastWin32Error();
            error = $"修改 {serviceName} 启动类型失败（错误 {code}：{new Win32Exception(code).Message}）。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 轮询等待服务进入目标状态。
    /// </summary>
    private static bool WaitForState(IntPtr handle, int expected)
    {
        var deadline = DateTime.UtcNow + StateTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!QueryServiceStatus(handle, out var status))
            {
                return false;
            }

            if (status.CurrentState == expected)
            {
                return true;
            }

            // 状态迁移中需要给服务时间，这里按 200ms 轮询即可。
            Thread.Sleep(200);
        }

        return false;
    }

    /// <summary>
    /// 列出当前处于运行状态、依赖指定服务的服务名。
    ///
    /// 停一个还有运行中依赖的服务时，SCM 会直接拒绝（错误 1051），所以重启 TermService
    /// 之前必须先把这些依赖停下来——`UmRdpService` 就依赖 TermService，而且平时是在跑的。
    /// 读不到依赖列表时返回空集合：调用方仍会尝试停止，失败时按 1051 给出可执行的提示。
    /// </summary>
    internal static IReadOnlyList<string> GetRunningDependents(string serviceName)
    {
        var dependents = new List<string>();

        using var handle = ServiceHandle.Open(
            serviceName,
            ServiceQueryStatus | ServiceEnumerateDependents);
        if (!handle.IsValid)
        {
            return dependents;
        }

        if (EnumDependentServicesW(handle.Value, ServiceActive, IntPtr.Zero, 0, out var needed, out _))
        {
            // 没有依赖时直接返回 true。
            return dependents;
        }

        if (Marshal.GetLastWin32Error() != ErrorMoreData || needed == 0)
        {
            return dependents;
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumDependentServicesW(
                    handle.Value,
                    ServiceActive,
                    buffer,
                    needed,
                    out _,
                    out var returned))
            {
                return dependents;
            }

            var size = Marshal.SizeOf<EnumServiceStatus>();
            for (var index = 0; index < returned; index++)
            {
                var item = Marshal.PtrToStructure<EnumServiceStatus>(buffer + (index * size));
                var name = Marshal.PtrToStringUni(item.ServiceName);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    dependents.Add(name);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return dependents;
    }

    /// <summary>
    /// 通过 SCM 更新服务配置。
    ///
    /// 直接写注册表时 SCM 内存里仍是旧配置，要重启系统才会重新读取；改完再回读注册表
    /// 当然是新值，于是「修复成功」但服务照样起不来。走这个 API 才会立刻生效。
    /// </summary>
    /// <param name="serviceName">服务名。</param>
    /// <param name="binaryPath">新的可执行映像路径；null 表示不改。</param>
    /// <param name="startName">新的运行账户；null 表示不改。</param>
    /// <param name="dependencies">完整的依赖列表；null 表示不改。</param>
    /// <param name="error">失败原因。</param>
    internal static bool TryChangeServiceConfig(
        string serviceName,
        string? binaryPath,
        string? startName,
        string[]? dependencies,
        out string error)
    {
        error = string.Empty;

        using var handle = ServiceHandle.Open(serviceName, ServiceChangeConfig);
        if (!handle.IsValid)
        {
            error = $"无法打开服务 {serviceName} 以修改配置（错误 {Marshal.GetLastWin32Error()}）。";
            return false;
        }

        // 多字符串要以两个 \0 结尾：这里补一个，封送时还会再加一个。
        var dependencyString = dependencies is { Length: > 0 }
            ? string.Join("\0", dependencies) + "\0"
            : null;

        if (!ChangeServiceConfig(
                handle.Value,
                ServiceNoChange,
                ServiceNoChange,
                ServiceNoChange,
                binaryPath,
                null,
                IntPtr.Zero,
                dependencyString,
                startName,
                null,
                null))
        {
            var code = Marshal.GetLastWin32Error();
            error = $"修改 {serviceName} 服务配置失败（错误 {code}：{new Win32Exception(code).Message}）。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 打开服务句柄并在 Dispose 时关闭。
    /// </summary>
    private sealed class ServiceHandle : IDisposable
    {
        private ServiceHandle(IntPtr value)
        {
            Value = value;
        }

        internal IntPtr Value { get; }

        internal bool IsValid => Value != IntPtr.Zero;

        /// <summary>
        /// 打开指定服务，失败时返回无效句柄。
        /// </summary>
        internal static ServiceHandle Open(string serviceName, uint access)
        {
            var manager = OpenSCManagerW(null, null, ScManagerConnect);
            if (manager == IntPtr.Zero)
            {
                return new ServiceHandle(IntPtr.Zero);
            }

            var service = IntPtr.Zero;
            var error = 0;

            try
            {
                service = OpenServiceW(manager, serviceName, access);
                error = Marshal.GetLastWin32Error();
            }
            finally
            {
                _ = CloseServiceHandle(manager);

                // CloseServiceHandle 成功会把 GetLastError 清成 0，调用方随后读到的
                // 就是「没有错误」。这里把 OpenServiceW 的错误码放回去再返回。
                Marshal.SetLastPInvokeError(error);
            }

            return new ServiceHandle(service);
        }

        public void Dispose()
        {
            if (Value != IntPtr.Zero)
            {
                _ = CloseServiceHandle(Value);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        internal uint ServiceType;
        internal uint CurrentState;
        internal uint ControlsAccepted;
        internal uint Win32ExitCode;
        internal uint ServiceSpecificExitCode;
        internal uint CheckPoint;
        internal uint WaitHint;
    }

    /// <summary>EnumDependentServices 返回的数组元素。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatus
    {
        internal IntPtr ServiceName;
        internal IntPtr DisplayName;
        internal ServiceStatus Status;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr service, int argumentCount, IntPtr arguments);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDependentServicesW(
        IntPtr service,
        uint serviceState,
        IntPtr services,
        uint bufferSize,
        out uint bytesNeeded,
        out uint servicesReturned);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(
        IntPtr service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);
}
