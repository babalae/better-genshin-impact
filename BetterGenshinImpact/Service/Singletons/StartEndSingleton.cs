using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Video;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.Genshin.Settings2;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Device;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Singletons;

public class StartEndSingleton : Singleton<StartEndSingleton>
{
    private Resolution? _resolution;

    public void OnStartup()
    {
    }

    public void OnMainWindowLoad()
    {
        TouchpadSoft.Instance.CheckAndRecordStatus();
        TouchpadSoft.Instance.DisableTouchpadWhenEnabledByHotKey();

        if (TaskContext.Instance().Config.CommonConfig.ChangeResolutionOnStart && !RuntimeHelper.IsDebug)
        {
            // 设置DPI
            SysDpi.Instance.SetDpi();

            Thread.Sleep(2000);

            ChangeResolution();
        }

        // 获取PC信息
        Task.Run(() =>
        {
            try
            {
                var json = GetPCInfo.GetJson();
                // 保存
                File.WriteAllText(Global.Absolute(@$"User/pc.json"), json);
            }
            catch (Exception e)
            {
                TaskControl.Logger.LogDebug("获取PC信息失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
            }
        });
        
        ObsRecorder.StartObs();

        var res = GameSettingsChecker.LoadGameSettingsAndCheck();
        // if (res != null && res.Value == false)
        // {
        //     // 退出
        //     Environment.Exit(0);
        // }
        

    }


    public void OnExit()
    {
        TouchpadSoft.Instance.RestoreTouchpadByHotKey();

        if (TaskContext.Instance().Config.CommonConfig.RestoreResolutionOnExit && !RuntimeHelper.IsDebug)
        {
            ResetResolution();
            Thread.Sleep(2000);
            SysDpi.Instance.ResetDpi();
        }

        try
        {
            Process.GetProcessesByName("obs64").ToList().ForEach(p => p.Kill());
            Process.GetProcessesByName("ffmpeg").ToList().ForEach(p => p.Kill());
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }


    public void ChangeResolution()
    {
        _resolution = new Resolution();
        _resolution.ChangeResolution(1920, 1080);
    }

    public void ResetResolution()
    {
        _resolution?.ChangeResolution(_resolution.autoWidth, _resolution.autoHeight);
    }
}