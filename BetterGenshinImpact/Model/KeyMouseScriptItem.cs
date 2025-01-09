using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers.Upload;
using System.IO;
using System.Security.Cryptography;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System.Windows;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Model;

public partial class KeyMouseScriptItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _createTimeStr = string.Empty;

    public DateTime CreateTime { get; set; }

    public string Path { get; set; }

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private double _uploadProgress;

    private CancellationTokenSource? _uploadCts;

    private readonly string _scriptPath = Global.Absolute(@"User\KeyMouseScript");
    private readonly ILogger _logger = TaskControl.Logger;

    [ObservableProperty]
    private bool _isUploadSuccess;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private double _deleteProgress;

    [ObservableProperty]
    private bool _isDeleteSuccess;

    private CancellationTokenSource? _deleteCts;

    [ObservableProperty]
    private string _uploadSpeed = string.Empty;

    private DateTime _lastProgressUpdateTime = DateTime.Now;
    private long _lastUploadedSize = 0;

    private string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024) // MB/s
        {
            return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
        }
        if (bytesPerSecond >= 1024) // KB/s
        {
            return $"{bytesPerSecond / 1024:F1} KB/s";
        }
        return $"{bytesPerSecond:F0} B/s";
    }

    [RelayCommand]
    private async Task Upload()
    {
        
        if (string.IsNullOrEmpty(Path) || !Directory.Exists(Path))
        {
            await MessageBox.ErrorAsync($"文件夹不存在:{Path}");
            return;
        }

        var userName = TaskContext.Instance().Config.CommonConfig.UserName;
        var uid = TaskContext.Instance().Config.CommonConfig.Uid;
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(uid))
        {
            await MessageBox.ErrorAsync("请先设置用户名和UID");
            return;
        }

        var hashFolder = Global.Absolute(@$"User/Common/Km/{new DirectoryInfo(Path).Name}");
        // 先校验hash
        if (!VerifyFileHashes(Path, hashFolder))
        {
            await MessageBox.ErrorAsync("上传前文件校验失败，联系管理员");
            return;
        }
        
        try
        {
            _uploadCts = new CancellationTokenSource();
            IsUploading = true;
            IsUploadSuccess = false;
            UploadProgress = 0;
            
            var dirName = new DirectoryInfo(Path).Name;
            _logger.LogDebug($"{dirName} 开始上传...");
            
            // var userName = TaskContext.Instance().Config.CommonConfig.UserName;
            // var uid = TaskContext.Instance().Config.CommonConfig.Uid;
            
            await Task.Run(() =>
            {
                try
                {
                    var tosClient = new TosClientHelper();
                    var files = Directory.GetFiles(Path, "*.*", SearchOption.AllDirectories);
                    
                    // 计算所有文件的总大小
                    long totalSize = 0;
                    long uploadedSize = 0;
                    _lastUploadedSize = 0;
                    _lastProgressUpdateTime = DateTime.Now;
                    
                    foreach (var file in files)
                    {
                        _uploadCts.Token.ThrowIfCancellationRequested();
                        totalSize += new FileInfo(file).Length;
                    }

                    foreach (var file in files)
                    {
                        _uploadCts.Token.ThrowIfCancellationRequested();

                        var relativePath = file.Replace(_scriptPath, "").TrimStart('\\');
                        var needUploadFileName = System.IO.Path.GetFileName(file);
                        var remotePath = $"{dirName[..10]}_{userName}_{uid}/{relativePath}";
                        remotePath = remotePath.Replace(@"\", "/");
                        var fileSize = new FileInfo(file).Length;

                        if (needUploadFileName == "video.mkv" || needUploadFileName == "video.mp4")
                        {
                            tosClient.UploadLargeFile(file, remotePath, 20 * 1024 * 1024, (bytes, totalBytes, percentage) => 
                            {
                                _uploadCts.Token.ThrowIfCancellationRequested();
                                var currentFileProgress = bytes;
                                var overallProgress = ((double)(uploadedSize + currentFileProgress) / totalSize) * 100;
                                UploadProgress = Math.Min(overallProgress, 99.9);
                                
                                // 计算速度
                                var now = DateTime.Now;
                                var timeDiff = (now - _lastProgressUpdateTime).TotalSeconds;
                                if (timeDiff >= 0.5) // 每0.5秒更新一次速度
                                {
                                    var sizeDiff = uploadedSize + currentFileProgress - _lastUploadedSize;
                                    var speed = sizeDiff / timeDiff;
                                    UploadSpeed = FormatSpeed(speed);
                                    
                                    _lastProgressUpdateTime = now;
                                    _lastUploadedSize = uploadedSize + currentFileProgress;
                                }
                                
                                _logger.LogDebug($"上传进度: {overallProgress:F}%");
                            });
                        }
                        else
                        {
                            tosClient.UploadFile(file, remotePath);
                        }
                        
                        uploadedSize += fileSize;
                        var progress = ((double)uploadedSize / totalSize) * 100;
                        UploadProgress = Math.Min(progress, 99.9);
                    }
                    
                    UploadProgress = 100;
                    UploadSpeed = string.Empty; // 清空速度显示
                    IsUploadSuccess = true;
                    _logger.LogDebug($"{dirName} 上传完成");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"上传过程中发生错误：{ex.Message}");
                    IsUploadSuccess = false;
                    throw;
                }
            }, _uploadCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("上传已取消");
            IsUploadSuccess = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "上传出错");
            IsUploadSuccess = false;
        }
        finally
        {
            IsUploading = false;
            UploadSpeed = string.Empty; // 确保清空速度显示
            if (!IsUploadSuccess)
            {
                UploadProgress = 0;
            }
            _uploadCts?.Dispose();
            _uploadCts = null;
        }
    }

    [RelayCommand]
    private void StopUpload()
    {
        _uploadCts?.Cancel();
    }

    [RelayCommand]
    private async Task DeleteUploadedFiles()
    {
        try
        {
            var dirName = new DirectoryInfo(Path).Name;
            // 删除确认
            var result = MessageBox.Show($"确定要清除已上传的 {dirName} 吗？", "确认清除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _deleteCts = new CancellationTokenSource();
            IsDeleting = true;
            IsDeleteSuccess = false;
            DeleteProgress = 0;
            
            _logger.LogDebug($"{dirName} 开始删除...");
            
            var userName = TaskContext.Instance().Config.CommonConfig.UserName;
            var uid = TaskContext.Instance().Config.CommonConfig.Uid;
            
            await Task.Run(() =>
            {
                try
                {
                    var tosClient = new TosClientHelper();
                    var files = Directory.GetFiles(Path, "*.*", SearchOption.AllDirectories);
                    
                    foreach (var file in files)
                    {
                        _deleteCts.Token.ThrowIfCancellationRequested();

                        var relativePath = file.Replace(_scriptPath, "").TrimStart('\\');
                        var remotePath = $"{dirName[..10]}_{userName}_{uid}/{relativePath}";
                        remotePath = remotePath.Replace(@"\", "/");
                        
                        tosClient.DeleteObject(remotePath);
                    }
                    
                    IsDeleteSuccess = true;
                    _logger.LogDebug($"{dirName} 删除完成");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"删除过程中发生错误：{ex.Message}");
                    IsDeleteSuccess = false;
                    throw;
                }
            }, _deleteCts.Token);

            // 删除成功提示
            if (IsDeleteSuccess)
            {
                MessageBox.Show("清除成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else 
            {
                MessageBox.Show("清除失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("删除已取消");
            IsDeleteSuccess = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除出错");
            IsDeleteSuccess = false;
            MessageBox.Show($"清除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDeleting = false;
            _deleteCts?.Dispose();
            _deleteCts = null;
        }
    }

    [RelayCommand]
    private void StopDelete()
    {
        _deleteCts?.Cancel();
    }
    
    public bool VerifyFileHashes(string pcFolder, string hashFolder)
    {
        var hashFilePath = System.IO.Path.Combine(hashFolder, "hash.json");
        if (!File.Exists(hashFilePath))
        {
            // 无文件hash信息，直接返回true
            _logger.LogDebug("Hash file not found");
            return true;
        }

        var storedHashes = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(hashFilePath));
        if (storedHashes == null)
        {
            throw new InvalidOperationException("Failed to deserialize hash file");
        }

        foreach (var filePath in Directory.GetFiles(pcFolder, "*.*", SearchOption.AllDirectories))
        {
            using (var stream = File.OpenRead(filePath))
            {
                if (!storedHashes.TryGetValue(System.IO.Path.GetFileName(filePath), out var storedHash))
                {
                    Debug.WriteLine($"Hash not found for {filePath}");
                    continue;
                }

                var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(stream);
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                if (storedHash != hashString)
                {
                    Debug.WriteLine($"Hash mismatch for {filePath}");
                    return false;
                }
            }
        }

        return true;
    }
}