using BetterGenshinImpact.GameTask;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace BetterGenshinImpact.Core.Recorder;

public class VideoRecorder : IDisposable
{
    private readonly ILogger<VideoRecorder> _logger = App.GetLogger<VideoRecorder>();
    
    private IGameCapture? _gameCapture;
    private VideoWriter? _videoWriter;
    private Timer? _captureTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    
    private int _frameRate = 30;
    private int _frameWidth;
    private int _frameHeight;
    private bool _isRecording = false;
    private readonly object _lockObject = new();
    
    // 帧缓冲区，避免丢帧
    private readonly Queue<Mat> _frameBuffer = new();
    private Task? _encodingTask;

    public bool IsRecording => _isRecording;

    public void StartRecording(string outputPath, int frameRate = 30)
    {
        if (_isRecording)
        {
            _logger.LogWarning("视频录制已在进行中");
            return;
        }

        try
        {
            _frameRate = frameRate;
            _cancellationTokenSource = new CancellationTokenSource();
            
            // 获取游戏截图器
            _gameCapture = TaskTriggerDispatcher.GlobalGameCapture;
            if (_gameCapture == null)
            {
                throw new InvalidOperationException("游戏截图器未初始化");
            }

            // 获取游戏窗口尺寸
            var systemInfo = TaskContext.Instance().SystemInfo;
            _frameWidth = systemInfo.CaptureAreaRect.Width;
            _frameHeight = systemInfo.CaptureAreaRect.Height;

            // 配置视频编码器参数
            var fourcc = VideoWriter.FourCC('H', '2', '6', '4'); // H.264编码
            _videoWriter = new VideoWriter(outputPath, fourcc, _frameRate, new OpenCvSharp.Size(_frameWidth, _frameHeight), true);

            if (!_videoWriter.IsOpened())
            {
                throw new InvalidOperationException("无法创建视频文件，请检查编码器是否可用");
            }

            _isRecording = true;
            
            // 启动编码任务
            _encodingTask = Task.Run(() => EncodingLoop(_cancellationTokenSource.Token));
            
            // 启动定时器进行截图
            _captureTimer = new Timer(1000.0 / _frameRate);
            _captureTimer.Elapsed += OnCaptureFrame;
            _captureTimer.AutoReset = true;
            _captureTimer.Start();

            _logger.LogInformation("视频录制已启动: {Path}, 分辨率: {Width}x{Height}, 帧率: {Fps}", 
                outputPath, _frameWidth, _frameHeight, _frameRate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动视频录制失败");
            Cleanup();
            throw;
        }
    }

    public void StopRecording()
    {
        if (!_isRecording)
        {
            _logger.LogWarning("视频录制未在进行中");
            return;
        }

        try
        {
            _logger.LogInformation("正在停止视频录制...");
            
            // 停止定时器
            _captureTimer?.Stop();
            _captureTimer?.Dispose();
            _captureTimer = null;

            // 取消编码任务
            _cancellationTokenSource?.Cancel();
            
            // 等待编码任务完成
            _encodingTask?.Wait(TimeSpan.FromSeconds(10));
            _encodingTask?.Dispose();
            _encodingTask = null;

            _isRecording = false;
            _logger.LogInformation("视频录制已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止视频录制时发生错误");
        }
        finally
        {
            Cleanup();
        }
    }

    private void OnCaptureFrame(object? sender, ElapsedEventArgs e)
    {
        if (!_isRecording || _gameCapture == null)
            return;

        try
        {
            // 捕获游戏画面
            var frame = _gameCapture.Capture();
            if (frame != null && !frame.Empty())
            {
                lock (_lockObject)
                {
                    _frameBuffer.Enqueue(frame.Clone());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "捕获视频帧时发生错误");
        }
    }

    private void EncodingLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested || _frameBuffer.Count > 0)
            {
                Mat? frame = null;
                
                lock (_lockObject)
                {
                    if (_frameBuffer.Count > 0)
                    {
                        frame = _frameBuffer.Dequeue();
                    }
                }

                if (frame != null)
                {
                    try
                    {
                        _videoWriter?.Write(frame);
                        frame.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "编码视频帧时发生错误");
                    }
                }
                else
                {
                    // 如果没有帧需要编码，稍微等待一下
                    Thread.Sleep(10);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "视频编码循环发生错误");
        }
    }

    private void Cleanup()
    {
        try
        {
            // 清理帧缓冲区
            lock (_lockObject)
            {
                while (_frameBuffer.Count > 0)
                {
                    var frame = _frameBuffer.Dequeue();
                    frame?.Dispose();
                }
            }

            _videoWriter?.Dispose();
            _videoWriter = null;
            
            _gameCapture = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理视频录制资源时发生错误");
        }
    }



    public void Dispose()
    {
        if (_isRecording)
        {
            StopRecording();
        }
        Cleanup();
        GC.SuppressFinalize(this);
    }
}