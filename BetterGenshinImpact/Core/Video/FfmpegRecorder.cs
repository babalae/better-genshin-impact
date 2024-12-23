using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Serilog.Core;


namespace BetterGenshinImpact.Core.Video;

public class FfmpegRecorder
{
    // ffmpeg进程
    private readonly Process _process;

    // ffmpeg.exe实体文件路径
    private static readonly string FfmpegPath = Global.Absolute(@"video\bin\ffmpeg.exe");

    private readonly string _filePath;
    private string _startTime = string.Empty;

    private readonly string _fileName;

    public FfmpegRecorder(string fileName)
    {
        _fileName = fileName;
        if (!File.Exists(FfmpegPath))
        {
            throw new Exception("ffmpeg.exe不存在");
        }

        var folderPath = Global.Absolute($@"User\KeyMouseScript\{fileName}\");
        Directory.CreateDirectory(folderPath);
        _filePath = Path.Combine(folderPath, "%Y_%m_%d_%H_%M_%S.mp4");
        var processInfo = new ProcessStartInfo
        {
            FileName = FfmpegPath,
            Arguments = $" -f gdigrab -hwaccel cuvid -show_region 1 -framerate 60 -use_wallclock_as_timestamps 1 -i title=原神 -pix_fmt yuv420p  -c:v libx264 -preset ultrafast -f segment -segment_time 1800 -reset_timestamps 1 -strftime 1  \"{_filePath}\"",
            StandardInputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // StandardOutputEncoding =  Encoding.UTF8,
        };
        _process = new Process { StartInfo = processInfo };
        // _process.OutputDataReceived += (sender, args) => { Debug.WriteLine(args.Data); };
        _process.ErrorDataReceived += (sender, args) =>
        {
            TaskControl.Logger.LogDebug(args.Data);
            if (string.IsNullOrEmpty(_startTime))
            {
                if (args.Data != null && args.Data.Contains("start"))
                {
                    string pattern = @"start:\s*(\d+\.\d+)";

                    Match match = Regex.Match(args.Data, pattern);
                    if (match.Success)
                    {
                        _startTime = match.Groups[1].Value.Replace(".", "");
                        TaskControl.Logger.LogInformation("ffmpeg录制: 视频起始时间戳 {Text}", _startTime);
                        File.WriteAllText(Path.Combine(folderPath, $"{_startTime}.txt"), _startTime);
                    }
                }
            }
        };
    }


    /// <summary>
    /// 功能: 开始录制
    /// </summary>
    public bool Start()
    {
        _process.Start();
        // _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        TaskControl.Logger.LogInformation("ffmpeg录制: {Text}", "已启动");
        return true;
    }

    /// <summary>
    /// 功能: 停止录制
    /// </summary>
    public void Stop()
    {
        // Kernel32.AttachConsole((uint)_process.Id);
        // Kernel32.SetConsoleCtrlHandler(null, true);
        // Kernel32.GenerateConsoleCtrlEvent(0, 0);
        // Kernel32.FreeConsole();

        // AttachConsole(_process.Id);
        // SetConsoleCtrlHandler(IntPtr.Zero, true);
        // GenerateConsoleCtrlEvent(0, 0);
        // FreeConsole();
        try
        {
            TaskControl.Logger.LogInformation("ffmpeg录制: {Text}", "正在停止录制，请稍后...");
            _process.StandardInput.WriteLine("q");

            if (!_process.WaitForExit(5000))
            {
                _process.Kill();
            }

            _process.Close();
            _process.Dispose();


            // Thread.Sleep(3000);
            // if (File.Exists(_filePath))
            // {
            //     // 重命名文件
            //     var newFilePath = Global.Absolute($@"User\KeyMouseScript\{_fileName}_{_startTime}.mp4");
            //     File.Move(_filePath, newFilePath);
            //     TaskControl.Logger.LogInformation("ffmpeg录制: {Text}", $"录制完成");
            // }
            // else
            // {
            //     TaskControl.Logger.LogError("ffmpeg录制: {Text}", "未找到结果文件，录制失败");
            // }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "ffmpeg录制: {Text}", "停止录制失败");
            TaskControl.Logger.LogError("ffmpeg录制: 停止时异常：{Text}", e.Message);
        }
    }
}