using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using Microsoft.ClearScript;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using TorchSharp;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTaskParam : BaseTaskParam<AutoFishingTask>
    {
        public AutoFishingTaskParam(int wholeProcessTimeoutSeconds, int throwRodTimeOutTimeoutSeconds, FishingTimePolicy fishingTimePolicy, bool saveScreenshotOnKeyTick, bool useTorch, CultureInfo? cultureInfo, IStringLocalizer<AutoFishingTask>? stringLocalizer) : base(cultureInfo, stringLocalizer)
        {
            WholeProcessTimeoutSeconds = wholeProcessTimeoutSeconds;
            ThrowRodTimeOutTimeoutSeconds = throwRodTimeOutTimeoutSeconds;
            FishingTimePolicy = fishingTimePolicy;
            SaveScreenshotOnKeyTick = saveScreenshotOnKeyTick;
            UseTorch = useTorch;
        }


        public int WholeProcessTimeoutSeconds { get; set; }
        public int ThrowRodTimeOutTimeoutSeconds { get; set; }
        public FishingTimePolicy FishingTimePolicy { get; set; }
        public bool SaveScreenshotOnKeyTick { get; set; }
        public bool UseTorch { get; set; }

        /// <summary>
        /// 从JS请求参数构建任务参数
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static AutoFishingTaskParam BuildFromSoloTaskConfig(object? config)
        {
            if (config == null)
            {
                return BuildFromConfig(TaskContext.Instance().Config.AutoFishingConfig);
            }

            var autoFishingConfig = TaskContext.Instance().Config.AutoFishingConfig;

            var jsObject = (ScriptObject)config;
            var wholeProcessTimeoutSeconds = ScriptObjectConverter.GetValue(jsObject, "wholeProcessTimeoutSeconds", autoFishingConfig.WholeProcessTimeoutSeconds);
            var throwRodTimeOutTimeoutSeconds = ScriptObjectConverter.GetValue(jsObject, "throwRodTimeOutTimeoutSeconds", autoFishingConfig.AutoThrowRodTimeOut);
            var fishingTimePolicy = (FishingTimePolicy)ScriptObjectConverter.GetValue(jsObject, "fishingTimePolicy", (int)autoFishingConfig.FishingTimePolicy);
            var saveScreenshotOnKeyTick = ScriptObjectConverter.GetValue(jsObject, "saveScreenshotOnKeyTick", false);

            bool useTorch;
            try
            {
                NativeLibrary.Load(autoFishingConfig.TorchDllFullPath);
                if (torch.TryInitializeDeviceType(DeviceType.CUDA))
                {
                    torch.set_default_device(new torch.Device(DeviceType.CUDA));
                }
                useTorch = true;
            }
            catch (Exception e) when (e is DllNotFoundException || e is NotSupportedException)
            {
                useTorch = false;
            }

            return new AutoFishingTaskParam(wholeProcessTimeoutSeconds, throwRodTimeOutTimeoutSeconds, fishingTimePolicy, saveScreenshotOnKeyTick, useTorch, null, null);
        }

        /// <summary>
        /// 从配置文件构建任务参数
        /// </summary>
        /// <param name="config"></param>
        /// <param name="saveScreenshotOnKeyTick"></param>
        /// <returns></returns>
        public static AutoFishingTaskParam BuildFromConfig(AutoFishingConfig config, bool saveScreenshotOnKeyTick = false)
        {
            CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
            bool useTorch;
            try
            {
                NativeLibrary.Load(config.TorchDllFullPath);
                if (torch.TryInitializeDeviceType(DeviceType.CUDA))
                {
                    torch.set_default_device(new torch.Device(DeviceType.CUDA));
                }
                useTorch = true;
            }
            catch (Exception e) when (e is DllNotFoundException || e is NotSupportedException)
            {
                useTorch = false;
            }
            return new AutoFishingTaskParam(config.WholeProcessTimeoutSeconds, config.AutoThrowRodTimeOut, config.FishingTimePolicy, saveScreenshotOnKeyTick, useTorch, cultureInfo, null);
        }
    }
}