using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Upload;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class UploadDialog
{
    private readonly string scriptPath = Global.Absolute(@"User\KeyMouseScript");
    
    private readonly string path;
    private bool _isUploading;
    
    public UploadDialog(string path)
    {
        InitializeComponent();
        this.path = path;
        this.Loaded += UploadDialogLoaded;
        this.Closing += UploadDialog_Closing;
    }

    private void UploadDialogLoaded(object sender, RoutedEventArgs e)
    {
    }
    
    private void UploadDialog_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isUploading)
        {
            e.Cancel = true;
            AppendLog("正在上传中，请等待上传完成...");
        }
    }
    
    private void AppendLog(string message)
    {
        LogTextBox.AppendText(message + "\n");
        LogTextBox.ScrollToEnd();
    }
    
    private async void BtnOkClick(object sender, RoutedEventArgs e)
    {
        try 
        {
            _isUploading = true;
            var dirName = new DirectoryInfo(path).Name;
            
            BtnOk.IsEnabled = false;
            BtnDelete.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            UploadProgressBar.Value = 0;
            AppendLog($"{dirName} 开始上传...请不要关闭此窗口！");
            
            var userName = TaskContext.Instance().Config.CommonConfig.UserName;
            var uid = TaskContext.Instance().Config.CommonConfig.Uid;
            
            await Task.Run(() =>
            {
            
                // 上传
                try
                {
                    var tosClient = new TosClientHelper();

                    // 循环 path 下的所有文件
                    var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var relativePath = file.Replace(scriptPath, "").TrimStart('\\');
                        var needUploadFileName = Path.GetFileName(file);
                        var remotePath = $"{dirName[..10]}_{userName}_{uid}/{relativePath}";
                        remotePath = remotePath.Replace(@"\", "/");

                        if (needUploadFileName == "video.mkv" || needUploadFileName == "video.mp4")
                        {
                            tosClient.UploadLargeFile(file, remotePath, 20 * 1024 * 1024, (bytes, totalBytes, percentage) => 
                            {
                                UIDispatcherHelper.Invoke(() =>
                                {
                                    UploadProgressBar.Value = percentage;
                                    AppendLog($"上传进度: {percentage:F}%");
                                });
                            });
                        }
                        else
                        {
                            tosClient.UploadFile(file, remotePath);
                        }

                    
                    }
                    
                    UIDispatcherHelper.Invoke(() =>
                    {
                        UploadProgressBar.Value = 100;
                        AppendLog($"{dirName} 上传完成");
                    });
                }
                catch (Exception ex)
                {
                    UIDispatcherHelper.Invoke(() =>
                    {
                        AppendLog($"上传过程中发生错误：{ex.Message}");
                    });
                }
            });
            
        }
        catch (Exception ex)
        {
            AppendLog($"上传出错: {ex.Message}");
            TaskControl.Logger.LogDebug("上传出错" + ex.Source + "\r\n--" + Environment.NewLine + ex.StackTrace + "\r\n---" + Environment.NewLine + ex.Message);
        }
        finally
        {
            BtnOk.IsEnabled = true;
            BtnDelete.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            _isUploading = false;
        }
    }
    
    private async void BtnDeleteClick(object sender, RoutedEventArgs e)
    {
        try 
        {
            var dirName = new DirectoryInfo(path).Name;
            var result = MessageBox.Show($"确定要删除已上传的 {dirName} 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            BtnOk.IsEnabled = false;
            BtnDelete.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            UploadProgressBar.Value = 0;
            AppendLog($"开始删除 {dirName} ...");
            
            var userName = TaskContext.Instance().Config.CommonConfig.UserName;
            var uid = TaskContext.Instance().Config.CommonConfig.Uid;
            
            await Task.Run(() =>
            {
                try
                {
                    var tosClient = new TosClientHelper();
                    var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                    var totalFiles = files.Length;
                    var currentFile = 0;
                    
                    foreach (var file in files)
                    {
                        var relativePath = file.Replace(scriptPath, "").TrimStart('\\');
                        var remotePath = $"{dirName[..10]}_{userName}_{uid}/{relativePath}";
                        remotePath = remotePath.Replace(@"\", "/");
                        
                        tosClient.DeleteObject(remotePath);
                        currentFile++;
                        
                        var percentage = (double)currentFile / totalFiles * 100;
                        UIDispatcherHelper.Invoke(() =>
                        {
                            UploadProgressBar.Value = percentage;
                            AppendLog($"删除进度: {percentage:F}%");
                        });
                    }
                    
                    UIDispatcherHelper.Invoke(() =>
                    {
                        UploadProgressBar.Value = 100;
                        AppendLog($"{dirName} 删除完成");
                    });
                }
                catch (Exception ex)
                {
                    UIDispatcherHelper.Invoke(() =>
                    {
                        AppendLog($"删除过程中发生错误：{ex.Message}");
                    });
                }
            });
        }
        catch (Exception ex)
        {
            AppendLog($"删除出错: {ex.Message}");
            TaskControl.Logger.LogDebug("删除出错" + ex.Source + "\r\n--" + Environment.NewLine + ex.StackTrace + "\r\n---" + Environment.NewLine + ex.Message);
        }
        finally
        {
            BtnOk.IsEnabled = true;
            BtnDelete.IsEnabled = true;
            BtnCancel.IsEnabled = true;
        }
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        if (!_isUploading)
        {
            Close();
        }
        else
        {
            AppendLog("正在上传中，请不要关闭窗口！");
        }
    }
}
