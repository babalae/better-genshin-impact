using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace BetterGenshinImpact.Service;

public sealed class CustomHtmlMaskService
{
    public const string RelativeDirectory = @"User\HtmlMask";
    public const string FileName = "custom.html";

    private const string AutoWindowId = "bettergi-custom-html-mask";
    private const string PreviewWindowId = "bettergi-custom-html-mask-preview";

    private readonly IConfigService _configService;

    public CustomHtmlMaskService(IConfigService configService)
    {
        _configService = configService;
        _configService.Get().MaskWindowConfig.PropertyChanged += OnMaskWindowConfigChanged;
    }

    public string DirectoryPath => Global.Absolute(RelativeDirectory);

    public string HtmlPath => Path.Combine(DirectoryPath, FileName);

    public string ReadHtml()
    {
        EnsureDefaultHtmlFile();
        return File.ReadAllText(HtmlPath, Encoding.UTF8);
    }

    public void SaveHtml(string? html)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(HtmlPath, html ?? string.Empty, Encoding.UTF8);
    }

    public void RestoreDefaultHtml()
    {
        SaveHtml(DefaultHtml);
    }

    public void Preview()
    {
        EnsureDefaultHtmlFile();
        var windowId = HtmlMaskWindow.Show(new Uri(HtmlPath).AbsoluteUri, PreviewWindowId, DirectoryPath);
        HtmlMaskWindow.SetClickThrough(windowId, _configService.Get().MaskWindowConfig.CustomHtmlMaskClickThrough);
    }

    public void ClosePreview()
    {
        HtmlMaskWindow.Close(PreviewWindowId);
    }

    public void RefreshAutoWindow()
    {
        if (_configService.Get().MaskWindowConfig.CustomHtmlMaskEnabled)
        {
            CloseAutoWindow();
            ShowIfEnabled();
            return;
        }

        CloseAutoWindow();
    }

    public void ShowIfEnabled()
    {
        var config = _configService.Get().MaskWindowConfig;
        if (!config.CustomHtmlMaskEnabled)
        {
            CloseAutoWindow();
            return;
        }

        var maskWindow = MaskWindow.InstanceNullable();
        if (TaskContext.Instance().GameHandle == IntPtr.Zero || maskWindow?.IsVisible != true)
        {
            return;
        }

        if (HtmlMaskWindow.Exists(AutoWindowId))
        {
            if (HtmlMaskWindow.GetClickThrough(AutoWindowId) != config.CustomHtmlMaskClickThrough)
            {
                HtmlMaskWindow.SetClickThrough(AutoWindowId, config.CustomHtmlMaskClickThrough);
            }

            return;
        }

        EnsureDefaultHtmlFile();
        var windowId = HtmlMaskWindow.Show(new Uri(HtmlPath).AbsoluteUri, AutoWindowId, DirectoryPath);
        HtmlMaskWindow.SetClickThrough(windowId, config.CustomHtmlMaskClickThrough);
    }

    public void CloseAutoWindow()
    {
        HtmlMaskWindow.Close(AutoWindowId);
    }

    private void EnsureDefaultHtmlFile()
    {
        if (File.Exists(HtmlPath))
        {
            return;
        }

        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(HtmlPath, DefaultHtml, Encoding.UTF8);
    }

    private void OnMaskWindowConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaskWindowConfig.CustomHtmlMaskEnabled))
        {
            RefreshAutoWindow();
            return;
        }

        if (e.PropertyName == nameof(MaskWindowConfig.CustomHtmlMaskClickThrough)
            && HtmlMaskWindow.Exists(AutoWindowId))
        {
            HtmlMaskWindow.SetClickThrough(AutoWindowId, _configService.Get().MaskWindowConfig.CustomHtmlMaskClickThrough);
        }
    }

    private const string DefaultHtml = """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <style>
    html, body {
      width: 100%;
      height: 100%;
      margin: 0;
      overflow: hidden;
      background: transparent;
      font-family: "MiSans", "Microsoft YaHei UI", sans-serif;
      color: rgba(255, 255, 255, 0.9);
    }

    .panel {
      position: absolute;
      left: 20px;
      top: 20px;
      padding: 10px 14px;
      border-radius: 8px;
      background: rgba(0, 0, 0, 0.35);
      border: 1px solid rgba(255, 255, 255, 0.18);
      text-shadow: 0 1px 8px rgba(0, 0, 0, 0.7);
    }
  </style>
</head>
<body>
  <div class="panel">BetterGI Custom HTML Mask</div>
</body>
</html>
""";
}
