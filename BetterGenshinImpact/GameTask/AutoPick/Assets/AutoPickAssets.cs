using System;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using OpenCvSharp;
using Vanara.PInvoke;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPick.Assets;

public class AutoPickAssets : Singleton<AutoPickAssets>
{
    private readonly ILogger<AutoPickAssets> _logger = App.GetLogger<AutoPickAssets>();
    private readonly ISystemInfo systemInfo;

    public User32.VK PickVk = User32.VK.VK_F;
    public RecognitionObject PickRo;
    public RecognitionObject ChatPickRo;

    private Rect CaptureRect => systemInfo.ScaleMax1080PCaptureRect;
    private double AssetScale => systemInfo.AssetScale;

    private AutoPickAssets()
    {
        systemInfo = TaskContext.Instance().SystemInfo;
        PickRo = RecognitionAssets.Get("AutoPick", "F", CaptureRect.Width, CaptureRect.Height);
        ChatPickRo = LoadCustomChatPickKey("F");
        var keyName = TaskContext.Instance().Config.AutoPickConfig.PickKey;
        if (!string.IsNullOrEmpty(keyName))
        {
            try
            {
                PickRo = LoadCustomPickKey(keyName);
                PickVk = User32Helper.ToVk(keyName);
                TaskContext.Instance().Config.KeyBindingsConfig.PickUpOrInteract = (Core.Config.KeyId)(int)PickVk;
                ChatPickRo = LoadCustomChatPickKey(keyName);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "加载自定义拾取按键时发生异常");
                _logger.LogError("加载自定义拾取按键失败，继续使用默认的F键");
                TaskContext.Instance().Config.AutoPickConfig.PickKey = "F";
                return;
            }

            if (keyName != "F")
            {
                _logger.LogInformation("自定义拾取按键：{Key}", keyName);
            }
        }
    }

    public RecognitionObject LoadCustomPickKey(string key)
    {
        return new RecognitionObject
        {
            Name = key,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoPick", key + ".png"),
            RegionOfInterest = new Rect((int)(1090 * AssetScale),
                (int)(330 * AssetScale),
                (int)(60 * AssetScale),
                (int)(420 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
    }

    public RecognitionObject LoadCustomChatPickKey(string key)
    {
        return new RecognitionObject
        {
            Name = "chatPick" + key,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoPick", key + ".png"),
            RegionOfInterest = new Rect((int)(1200 * AssetScale),
                (int)(350 * AssetScale),
                (int)(50 * AssetScale),
                CaptureRect.Height - (int)(220 * AssetScale) - (int)(350 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
    }

}
