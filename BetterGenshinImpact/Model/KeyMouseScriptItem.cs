using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers.Upload;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

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

    [RelayCommand]
    private async Task Upload()
    {
        try
        {
            _uploadCts = new CancellationTokenSource();
            IsUploading = true;
            IsUploadSuccess = false;
            UploadProgress = 0;
            
            var dirName = new DirectoryInfo(Path).Name;
            _logger.LogDebug($"{dirName} 开始上传...");
            
            var userName = TaskContext.Instance().Config.CommonConfig.UserName;
            var uid = TaskContext.Instance().Config.CommonConfig.Uid;
            
            await Task.Run(() =>
            {
                try
                {
                    var tosClient = new TosClientHelper();
                    var files = Directory.GetFiles(Path, "*.*", SearchOption.AllDirectories);
                    
                    // 计算所有文件的总大小
                    long totalSize = 0;
                    long uploadedSize = 0;
                    foreach (var file in files)
                    {
                        totalSize += new FileInfo(file).Length;
                    }

                    foreach (var file in files)
                    {
                        if (_uploadCts.Token.IsCancellationRequested)
                            break;

                        var relativePath = file.Replace(_scriptPath, "").TrimStart('\\');
                        var needUploadFileName = System.IO.Path.GetFileName(file);
                        var remotePath = $"{dirName[..10]}_{userName}_{uid}/{relativePath}";
                        remotePath = remotePath.Replace(@"\", "/");
                        var fileSize = new FileInfo(file).Length;

                        if (needUploadFileName == "video.mkv" || needUploadFileName == "video.mp4")
                        {
                            tosClient.UploadLargeFile(file, remotePath, 20 * 1024 * 1024, (bytes, totalBytes, percentage) => 
                            {
                                var currentFileProgress = bytes;
                                var overallProgress = ((double)(uploadedSize + currentFileProgress) / totalSize) * 100;
                                UploadProgress = Math.Min(overallProgress, 99.9); // 保留最后0.1%给上传完成时
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
}