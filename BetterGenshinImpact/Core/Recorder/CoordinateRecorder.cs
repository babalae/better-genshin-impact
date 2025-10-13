using BetterGenshinImpact.GameTask;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Recorder;

public class CoordinateRecorder
{
    private readonly ILogger<CoordinateRecorder> _logger = App.GetLogger<CoordinateRecorder>();
    
    private List<CoordinateRecord> _records = new();
    private bool _isRecording = false;
    private DateTime _startTime;
    private readonly object _lockObject = new();

    public bool IsRecording => _isRecording;

    public void StartRecording()
    {
        if (_isRecording)
        {
            _logger.LogWarning("坐标录制已在进行中");
            return;
        }

        lock (_lockObject)
        {
            _records.Clear();
            _startTime = DateTime.Now;
            _isRecording = true;
        }

        _logger.LogInformation("坐标录制已启动");
    }

    public void StopRecording()
    {
        if (!_isRecording)
        {
            _logger.LogWarning("坐标录制未在进行中");
            return;
        }

        _isRecording = false;
        _logger.LogInformation("坐标录制已停止，共记录 {Count} 个坐标点", _records.Count);
    }

    public void RecordCoordinate(double x, double y, string? description = null)
    {
        if (!_isRecording) return;

        try
        {
            var record = new CoordinateRecord
            {
                Timestamp = DateTime.Now,
                RelativeTime = (DateTime.Now - _startTime).TotalMilliseconds,
                X = x,
                Y = y,
                Description = description
            };

            lock (_lockObject)
            {
                _records.Add(record);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "记录坐标时发生错误");
        }
    }

    public void RecordCurrentCoordinate(string? description = null)
    {
        if (!_isRecording) return;

        try
        {
            var systemInfo = TaskContext.Instance().SystemInfo;
            var captureArea = systemInfo.CaptureAreaRect;
            
            // 记录当前捕获区域的中心坐标
            var centerX = captureArea.X + captureArea.Width / 2.0;
            var centerY = captureArea.Y + captureArea.Height / 2.0;
            
            RecordCoordinate(centerX, centerY, description ?? "自动记录");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "记录当前坐标时发生错误");
        }
    }

    public async Task SaveToFileAsync(string filePath)
    {
        if (_records.Count == 0)
        {
            _logger.LogWarning("没有坐标数据可保存");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(new CoordinateRecordData
            {
                StartTime = _startTime,
                EndTime = _records.Count > 0 ? _records[^1].Timestamp : _startTime,
                RecordCount = _records.Count,
                Records = _records
            }, options);

            await File.WriteAllTextAsync(filePath, json);
            _logger.LogInformation("坐标数据已保存到: {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存坐标数据时发生错误");
            throw;
        }
    }

    public void ClearRecords()
    {
        lock (_lockObject)
        {
            _records.Clear();
        }
        _logger.LogInformation("坐标记录已清空");
    }

    public List<CoordinateRecord> GetRecords()
    {
        lock (_lockObject)
        {
            return new List<CoordinateRecord>(_records);
        }
    }
}

public class CoordinateRecord
{
    public DateTime Timestamp { get; set; }
    public double RelativeTime { get; set; } // 相对于录制开始的时间（毫秒）
    public double X { get; set; }
    public double Y { get; set; }
    public string? Description { get; set; }
}

public class CoordinateRecordData
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int RecordCount { get; set; }
    public List<CoordinateRecord> Records { get; set; } = new();
}