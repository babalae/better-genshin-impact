using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using OpenCvSharp;

namespace Fischless.GameCapture.Wayland;

public class WaylandGameCapture : IGameCapture
{
    public bool IsCapturing { get; private set; }

    // 文件路径必须与 Python 脚本中的定义保持一致
    private static readonly string FramePath  = "/dev/shm/bettergi/frame.bin";
    private static readonly string FrameTmpPath  = "/dev/shm/bettergi/frame.bin.tmp";
    private static readonly string PidPath    = "/dev/shm/bettergi/daemon.pid";
    private static readonly string ScriptPath = "/dev/shm/bettergi/screencast_daemon.py";

    private int _daemonPid = 0;

    /// <summary>
    /// 启动截图守护进程，并等待共享内存帧文件就绪
    /// 由于 Wayland 的限制，窗口句柄 hWnd 实际没有被使用
    /// </summary>
    public void Start(nint hWnd, Dictionary<string, object>? settings = null)
    {
        IsCapturing = false;

        KillDaemon();

        var name = typeof(WaylandGameCapture).Assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("screencast_daemon.py"));
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("[Wayland] 未找到嵌入的 screencast_daemon.py");
            return;
        }

        using var stream = typeof(WaylandGameCapture).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            Console.Error.WriteLine("[Wayland] 无法打开嵌入的脚本");
            return;
        }

        try
        {
            Directory.CreateDirectory("/dev/shm/bettergi");
            using var fs = new FileStream(ScriptPath, FileMode.Create);
            stream.CopyTo(fs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Wayland] 把脚本复制到 /dev/shm/bettergi 时出错: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName               = "/usr/bin/python3",
                Arguments              = $"\"{ScriptPath}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Wayland] 启动守护进程出错: {ex.Message}");
            KillDaemon();
            return;
        }

        // 等待 Pid 文件出现，最长等待 10 秒（200 * 50ms）
        for (int i = 0; i < 200; i++)
        {
            if (File.Exists(PidPath))
            {
                try
                {
                    string pidStr = File.ReadAllText(PidPath).Trim();
                    _daemonPid = int.Parse(pidStr);
                    if (_daemonPid != 0) break;
                }
                catch { }
            }
            Thread.Sleep(50);
        }

        // 等待共享内存帧文件出现，最长等待 30 秒（600 * 50ms），或者守护进程退出
        for (int i = 0; i < 600; i++)
        {
            if (!IsDaemonAlive())
            {
                return;
            }
            if (File.Exists(FramePath))
            {
                IsCapturing = true;
                return;
            }
            Thread.Sleep(50);
        }

        KillDaemon();
    }

    /// <summary>
    /// 从共享内存帧文件中读取最新的一帧并转换为 OpenCV Mat。
    /// 帧格式：8 字节大端头（宽、高各 4 字节）+ BGR 像素数据。
    /// </summary>
    public Mat? Capture()
    {
        if (!IsCapturing) return null;
        if (!IsDaemonAlive())
        {
            IsCapturing = false;
            return null;
        }

        try
        {
            using var mmf = MemoryMappedFile.CreateFromFile(
                FramePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            long cap = accessor.Capacity;
            if (cap < 8) return null;

            byte[] header = new byte[8];
            accessor.ReadArray(0, header, 0, 8);
            int w = BinaryPrimitives.ReadInt32BigEndian(header);
            int h = BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(header, 4, 4));

            if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;

            int size = w * h * 3;
            if (cap < 8 + size) return null;

            byte[] pixels = new byte[size];
            accessor.ReadArray(8, pixels, 0, size);
            return Mat.FromPixelData(h, w, MatType.CV_8UC3, pixels);
        }
        catch (FileNotFoundException) { return null; }
        catch (IOException) { return null; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Wayland] capture: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public void Stop()
    {
        IsCapturing = false;
        KillDaemon();
    }

    public void Dispose()
    {
        IsCapturing = false;
        KillDaemon();
    }

    private bool IsDaemonAlive()
    {
        if (_daemonPid == 0) return false;
        string procPath = $"/proc/{_daemonPid}";
        return Directory.Exists(procPath);
    }

    private void KillDaemon()
    {
        IsCapturing = false;

        if (_daemonPid != 0)
        {
            string pidStr = _daemonPid.ToString();

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "/bin/kill",
                    Arguments       = pidStr,
                    UseShellExecute = false,
                    CreateNoWindow  = true
                });
            }
            catch { }

            Thread.Sleep(50);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "/bin/kill",
                    Arguments       = $"-9 {pidStr}",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                });
            }
            catch { }
        }

        try { File.Delete(PidPath); } catch { }
        try { File.Delete(FramePath); } catch { }
        try { File.Delete(FrameTmpPath); } catch { }
        try { File.Delete(ScriptPath); } catch { }

        _daemonPid = 0;
    }
}
