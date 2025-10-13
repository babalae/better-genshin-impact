using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.GameTask;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class IntegratedRecordPageViewModel : ViewModel
{
    private readonly ILogger<IntegratedRecordPageViewModel> _logger = App.GetLogger<IntegratedRecordPageViewModel>();
    
    [ObservableProperty]
    private string _recordingStatus = "未开始";

    [ObservableProperty]
    private Brush _statusColor = Brushes.Gray;

    [ObservableProperty]
    private bool _canStartRecord = true;

    [ObservableProperty]
    private bool _canStopRecord;

    [ObservableProperty]
    private string _recordingDuration = "";

    [ObservableProperty]
    private string _sessionId = "";

    [ObservableProperty]
    private string _outputDirectory = Global.Absolute(@"User\IntegratedRecords");

    [ObservableProperty]
    private int _videoFrameRate = 15;

    [ObservableProperty]
    private int _coordinateRecordInterval = 500;

    [ObservableProperty]
    private ObservableCollection<IntegratedRecordItem> _recordList = [];

    private IntegratedRecorder? _integratedRecorder;
    private DispatcherTimer? _statusTimer;
    private DateTime _recordingStartTime;

    public IntegratedRecordPageViewModel()
    {
        _logger.LogInformation("IntegratedRecordPageViewModel 构造函数被调用");
        LoadRecordList();
        _logger.LogInformation("IntegratedRecordPageViewModel 构造函数完成，RecordList中有 {Count} 条记录", RecordList.Count);
    }

    [RelayCommand]
    private void Loaded()
    {
        // 页面加载时的初始化操作
        _logger.LogInformation("综合录制页面已加载，当前记录数: {Count}", RecordList.Count);
        
        // 可以在这里添加设计时数据的加载逻辑
        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            // 设计时模式，添加示例数据用于预览
            _logger.LogInformation("设计时模式，加载设计时数据");
            LoadDesignTimeData();
        }
        else
        {
            _logger.LogInformation("运行时模式，重新加载记录列表");
            LoadRecordList();
        }
    }

    private void LoadDesignTimeData()
    {
        // 添加设计时示例数据
        if (RecordList.Count != 0) return;
        
        var item1 = new IntegratedRecordItem();
        item1.SessionId = "DESIGN_001";
        item1.StartTime = DateTime.Now.AddHours(-2);
        item1.Duration = TimeSpan.FromMinutes(30);
        RecordList.Add(item1);
            
        var item2 = new IntegratedRecordItem();
        item2.SessionId = "DESIGN_002";
        item2.StartTime = DateTime.Now.AddHours(-1);
        item2.Duration = TimeSpan.FromMinutes(45);
        RecordList.Add(item2);
        
        _logger.LogInformation("设计时数据已加载: {Count} 条记录", RecordList.Count);
    }

    [RelayCommand]
    private async Task StartRecord()
    {
        // 检查TaskContext是否初始化
        if (!TaskContext.Instance().IsInitialized)
        {
            MessageBox.Show("请先在启动页启动截图器再使用本功能", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_integratedRecorder != null)
        {
            MessageBox.Show("录制已在进行中", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // 确保输出目录存在
            if (!Directory.Exists(OutputDirectory))
            {
                Directory.CreateDirectory(OutputDirectory);
            }

            // 创建录制器
            _integratedRecorder = new IntegratedRecorder(OutputDirectory, VideoFrameRate, CoordinateRecordInterval);
            
            if (_integratedRecorder == null)
            {
                throw new InvalidOperationException("录制器创建失败");
            }
            
            // 开始录制
            await _integratedRecorder.StartRecordingAsync();
            
            // 更新状态
            RecordingStatus = "录制中";
            StatusColor = Brushes.Red;
            CanStartRecord = false;
            CanStopRecord = true;
            SessionId = _integratedRecorder.GetSessionId();
            _recordingStartTime = DateTime.Now;
            
            // 启动状态计时器
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusTimer.Tick += UpdateRecordingDuration;
            _statusTimer.Start();
            
            _logger.LogInformation("综合录制已开始，会话ID: {SessionId}", SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "开始综合录制失败");
            
            // 重置状态
            RecordingStatus = "录制失败";
            StatusColor = Brushes.Red;
            CanStartRecord = true;
            CanStopRecord = false;
            RecordingDuration = "";
            SessionId = "";
            
            _integratedRecorder?.Dispose();
            _integratedRecorder = null;
            
            MessageBox.Show($"开始录制失败: {ex.Message}\n详情: {ex.GetType().Name}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateRecordingDuration(object? sender, EventArgs e)
    {
        if (_integratedRecorder != null)
        {
            var duration = DateTime.Now - _recordingStartTime;
            RecordingDuration = $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        }
    }

    [RelayCommand]
    private async Task StopRecord()
    {
        if (_integratedRecorder == null)
        {
            return;
        }

        try
        {
            await _integratedRecorder.StopRecordingAsync();

            // 更新状态
            RecordingStatus = "已停止";
            StatusColor = Brushes.Green;
            CanStartRecord = true;
            CanStopRecord = false;
            
            _logger.LogInformation("综合录制已停止");
            
            // 刷新记录列表
            LoadRecordList();
            
            MessageBox.Show("录制已完成，文件已保存到:\n" + OutputDirectory, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止综合录制失败");
            MessageBox.Show($"停止录制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _integratedRecorder?.Dispose();
            _integratedRecorder = null;
        }
    }

    [RelayCommand]
    private void SelectOutputDirectory()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择录制输出目录",
            SelectedPath = OutputDirectory
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputDirectory = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void RefreshRecordList()
    {
        LoadRecordList();
    }

    [RelayCommand]
    private void OpenRecordDirectory()
    {
        if (Directory.Exists(OutputDirectory))
        {
            Process.Start(new ProcessStartInfo(OutputDirectory) { UseShellExecute = true });
        }
        else
        {
            MessageBox.Show("目录不存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void OpenRecordFolder(IntegratedRecordItem? item)
    {
        if (item == null) return;

        var recordDirectory = Path.Combine(OutputDirectory, item.SessionId);
        if (Directory.Exists(recordDirectory))
        {
            Process.Start(new ProcessStartInfo(recordDirectory) { UseShellExecute = true });
        }
        else
        {
            MessageBox.Show("录制目录不存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void DeleteRecord(IntegratedRecordItem? item)
    {
        if (item == null) return;

        var result = MessageBox.Show($"确定要删除录制记录 '{item.SessionId}' 吗？\n这将删除所有相关文件。", 
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var recordDirectory = Path.Combine(OutputDirectory, item.SessionId);
                if (Directory.Exists(recordDirectory))
                {
                    Directory.Delete(recordDirectory, true);
                }

                RecordList.Remove(item);
                _logger.LogInformation("已删除录制记录: {SessionId}", item.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除录制记录失败");
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadRecordList()
    {
        try
        {
            RecordList.Clear();
            _logger.LogInformation("开始加载录制记录列表，输出目录: {OutputDirectory}", OutputDirectory);

            if (Directory.Exists(OutputDirectory))
            {
                var directories = Directory.GetDirectories(OutputDirectory)
                    .Select(dir => new DirectoryInfo(dir))
                    .OrderByDescending(dir => dir.CreationTime);

                _logger.LogInformation("找到 {Count} 个录制目录", directories.Count());

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                foreach (var dir in directories)
                {
                    var metadataFile = Path.Combine(dir.FullName, "metadata.json");
                    if (File.Exists(metadataFile))
                    {
                        try
                        {
                            var metadataJson = File.ReadAllText(metadataFile);
                            _logger.LogInformation("加载元数据文件: {File}", metadataFile);
                            _logger.LogInformation("JSON内容: {Json}", metadataJson);
                            
                            var metadata = System.Text.Json.JsonSerializer.Deserialize<RecordingMetadata>(metadataJson, options);
                            
                            if (metadata != null)
                            {
                                _logger.LogInformation("成功反序列化元数据: RecordId={RecordId}, StartTime={StartTime}, Duration={Duration}", 
                                    metadata.RecordId, metadata.StartTime, metadata.Duration);
                                
                                var item = new IntegratedRecordItem();
                                item.SessionId = metadata.RecordId;
                                item.StartTime = metadata.StartTime;
                                item.Duration = metadata.Duration;
                                RecordList.Add(item);
                                
                                _logger.LogInformation("添加记录到列表: SessionId={SessionId}", item.SessionId);
                            }
                            else
                            {
                                _logger.LogWarning("反序列化元数据为null");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "加载录制元数据失败: {Directory}", dir.FullName);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("元数据文件不存在: {File}", metadataFile);
                    }
                }
                
                _logger.LogInformation("加载完成，共 {Count} 条记录", RecordList.Count);
            }
            else
            {
                _logger.LogWarning("输出目录不存在: {OutputDirectory}", OutputDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载录制记录列表失败");
        }
    }

    public void Cleanup()
    {
        _statusTimer?.Stop();
        _integratedRecorder?.Dispose();
    }

    // 属性更改通知
    partial void OnVideoFrameRateChanged(int value)
    {
        _logger.LogInformation("视频帧率已更改为: {FrameRate}", value);
        
        // 只有在非录制状态下才能修改参数（通过检查_integratedRecorder是否为null来判断）
        if (_integratedRecorder == null)
        {
            // 非录制状态下，参数会保存在属性中，下次录制时会使用新值
            _logger.LogInformation("视频帧率参数已更新，将在下次录制时使用: {FrameRate} FPS", value);
        }
        else
        {
            _logger.LogWarning("录制进行中，无法修改视频帧率。请先停止录制后再修改。");
            MessageBox.Show("录制进行中，无法修改视频帧率。请先停止录制后再修改。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    partial void OnCoordinateRecordIntervalChanged(int value)
    {
        _logger.LogInformation("坐标记录间隔已更改为: {Interval}ms", value);
        
        // 只有在非录制状态下才能修改参数（通过检查_integratedRecorder是否为null来判断）
        if (_integratedRecorder == null)
        {
            // 非录制状态下，参数会保存在属性中，下次录制时会使用新值
            _logger.LogInformation("坐标记录间隔参数已更新，将在下次录制时使用: {Interval}ms", value);
        }
        else
        {
            _logger.LogWarning("录制进行中，无法修改坐标记录间隔。请先停止录制后再修改。");
            MessageBox.Show("录制进行中，无法修改坐标记录间隔。请先停止录制后再修改。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

public partial class IntegratedRecordItem : ObservableObject
{
    [ObservableProperty] 
    private string _sessionId = string.Empty;
    
    [ObservableProperty] 
    private DateTime _startTime;
    
    [ObservableProperty] 
    private TimeSpan _duration;
}

public class RecordingMetadata
{
    public string RecordId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
}