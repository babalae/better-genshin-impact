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

    [RelayCommand]
    private async Task Upload()
    {
        try
        {
            _uploadCts = new CancellationTokenSource();
            IsUploading = true;
            UploadProgress = 0;
            
            var dirName = new DirectoryInfo(Path).Name;
            _logger.LogInformation($"{dirName} 开始上传...");
            
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
                        if (_uploadCts.Token.IsCancellationRequested)
                            break;

                        var relativePath = file.Replace(_scriptPath, "").TrimStart('\\');
                        var needUploadFileName = System.IO.Path.GetFileName(file);
                        var remotePath = $"{dirName[..10]}_{userName}_{uid}/{relativePath}";
                        remotePath = remotePath.Replace(@"\", "/");

                        if (needUploadFileName == "video.mkv" || needUploadFileName == "video.mp4")
                        {
                            tosClient.UploadLargeFile(file, remotePath, 20 * 1024 * 1024, (bytes, totalBytes, percentage) => 
                            {
                                UploadProgress = percentage;
                                _logger.LogInformation($"上传进度: {percentage:F}%");
                            });
                        }
                        else
                        {
                            tosClient.UploadFile(file, remotePath);
                        }
                    }
                    
                    UploadProgress = 100;
                    _logger.LogInformation($"{dirName} 上传完成");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"上传过程中发生错误：{ex.Message}");
                    throw;
                }
            }, _uploadCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("上传已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "上传出错");
        }
        finally
        {
            IsUploading = false;
            UploadProgress = 0;
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