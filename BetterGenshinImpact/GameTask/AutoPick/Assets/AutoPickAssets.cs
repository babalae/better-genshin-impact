using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System.Drawing;
using Vanara.PInvoke;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPick.Assets;

public class AutoPickAssets : BaseAssets<AutoPickAssets>
{
    private readonly ILogger<AutoPickAssets> _logger = App.GetLogger<AutoPickAssets>();

    public RecognitionObject FRo;
    public RecognitionObject ChatIconRo;
    public RecognitionObject SettingsIconRo;
    public RecognitionObject LRo;


    public User32.VK PickVk = User32.VK.VK_F;
    public bool UseControllerY;
    public RecognitionObject PickRo;
    public RecognitionObject ChatPickRo;
    public IReadOnlyList<RecognitionObject> ControllerIconBlacklistRos = [];

    private AutoPickAssets()
    {
        FRo = new RecognitionObject
        {
            Name = "F",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoPick", "F.png"),
            RegionOfInterest = new Rect((int)(1090 * AssetScale),
                (int)(330 * AssetScale),
                (int)(60 * AssetScale),
                (int)(420 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        ChatIconRo = new RecognitionObject
        {
            Name = "ChatIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoSkip", "icon_option.png"),
            DrawOnWindow = false,
            DrawOnWindowPen = new Pen(Color.Chocolate, 2)
        }.InitTemplate();
        SettingsIconRo = new RecognitionObject
        {
            Name = "SettingsIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoPick", "icon_settings.png"),
            DrawOnWindow = false,
            DrawOnWindowPen = new Pen(Color.Chocolate, 2)
        }.InitTemplate();
        
        LRo = new RecognitionObject
        {
            Name = "L",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoPick", "L.png"),
            RegionOfInterest = new Rect(CaptureRect.Width-(int)(110 * AssetScale),
                (int)(550 * AssetScale),
                (int)(70 * AssetScale),
                (int)(100 * AssetScale)),
        }.InitTemplate();


        PickRo = FRo;
        var keyName = TaskContext.Instance().Config.AutoPickConfig.PickKey;
        if (!string.IsNullOrEmpty(keyName))
        {
            try
            {
                PickRo = LoadCustomPickKey(keyName);
                ChatPickRo = LoadCustomChatPickKey(keyName);
                if (IsControllerYPromptKey(keyName))
                {
                    UseControllerY = true;
                    PickVk = User32.VK.VK_F;
                    TaskContext.Instance().Config.KeyBindingsConfig.PickUpOrInteract = Core.Config.KeyId.F;
                    ControllerIconBlacklistRos = LoadControllerIconBlacklistTemplates(systemInfo);
                    VirtualXbox360Controller.EnsureConnected(_logger);
                }
                else
                {
                    UseControllerY = false;
                    PickVk = User32Helper.ToVk(keyName);
                    TaskContext.Instance().Config.KeyBindingsConfig.PickUpOrInteract = (Core.Config.KeyId)(int)PickVk;
                }
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
                if (IsControllerYPromptKey(keyName))
                {
                    _logger.LogInformation("自定义拾取提示：手柄Y（YY模板，交互发送虚拟手柄Y键）");
                }
                else
                {
                    _logger.LogInformation("自定义拾取按键：{Key}", keyName);
                }
            }
        }
    }

    public static bool IsControllerYPromptKey(string key)
    {
        return string.Equals(key, "YY", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<RecognitionObject> LoadControllerIconBlacklistTemplates(ISystemInfo systemInfo)
    {
        var assetsFolder = Global.Absolute($@"GameTask\AutoPick\Assets\{systemInfo.GameScreenSize.Width}x{systemInfo.GameScreenSize.Height}\controller_icon_blacklist");
        if (!Directory.Exists(assetsFolder))
        {
            assetsFolder = Global.Absolute(@"GameTask\AutoPick\Assets\1920x1080\controller_icon_blacklist");
        }

        return LoadControllerIconBlacklistTemplates(assetsFolder, systemInfo.GameScreenSize.Width != 1920 ? systemInfo.AssetScale : 1d);
    }

    internal static IReadOnlyList<RecognitionObject> LoadControllerIconBlacklistTemplates(string directoryPath, double assetScale = 1d)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var templates = new List<RecognitionObject>();
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.png", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var mat = Mat.FromStream(stream, ImreadModes.Color);
                if (mat.Empty())
                {
                    mat.Dispose();
                    continue;
                }

                if (Math.Abs(assetScale - 1d) > 0.001)
                {
                    var resized = ResizeHelper.Resize(mat, assetScale);
                    mat.Dispose();
                    mat = resized;
                }

                templates.Add(CreateControllerIconBlacklistTemplate(Path.GetFileNameWithoutExtension(filePath), mat));
            }
            catch (Exception)
            {
                // Bad user-supplied templates should not break auto-pick startup.
            }
        }

        return templates;
    }

    internal static RecognitionObject CreateControllerIconBlacklistTemplate(string name, Mat templateMat)
    {
        return new RecognitionObject
        {
            Name = $"ControllerIconBlacklist:{name}",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = templateMat,
            Threshold = 0.9,
            DrawOnWindow = false
        }.InitTemplate();
    }

    public void PressPickKey()
    {
        if (UseControllerY)
        {
            VirtualXbox360Controller.PressY(_logger);
            return;
        }

        Simulation.SendInput.Keyboard.KeyPress(PickVk);
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
