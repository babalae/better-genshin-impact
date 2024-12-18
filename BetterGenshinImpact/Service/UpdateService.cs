using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Threading;
using BetterGenshinImpact.View.Windows;
using Downloader;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.Service;

public class UpdateService : IUpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly IConfigService _configService;

    private const string InstallationUrlPrefix = "https://raw.githubusercontent.com/bettergi/bettergi-installation-data/refs/heads/main/installation";
    private const string HashUrl = "https://raw.githubusercontent.com/bettergi/bettergi-installation-data/refs/heads/main/hash.json";
    private const string NoticUrl = "https://hui-config.oss-cn-hangzhou.aliyuncs.com/bgi/notice.json";
    private const string DownloadPageUrl = "https://bgi.huiyadan.com/download.html";

    public AllConfig Config { get; set; }

    public UpdateService(IConfigService configService)
    {
        _logger = App.GetLogger<UpdateService>();
        _configService = configService;
        Config = _configService.Get();
    }

    private class XYZ
    {
        public string X { get; set; } = string.Empty;
        public string Y { get; set; } = string.Empty;
        public string Z { get; set; } = string.Empty;
    }

    /// <summary>
    /// Please call me in main thread
    /// </summary>
    /// <param name="option"></param>
    public async Task CheckUpdateAsync(UpdateOption option)
    {
        try
        {
            string newVersion = await GetLatestVersionAsync();

#if DEBUG && true
            newVersion = "256.256.256.256";
#endif

            if (string.IsNullOrWhiteSpace(newVersion))
            {
                return;
            }

            if (!Global.IsNewVersion(newVersion))
            {
                return;
            }

            if (!string.IsNullOrEmpty(Config.NotShowNewVersionNoticeEndVersion)
                && !Global.IsNewVersion(Config.NotShowNewVersionNoticeEndVersion, newVersion))
            {
                return;
            }

            CheckUpdateWindow win = new()
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = $"发现新版本 {newVersion}",
                UserInteraction = async (sender, button) =>
                {
                    CheckUpdateWindow win = (CheckUpdateWindow)sender;
                    CancellationTokenSource? tokenSource = new();

                    switch (button)
                    {
                        case CheckUpdateWindow.CheckUpdateWindowButton.BackgroundUpdate:
                            // TBD
                            break;

                        case CheckUpdateWindow.CheckUpdateWindowButton.OtherUpdate:
                            Process.Start(new ProcessStartInfo(DownloadPageUrl) { UseShellExecute = true });
                            break;

                        case CheckUpdateWindow.CheckUpdateWindowButton.Update:
                            {
                                win.ShowUpdateStatus = true;

                                try
                                {
                                    win.UpdateStatusMessage = "正在获取更新代理...";

                                    (string fastProxyUrl, string jsonString) = await ProxySpeedTester.GetFastestProxyAsync(HashUrl);

                                    win.UpdateStatusMessage = $"获取更新代理成功 {fastProxyUrl.Replace("{0}", string.Empty)}";

                                    Dictionary<string, string>? hashDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);

                                    if (hashDict == null)
                                    {
                                        win.UpdateStatusMessage = "获取更新列表失败，更新已取消。";
                                        return;
                                    }

                                    List<(string, string, string)> downloadList = [];

                                    foreach (KeyValuePair<string, string> hashPair in hashDict)
                                    {
                                        string targetFileName = Path.GetFullPath($@".\update\{hashPair.Key}.zip");
                                        string sourceFileName = Path.GetFullPath($@".\{hashPair.Value}");

                                        if (IsNeedDownload(sourceFileName, hashPair.Value))
                                        {
                                            string url = string.Format(fastProxyUrl, $"{InstallationUrlPrefix}/{hashPair.Key}.zip").Replace("\\", "/");
                                            downloadList.Add((hashPair.Key, url, targetFileName));
                                        }
                                    }

                                    SemaphoreSlimParallel downloadParallel = await SemaphoreSlimParallel.ForEach(downloadList, async item =>
                                    {
                                        DownloadConfiguration downloadOpt = new();
                                        DownloadService downloader = new(downloadOpt);

                                        //downloader.DownloadFileCompleted += (sender, e) =>
                                        //{
                                        //    if (e.Error != null)
                                        //    {
                                        //        _logger.LogError(e.Error, "下载失败");
                                        //        win.UpdateStatusMessage = $"下载失败：{e.Error.Message}";
                                        //    }
                                        //};

                                        (string shortName, string url, string fileName) = item;

                                        win.UpdateStatusMessage = $"正在下载 {shortName}";
                                        await downloader.DownloadFileTaskAsync(url, fileName, cancellationToken: tokenSource.Token);

                                        win.UpdateStatusMessage = $"正在解压 {shortName}";
                                    }, 3);

                                    await downloadParallel.DisposeAsync();

                                    win.UpdateStatusMessage = "下载已完成";
                                }
                                catch (Exception e)
                                {
                                    _logger.LogError(e, "更新失败");
                                    win.UpdateStatusMessage = $"更新失败：{e.Message}";
                                }
                            }
                            break;

                        case CheckUpdateWindow.CheckUpdateWindowButton.Ignore:
                            Config.NotShowNewVersionNoticeEndVersion = newVersion;
                            win.Close();
                            break;

                        case CheckUpdateWindow.CheckUpdateWindowButton.Cancel:
                            if (tokenSource != null)
                            {
                                if (MessageBox.Question("正在更新中，确定要取消更新吗？") == MessageBoxResult.Yes)
                                {
                                    win.ShowUpdateStatus = false;
                                    tokenSource?.Cancel();
                                    win.Close();
                                }
                            }
                            else
                            {
                                win.ShowUpdateStatus = false;
                                win.Close();
                            }
                            break;
                    }
                }
            };

            win.NavigateToHtml(await GetReleaseMarkdownHtmlAsync());
            win.ShowDialog();
        }
        catch (Exception e)
        {
            Debug.WriteLine("获取最新版本信息失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
            _logger.LogWarning("获取 BetterGI 最新版本信息失败");
        }
    }

    private async Task<string> GetLatestVersionAsync()
    {
        try
        {
            using HttpClient httpClient = new();
            Notice? notice = await httpClient.GetFromJsonAsync<Notice>(NoticUrl);

            if (notice != null)
            {
                return notice.Version;
            }
        }
        catch (Exception e)
        {
            _ = e;
        }

        return string.Empty;
    }

    private async Task<string> GetReleaseMarkdownHtmlAsync()
    {
        try
        {
            using HttpClient httpClient = new();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            string jsonString = await httpClient.GetStringAsync("https://api.github.com/repos/babalae/better-genshin-impact/releases/latest");
            var jsonDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);

            if (jsonDict != null)
            {
                string? name = jsonDict["name"] as string;
                string? body = jsonDict["body"] as string;
                string md = $"# {name}{new string('\n', 2)}{body}";

                md = WebUtility.HtmlEncode(md);
                string md2html = ResourceHelper.GetString($"pack://application:,,,/Assets/Strings/md2html.html", Encoding.UTF8);
                var html = md2html.Replace("{{content}}", md);

                return html;
            }
        }
        catch (Exception e)
        {
            _ = e;
        }

        return GetReleaseMarkdownHtmlFallback();
    }

    private string GetReleaseMarkdownHtmlFallback()
    {
        return
            """
            <!DOCTYPE html>
            <html lang="zh">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>更新日志</title>
                <style>
                    body {
                        background-color: #212121;
                        color: white;
                        font-family: Arial, sans-serif;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        height: 100vh;
                        margin: 0;
                    }
                    .message {
                        text-align: center;
                        font-size: 20px;
                    }
                </style>
            </head>
            <body>
                <div class="message">
                    获取更新日志失败，请自行选择是否更新！
                </div>
            </body>
            </html>
            """;
    }

    private static bool IsNeedDownload(string sourceFileName, string targetSha256)
    {
        if (File.Exists(sourceFileName))
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream fileStream = new(sourceFileName, FileMode.Open, FileAccess.Read);
            byte[] hashBytes = sha256.ComputeHash(fileStream);
            string sourceSha256 = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToUpper();

            if (sourceSha256 == targetSha256)
            {
                return false;
            }
        }
        return true;
    }
}
