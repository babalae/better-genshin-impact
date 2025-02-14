using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.GameLoading.Assets;
using System;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Genshin.Paths;
using System.IO;

namespace BetterGenshinImpact.GameTask.GameLoading;

public class GameLoadingTrigger : ITaskTrigger
{
    public string Name => "自动开门";

    public bool IsEnabled { get; set; }

    public int Priority => 999;

    public bool IsExclusive => false;

    public bool IsBackgroundRunning => true;

    private readonly GameLoadingAssets _assets;

    private readonly GenshinStartConfig _config = TaskContext.Instance().Config.GenshinStartConfig;

    // private int _enterGameClickCount = 0;
    // private int _welkinMoonClickCount = 0;
    // private int _noneClickCount, _wmNoneClickCount;

    private DateTime _prevExecuteTime = DateTime.MinValue;

    private DateTime _triggerStartTime = DateTime.Now;

    private string GameServer = "";

    private string FileName = "";
    private string iniPath = "";

    public GameLoadingTrigger()
    {
        GameLoadingAssets.DestroyInstance();
        _assets = GameLoadingAssets.Instance;
    }

    public void Init()
    {
        IsEnabled = _config.AutoEnterGameEnabled;
        // // 前面没有联动启动原神，这个任务也不用启动
        // if ((DateTime.Now - TaskContext.Instance().LinkedStartGenshinTime).TotalMinutes >= 5)
        // {
        //     IsEnabled = false;
        // }
        if (_config.RecordGameTimeEnabled)
        {
            Debug.WriteLine("[GameLoading] 使用Starward记录时间");
            // 直接从RegistryGameLocator获取到服务器
            if (!string.IsNullOrEmpty(RegistryGameLocator.GameServer)) {
                GameServer = RegistryGameLocator.GameServer;
            }


            FileName = Path.GetFileName(_config.InstallPath);
            if (FileName == "GenshinImpact.exe") {
                GameServer = "hk4e_global";
            }
            if (FileName == "YuanShen.exe")
            {

                iniPath = Path.GetDirectoryName(_config.InstallPath) + "//config.ini";
                // 读取 ini 文件内容
                string[] lines = File.ReadAllLines(iniPath);

                bool inGeneralSection = false;
                string channelValue = null;

                foreach (string line in lines)
                {
                    // 去除行首和行尾的空白字符
                    string trimmedLine = line.Trim();



                    // 检查是否进入 [General] 节
                    if (trimmedLine.Equals("[General]", StringComparison.OrdinalIgnoreCase))
                    {
                        inGeneralSection = true;
                        continue;
                    }

                    // 检查是否离开 [General] 节
                    if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                    {
                        inGeneralSection = false;
                        continue;
                    }

                    // 如果在 [General] 节内，检查 channel 键
                    if (inGeneralSection && trimmedLine.Contains("="))
                    {
                        string[] parts = trimmedLine.Split(new[] { '=' }, 2);
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        if (key.Equals("channel", StringComparison.OrdinalIgnoreCase))
                        {
                            channelValue = value;
                            break;
                        }
                    }
                }
                // channelValue = 1 ： 官服
                // channelValue = 14 ： B服
                if (channelValue == "1")
                {
                    GameServer = "hk4e_cn";
                }
                if (channelValue == "14")
                {
                    GameServer = "hk4e_bilibili";
                }

                try
                {
                    Process.Start(new ProcessStartInfo($"starward://playtime/{GameServer}") { UseShellExecute = true });
                }
                catch (Exception ex) { 
                    
                    
                    Debug.WriteLine("[GameLoading] Starward记录时间失败");
                }


            }
            else { Debug.WriteLine("[GameLoading] 不使用Starward记录时间"); }
        } }

    public void OnCapture(CaptureContent content)
    {
        // 2s 一次
        if ((DateTime.Now - _prevExecuteTime).TotalMilliseconds <= 2000)
        {
            return;
        }
        _prevExecuteTime = DateTime.Now;
        // 5min 后自动停止
        if ((DateTime.Now - _triggerStartTime).TotalMinutes >= 5)
        {
            IsEnabled = false;
            return;
        }

        if (Bv.IsInMainUi(content.CaptureRectArea) || Bv.IsInAnyClosableUi(content.CaptureRectArea))
        {
            IsEnabled = false;
            return;
        }

        using var ra = content.CaptureRectArea.Find(_assets.EnterGameRo);
        if (!ra.IsEmpty())
        {
            // 随便找个相对点击的位置
            TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
            // TaskControl.Logger.LogInformation("自动开门");
            return;
        }

        var wmRa = content.CaptureRectArea.Find(_assets.WelkinMoonRo);
        if (!wmRa.IsEmpty())
        {
            TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
            Debug.WriteLine("[GameLoading] Click blessing of the welkin moon");
            // TaskControl.Logger.LogInformation("自动点击月卡");
            return;
        }

        // 原石
        var ysRa = content.CaptureRectArea.Find(ElementAssets.Instance.PrimogemRo);
        if (!ysRa.IsEmpty())
        {
            GameCaptureRegion.GameRegion1080PPosMove(10, 10);
            TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
            Debug.WriteLine("[GameLoading] 跳过原石");
            return;
        }
    }
};