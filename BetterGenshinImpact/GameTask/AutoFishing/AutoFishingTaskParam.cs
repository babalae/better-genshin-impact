using BetterGenshinImpact.GameTask.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTaskParam : BaseTaskParam
    {
        public AutoFishingTaskParam(int wholeProcessTimeoutSeconds, int throwRodTimeOutTimeoutSeconds, FishingTimePolicy fishingTimePolicy, bool saveScreenshotOnKeyTick)
        {
            WholeProcessTimeoutSeconds = wholeProcessTimeoutSeconds;
            ThrowRodTimeOutTimeoutSeconds = throwRodTimeOutTimeoutSeconds;
            FishingTimePolicy = fishingTimePolicy;
            SaveScreenshotOnKeyTick = saveScreenshotOnKeyTick;
        }


        public int WholeProcessTimeoutSeconds { get; set; }
        public int ThrowRodTimeOutTimeoutSeconds { get; set; }
        public FishingTimePolicy FishingTimePolicy { get; set; }
        public bool SaveScreenshotOnKeyTick { get; set; }
        
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

            var configType = config.GetType();
            var wholeProcessTimeoutSeconds = (int?)configType.GetProperty("wholeProcessTimeoutSeconds")?.GetValue(config) ?? autoFishingConfig.WholeProcessTimeoutSeconds;
            var throwRodTimeOutTimeoutSeconds = (int?)configType.GetProperty("throwRodTimeOutTimeoutSeconds")?.GetValue(config) ?? autoFishingConfig.AutoThrowRodTimeOut;
            var fishingTimePolicy = (FishingTimePolicy?)configType.GetProperty("fishingTimePolicy")?.GetValue(config) ?? autoFishingConfig.FishingTimePolicy;
            var saveScreenshotOnKeyTick = (bool?)configType.GetProperty("saveScreenshotOnKeyTick")?.GetValue(config) ?? false;

            return new AutoFishingTaskParam(wholeProcessTimeoutSeconds, throwRodTimeOutTimeoutSeconds, fishingTimePolicy, saveScreenshotOnKeyTick);
        }
        
        /// <summary>
        /// 从配置文件构建任务参数
        /// </summary>
        /// <param name="config"></param>
        /// <param name="saveScreenshotOnKeyTick"></param>
        /// <returns></returns>
        public static AutoFishingTaskParam BuildFromConfig(AutoFishingConfig config, bool saveScreenshotOnKeyTick = false)
        {
            return new AutoFishingTaskParam(config.WholeProcessTimeoutSeconds, config.AutoThrowRodTimeOut, config.FishingTimePolicy, saveScreenshotOnKeyTick);
        }
    }
}
