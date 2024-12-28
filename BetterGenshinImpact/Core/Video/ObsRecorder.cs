using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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

    public ObsRecorder()
    {
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
                    TaskControl.Logger.LogInformation("OBS: 开始录制");
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
            TaskControl.Logger.LogInformation("OBS: 文件存储在 {Path}", path);
        }
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