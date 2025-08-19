using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterGenshinImpact.Helpers.Win32;

/// <summary>
/// 控制台帮助类，用于在WPF应用程序中分配和管理控制台窗口
/// </summary>
public static class ConsoleHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleTitle(string lpConsoleTitle);

    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_ERROR_HANDLE = -12;
    private const int ATTACH_PARENT_PROCESS = -1;

    private static bool _consoleAllocated = false;

    /// <summary>
    /// 分配控制台窗口
    /// </summary>
    /// <param name="title">控制台窗口标题</param>
    /// <returns>是否成功分配控制台</returns>
    public static bool AllocateConsole(string title = "BetterGI Console")
    {
        if (_consoleAllocated)
        {
            return true;
        }

        // 尝试附加到父进程的控制台（如果从命令行启动）
        if (AttachConsole(ATTACH_PARENT_PROCESS))
        {
            _consoleAllocated = true;
            InitializeConsoleStreams();
            Console.WriteLine("\n=== BetterGI 控制台输出 ===");
            return true;
        }

        // 如果无法附加到父进程，则分配新的控制台
        // if (AllocConsole())
        // {
        //     _consoleAllocated = true;
        //     SetConsoleTitle(title);
        //     InitializeConsoleStreams();
        //     Console.WriteLine("=== BetterGI 控制台输出 ===");
        //     return true;
        // }

        return false;
    }

    /// <summary>
    /// 释放控制台窗口
    /// </summary>
    public static void FreeConsoleWindow()
    {
        if (_consoleAllocated)
        {
            FreeConsole();
            _consoleAllocated = false;
        }
    }

    /// <summary>
    /// 初始化控制台流
    /// </summary>
    private static void InitializeConsoleStreams()
    {
        // 重定向标准输出流
        var stdOutHandle = GetStdHandle(STD_OUTPUT_HANDLE);
        var stdOutStream = new FileStream(stdOutHandle, FileAccess.Write);
        var stdOutWriter = new StreamWriter(stdOutStream, Encoding.UTF8) { AutoFlush = true };
        Console.SetOut(stdOutWriter);

        // 重定向标准错误流
        var stdErrHandle = GetStdHandle(STD_ERROR_HANDLE);
        var stdErrStream = new FileStream(stdErrHandle, FileAccess.Write);
        var stdErrWriter = new StreamWriter(stdErrStream, Encoding.UTF8) { AutoFlush = true };
        Console.SetError(stdErrWriter);

        // 重定向标准输入流
        var stdInHandle = GetStdHandle(STD_INPUT_HANDLE);
        var stdInStream = new FileStream(stdInHandle, FileAccess.Read);
        var stdInReader = new StreamReader(stdInStream, Encoding.UTF8);
        Console.SetIn(stdInReader);
    }

    /// <summary>
    /// 检查控制台是否已分配
    /// </summary>
    /// <returns>控制台是否已分配</returns>
    public static bool IsConsoleAllocated => _consoleAllocated;

    /// <summary>
    /// 向控制台输出信息
    /// </summary>
    /// <param name="message">要输出的消息</param>
    public static void WriteLine(string message)
    {
        if (_consoleAllocated)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }

    /// <summary>
    /// 向控制台输出错误信息
    /// </summary>
    /// <param name="message">要输出的错误消息</param>
    public static void WriteError(string message)
    {
        if (_consoleAllocated)
        {
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: {message}");
        }
    }
}