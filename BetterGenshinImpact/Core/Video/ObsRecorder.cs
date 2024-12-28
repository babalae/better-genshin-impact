using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;

namespace BetterGenshinImpact.Core.Video;

public class ObsRecorder : IVideoRecorder
{
    private static readonly string ObsPath = Global.Absolute(@"video\bin\OBS-Studio-31.0.0-Windows\bin\64bit\obs64.exe");
    private Process? _obs64Process;

    private OBSWebsocket obs;
    private bool isConnected = false;

    private DateTime _lastRecordTime = DateTime.MinValue;

    private readonly string _fileName;

    public ObsRecorder(string fileName)
    {
        _fileName = fileName;
        // 判断 OBS 是否已经启动
        if (Process.GetProcessesByName("obs64").Length == 0)
        {
            // 启动 OBS 并等待启动完成
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = ObsPath,
                Arguments = "--disable-shutdown-check --disable-missing-files-check --disable-updater",
                WorkingDirectory = Path.GetDirectoryName(ObsPath)
            };

            _obs64Process = Process.Start(startInfo);
            if (_obs64Process != null)
            {
                _obs64Process.WaitForInputIdle(); // Wait for the process to be ready for input
                Debug.WriteLine("OBS has started and is ready for input.");
                TaskControl.Logger.LogInformation("OBS: 启动完成");
            }
            else
            {
                TaskControl.Logger.LogError("OBS: 启动失败");
                throw new Exception("OBS启动失败");
            }
        }

        obs = new OBSWebsocket();

        // 注册连接事件处理
        obs.Connected += OnConnected;
        obs.Disconnected += OnDisconnected;

        Connect();
    }

    // 连接到 OBS
    public void Connect(string url = "ws://localhost:44557", string password = "huiyadanli@789")
    {
        try
        {
            obs.ConnectAsync(url, password);
        }
        catch (AuthFailureException)
        {
            TaskControl.Logger.LogError("OBS: 验证失败 - 密码错误");
            throw;
        }
        catch (ErrorResponseException ex)
        {
            TaskControl.Logger.LogError($"连接失败: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError($"连接失败: {ex.Message}");
            throw;
        }
    }

    // 开始录制
    public bool Start()
    {
        for (int i = 0; i < 10; i++)
        {
            if (obs.IsConnected)
            {
                try
                {
                    obs.StartRecord();
                    _lastRecordTime = DateTime.UtcNow;
                    TaskControl.Logger.LogInformation("OBS: 开始录制，时间: {Time}", _lastRecordTime.ToString("yyyy-MM-dd HH:mm:ss:ffff"));
                    return true;
                }
                catch (ErrorResponseException ex)
                {
                    if (ex.ErrorCode == 207)
                    {
                        TaskControl.Logger.LogInformation("207错误，等待连接 OBS 就绪...重试次数: {Count}", i + 1);
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        TaskControl.Logger.LogError($"OBS: 开始录制失败: {ex.Message}");
                        throw;
                    }
                }
            }
            else
            {
                Thread.Sleep(1000);
                TaskControl.Logger.LogInformation("等待连接 OBS 连接...重试次数: {Count}", i + 1);
            }
        }


        TaskControl.Logger.LogError("OBS: 启动录制失败，未连接到 OBS");
        return false;
    }

    // 停止录制
    public void Stop()
    {
        if (obs.IsConnected)
        {
            var path = obs.StopRecord();
            TaskControl.Logger.LogInformation("OBS: 停止录制录制");
            var name = Path.GetFileName(path);
            TaskControl.Logger.LogInformation("OBS: 文件存储在 {Path}", name);

            MoveFile(name);

        }
    }

    private void MoveFile(string name)
    {
        Task.Run(() =>
        {
            try
            {
                var videoPath = Global.Absolute($@"video\{name}");
                var folderPath = Global.Absolute($@"User\KeyMouseScript\{_fileName}\");
                if (File.Exists(videoPath))
                {
                    int i = 0;
                    for (i = 0; i < 10; i++)
                    {
                        if (IsFileLocked(videoPath))
                        {
                            TaskControl.Logger.LogDebug("OBS: 等待文件保存完成...重试次数: {Count}", i + 1);
                            Thread.Sleep(1000);
                        }
                        else
                        {
                            var targetPath = Path.Combine(folderPath, Path.GetFileName(videoPath));
                            File.Move(videoPath, targetPath);
                            TaskControl.Logger.LogInformation("OBS: 录制结果文件已移动到 {Path}", targetPath);
                            break;
                        }
                    }
                    
                    if (i == 10)
                    {
                        TaskControl.Logger.LogError("未能移动录制结果文件，文件可能被占用，请手动移动，文件路径: {Path}", videoPath);
                    }

                }
                else
                {
                    TaskControl.Logger.LogError("OBS: 未找到录制结果文件");
                }

                File.WriteAllText(Path.Combine(folderPath, "videoStartTime.txt"), (_lastRecordTime - new DateTime(1970, 1, 1)).TotalNanoseconds.ToString("F0"));
            }
            catch (Exception e)
            {
                TaskControl.Logger.LogError("移动录制结果文件时出现错误: {Error}", e.Message);
            }
        });

    }
    
    static bool IsFileLocked(string filePath)
    {
        FileStream stream = null;

        try
        {
            // 尝试以读取方式打开文件
            stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (IOException)
        {
            // 捕获IOException异常表示文件被占用
            return true;
        }
        finally
        {
            // 关闭文件流
            stream?.Close();
        }

        return false;
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        isConnected = true;
        TaskControl.Logger.LogInformation("OBS: 成功连接");
    }

    private void OnDisconnected(object? sender, ObsDisconnectionInfo e)
    {
        if (isConnected)
        {
            TaskControl.Logger.LogWarning("OBS: 断开连接, 原因: {Reason}", e.DisconnectReason);
        }
        else
        {
            TaskControl.Logger.LogError("OBS: 断开连接, 原因: {Reason}", e.DisconnectReason);
        }

        isConnected = false;
    }

    public void Dispose()
    {
        if (obs.IsConnected)
        {
            obs.Disconnect();
        }
    }
}