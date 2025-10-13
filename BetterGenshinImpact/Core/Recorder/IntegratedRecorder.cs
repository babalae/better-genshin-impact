using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using Gma.System.MouseKeyHook;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Recorder;

public class IntegratedRecorder : IDisposable
{
    private readonly ILogger<IntegratedRecorder> _logger = App.GetLogger<IntegratedRecorder>();
    
    // 录制状态
    private bool _isRecording;
    private CancellationTokenSource? _cancellationTokenSource;
    
    // 键鼠录制
    private KeyMouseRecorder? _keyMouseRecorder;
    private IKeyboardMouseEvents? _globalHook;
    private DirectInputMonitor? _directInputMonitor;
    
    // 视频录制
    private VideoRecorder? _videoRecorder;
    
    // 坐标录制
    private CoordinateRecorder? _coordinateRecorder;
    private Timer? _coordinateTimer;
    
    // 录制信息
    private DateTime _startTime;
    private string _recordId = string.Empty;
    private string _outputDirectory;

    // 坐标获取统计
    private int _coordinateAttempts = 0;
    private int _coordinateSuccesses = 0;
    private double _lastSuccessX = 0;
    private double _lastSuccessY = 0;
    
    // 同步锁
    private readonly object _lockObject = new();
    
    // 配置参数
    private int _frameRate;
    private int _coordinateInterval;

    public IntegratedRecorder(string outputDirectory, int frameRate, int coordinateInterval)
    {
        _outputDirectory = outputDirectory;
        _frameRate = frameRate;
        _coordinateInterval = coordinateInterval;
    }

    public IntegratedRecorder() : this(Path.Combine(Global.Absolute("User\\IntegratedRecords"), $"IntegratedRecord_{DateTime.Now:yyyyMMdd_HHmmss}"), 30, 100)
    {
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            lock (_lockObject)
            {
                _isRecording = value;
            }
        }
    }

    public async Task StartRecordingAsync(string? customName = null)
    {
        if (IsRecording)
        {
            _logger.LogWarning("已经在录制状态，无法重新开始");
            return;
        }

        if (!TaskContext.Instance().IsInitialized)
        {
            throw new InvalidOperationException("请先在启动页启动截图器再使用本功能");
        }

        try
        {
            // 初始化录制
            _recordId = string.IsNullOrEmpty(customName) 
                ? $"IntegratedRecord_{DateTime.Now:yyyyMMdd_HHmmss}" 
                : $"{customName}_{DateTime.Now:yyyyMMdd_HHmmss}";
            
            _outputDirectory = Path.Combine(Global.Absolute("User\\IntegratedRecords"), _recordId);
            Directory.CreateDirectory(_outputDirectory);

            _startTime = DateTime.Now;
            _cancellationTokenSource = new CancellationTokenSource();

            // 预热地图匹配，确保模板加载完成
            var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
            _logger.LogInformation("使用地图匹配方法: {Method}", matchingMethod);
            Navigation.WarmUp(matchingMethod);
            _logger.LogInformation("地图匹配模板预热完成");

            // 启动各种录制器
            await StartKeyMouseRecording();
            StartVideoRecording();
            StartCoordinateRecording();

            IsRecording = true;
            _logger.LogInformation("综合录制已启动: {RecordId}", _recordId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动综合录制失败");
            await StopRecordingAsync();
            throw;
        }
    }

    public async Task StopRecordingAsync()
    {
        if (!IsRecording)
        {
            _logger.LogWarning("未处于录制状态");
            return;
        }

        try
        {
            _cancellationTokenSource?.Cancel();

            // 停止各种录制器
            await StopKeyMouseRecording();
            StopVideoRecording();
            await StopCoordinateRecording();

            // 保存录制元数据
            SaveRecordingMetadata();

            IsRecording = false;
            _logger.LogInformation("综合录制已停止: {RecordId}", _recordId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止综合录制失败");
            throw;
        }
        finally
        {
            Cleanup();
        }
    }

    private async Task StartKeyMouseRecording()
    {
        _keyMouseRecorder = new KeyMouseRecorder();
        _directInputMonitor = new DirectInputMonitor();
        
        // 启动DirectInput监控
        _directInputMonitor.Start();
        
        // 设置全局钩子
        _globalHook = Hook.GlobalEvents();
        _globalHook.KeyDown += OnKeyDown;
        _globalHook.KeyUp += OnKeyUp;
        _globalHook.MouseDown += OnMouseDown;
        _globalHook.MouseUp += OnMouseUp;
        _globalHook.MouseMove += OnMouseMove;
        _globalHook.MouseWheel += OnMouseWheel;

        await Task.CompletedTask;
    }

    private async Task StopKeyMouseRecording()
    {
        // 移除全局钩子
        if (_globalHook != null)
        {
            _globalHook.KeyDown -= OnKeyDown;
            _globalHook.KeyUp -= OnKeyUp;
            _globalHook.MouseDown -= OnMouseDown;
            _globalHook.MouseUp -= OnMouseUp;
            _globalHook.MouseMove -= OnMouseMove;
            _globalHook.MouseWheel -= OnMouseWheel;
            _globalHook.Dispose();
            _globalHook = null;
        }

        // 停止DirectInput监控
        _directInputMonitor?.Stop();
        _directInputMonitor?.Dispose();
        _directInputMonitor = null;

        // 保存键鼠录制数据
        if (_keyMouseRecorder != null)
        {
            var keyMouseData = _keyMouseRecorder.ToJsonMacro();
            var keyMousePath = Path.Combine(_outputDirectory, "keymouse.json");
            await File.WriteAllTextAsync(keyMousePath, keyMouseData);
            _logger.LogInformation("键鼠录制数据已保存: {Path}", keyMousePath);
        }
    }

    private void StartVideoRecording()
    {
        _videoRecorder = new VideoRecorder();
        var videoPath = Path.Combine(_outputDirectory, "video.mp4");
        _videoRecorder.StartRecording(videoPath, _frameRate);
    }

    private void StopVideoRecording()
    {
        _videoRecorder?.StopRecording();
        _videoRecorder?.Dispose();
        _videoRecorder = null;
    }

    private void StartCoordinateRecording()
    {
        _coordinateRecorder = new CoordinateRecorder();
        _coordinateRecorder.StartRecording();
        
        // 重置统计
        _coordinateAttempts = 0;
        _coordinateSuccesses = 0;
        
        _logger.LogInformation("开始坐标录制 - 间隔: {Interval}ms, 地图匹配方法: {Method}", 
            _coordinateInterval, TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod);
        
        // 按配置间隔记录坐标
        _coordinateTimer = new Timer(_coordinateInterval);
        _coordinateTimer.Elapsed += (_, _) =>
        {
            try
            {
                if (!IsRecording || !TaskContext.Instance().IsInitialized) return;
                var elapsed = (DateTime.Now - _startTime).TotalMilliseconds;
                var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
                    
                // 使用BGI的地图匹配获取当前游戏内坐标
                var imageRegion = TaskControl.CaptureToRectArea();
                _coordinateAttempts++;
                
                // 记录调试信息
                _logger.LogDebug("尝试获取坐标 #{Attempt} - 录制状态: {IsRecording}, 截图区域: {Width}x{Height}, 经过时间: {Elapsed}ms", 
                    _coordinateAttempts, IsRecording, imageRegion.Width, imageRegion.Height, elapsed);
                
                // 检查是否在小地图界面（大地图或主界面）
                var isInMainUi = Bv.IsInMainUi(imageRegion);
                var isInBigMap = Bv.IsInBigMapUi(imageRegion);
                var canGetPosition = isInMainUi || isInBigMap;
                
                if (canGetPosition)
                {
                    var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
                        
                    _logger.LogDebug("尝试地图匹配 - 主界面: {IsMainUi}, 大地图: {IsBigMap}, 匹配方法: {Method}", 
                        isInMainUi, isInBigMap, matchingMethod);
                        
                    // 重试获取位置
                    var position = Navigation.GetPositionStable(imageRegion, nameof(MapTypes.Teyvat), matchingMethod);
                        
                    if (position != default)
                    {
                        _coordinateSuccesses++;
                        _logger.LogDebug("地图匹配成功 - 位置: ({X}, {Y}), 成功率: {SuccessRate:F1}%", 
                            position.X, position.Y, (_coordinateSuccesses * 100.0) / _coordinateAttempts);
                        
                        // 转换为游戏内坐标系
                        var gamePosition = MapManager.GetMap(nameof(MapTypes.Teyvat), matchingMethod)
                            .ConvertImageCoordinatesToGenshinMapCoordinates(position);
                            
                        if (gamePosition != default)
                        {
                            // 更新上次成功位置
                            _lastSuccessX = gamePosition.X;
                            _lastSuccessY = gamePosition.Y;
                            
                            _coordinateRecorder.RecordCoordinate(
                                gamePosition.X,
                                gamePosition.Y,
                                $"时间: {elapsed:F0}ms, 区域: {captureArea.Width}x{captureArea.Height}, 主界面: {isInMainUi}, 大地图: {isInBigMap}"
                            );
                            
                            _logger.LogInformation("成功记录坐标 #{Attempt}: ({X}, {Y}), 总成功率: {SuccessRate:F1}%", 
                                _coordinateSuccesses, gamePosition.X, gamePosition.Y, (_coordinateSuccesses * 100.0) / _coordinateAttempts);
                        }
                        else
                        {
                            _logger.LogWarning("坐标转换失败 - 原始位置: ({X}, {Y})", position.X, position.Y);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("地图匹配失败 #{Attempt} - 成功率: {SuccessRate:F1}%, 请确保：1.在小地图界面 2.地图匹配模板加载完成 3.角色位置在已识别区域", 
                            _coordinateAttempts, (_coordinateSuccesses * 100.0) / _coordinateAttempts);
                        _logger.LogInformation("地图匹配方法: {Method}, 上次成功位置: ({LastX}, {LastY})", 
                            matchingMethod, _lastSuccessX, _lastSuccessY);
                    }
                }
                else
                {
                    _logger.LogDebug("当前界面不支持坐标获取 #{Attempt} - 主界面: {IsMainUi}, 大地图: {IsBigMap}, 成功率: {SuccessRate:F1}%", 
                        _coordinateAttempts, isInMainUi, isInBigMap, (_coordinateSuccesses * 100.0) / _coordinateAttempts);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录坐标时发生错误");
            }
        };
        _coordinateTimer.Start();
    }

    private async Task StopCoordinateRecording()
    {
        _coordinateTimer?.Dispose();
        _coordinateTimer = null;

        // 停止坐标录制
        _coordinateRecorder?.StopRecording();

        // 输出坐标获取统计
        if (_coordinateAttempts > 0)
        {
            var successRate = (_coordinateSuccesses * 100.0) / _coordinateAttempts;
            _logger.LogInformation("坐标获取统计 - 总尝试: {Attempts}, 成功: {Successes}, 成功率: {SuccessRate:F1}%", 
                _coordinateAttempts, _coordinateSuccesses, successRate);
            
            if (_coordinateSuccesses == 0)
            {
                _logger.LogWarning("未能成功获取任何坐标，请检查：1.是否在小地图界面 2.地图匹配模板是否正确加载 3.角色是否在已识别区域");
            }
        }

        // 保存坐标录制数据
        if (_coordinateRecorder != null)
        {
            var coordinatePath = Path.Combine(_outputDirectory, "coordinates.json");
            await _coordinateRecorder.SaveToFileAsync(coordinatePath);
            _logger.LogInformation("坐标录制数据已保存: {Path}", coordinatePath);
        }
    }

    private void SaveRecordingMetadata()
    {
        var metadata = new RecordingMetadata
        {
            RecordId = _recordId,
            StartTime = _startTime,
            Duration = DateTime.Now - _startTime
        };

        var metadataPath = Path.Combine(_outputDirectory, "metadata.json");
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(metadataPath, json);
        _logger.LogInformation("录制元数据已保存: {Path}", metadataPath);
    }

    private void Cleanup()
    {
        _keyMouseRecorder = null;
        _coordinateRecorder = null;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    #region 事件处理

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_keyMouseRecorder != null && IsRecording)
        {
            var time = Kernel32.GetTickCount();
            _keyMouseRecorder.KeyDown(e, time);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_keyMouseRecorder != null && IsRecording)
        {
            var time = Kernel32.GetTickCount();
            _keyMouseRecorder.KeyUp(e, time);
        }
    }

    private void OnMouseDown(object? sender, EventArgs e)
    {
        if (_keyMouseRecorder != null && IsRecording && e is MouseEventExtArgs mouseArgs)
        {
            _keyMouseRecorder.MouseDown(mouseArgs);
        }
    }

    private void OnMouseUp(object? sender, EventArgs e)
    {
        if (_keyMouseRecorder != null && IsRecording && e is MouseEventExtArgs mouseArgs)
        {
            _keyMouseRecorder.MouseUp(mouseArgs);
        }
    }

    private void OnMouseMove(object? sender, EventArgs e)
    {
        if (_keyMouseRecorder != null && IsRecording && e is MouseEventExtArgs mouseArgs)
        {
            _keyMouseRecorder.MouseMoveTo(mouseArgs);
        }
    }

    private void OnMouseWheel(object? sender, EventArgs e)
    {
        if (_keyMouseRecorder != null && IsRecording && e is MouseEventExtArgs mouseArgs)
        {
            _keyMouseRecorder.MouseWheel(mouseArgs);
        }
    }

    #endregion

    public string GetSessionId() => _recordId;

    /// <summary>
    /// 更新视频帧率（仅在非录制状态下可用）
    /// </summary>
    /// <param name="frameRate">新的帧率</param>
    public void UpdateFrameRate(int frameRate)
    {
        if (frameRate <= 0)
        {
            throw new ArgumentException("帧率必须大于0", nameof(frameRate));
        }

        _frameRate = frameRate;
        _logger.LogInformation("视频帧率已更新为: {FrameRate} FPS (将在下次录制时生效)", _frameRate);
    }

    /// <summary>
    /// 更新坐标记录间隔（仅在非录制状态下可用）
    /// </summary>
    /// <param name="interval">新的间隔（毫秒）</param>
    public void UpdateCoordinateInterval(int interval)
    {
        if (interval <= 0)
        {
            throw new ArgumentException("间隔必须大于0", nameof(interval));
        }

        _coordinateInterval = interval;
        _logger.LogInformation("坐标记录间隔已更新为: {Interval}ms (将在下次录制时生效)", _coordinateInterval);
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            Task.Run(async () => await StopRecordingAsync()).Wait();
        }
        GC.SuppressFinalize(this);
    }
}

public class RecordingMetadata
{
    public string RecordId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
}