using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using Microsoft.ClearScript;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTaskParam : BaseTaskParam<AutoFishingTask>
    {
        /// <summary>
        /// 配置根。参数类由脚本和界面直接构建，没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
        /// </summary>
        private static AllConfig Config => ConfigService.Config!;

        public AutoFishingTaskParam(int wholeProcessTimeoutSeconds, int throwRodTimeOutTimeoutSeconds, FishingTimePolicy fishingTimePolicy, bool saveScreenshotOnKeyTick, CultureInfo? cultureInfo, IStringLocalizer<AutoFishingTask>? stringLocalizer) : base(cultureInfo, stringLocalizer)
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
                return BuildFromConfig(Config.AutoFishingConfig);
            }

            var autoFishingConfig = Config.AutoFishingConfig;

            var jsObject = (ScriptObject)config;
            var wholeProcessTimeoutSeconds = ScriptObjectConverter.GetValue(jsObject, "wholeProcessTimeoutSeconds", autoFishingConfig.WholeProcessTimeoutSeconds);
            var throwRodTimeOutTimeoutSeconds = ScriptObjectConverter.GetValue(jsObject, "throwRodTimeOutTimeoutSeconds", autoFishingConfig.AutoThrowRodTimeOut);
            var fishingTimePolicy = (FishingTimePolicy)ScriptObjectConverter.GetValue(jsObject, "fishingTimePolicy", (int)autoFishingConfig.FishingTimePolicy);
            var saveScreenshotOnKeyTick = ScriptObjectConverter.GetValue(jsObject, "saveScreenshotOnKeyTick", false);

            return new AutoFishingTaskParam(wholeProcessTimeoutSeconds, throwRodTimeOutTimeoutSeconds, fishingTimePolicy, saveScreenshotOnKeyTick, null, null);
        }

        /// <summary>
        /// 从配置文件构建任务参数
        /// </summary>
        /// <param name="config"></param>
        /// <param name="saveScreenshotOnKeyTick"></param>
        /// <returns></returns>
        public static AutoFishingTaskParam BuildFromConfig(AutoFishingConfig config, bool saveScreenshotOnKeyTick = false)
        {
            CultureInfo cultureInfo = new CultureInfo(Config.OtherConfig.GameCultureInfoName);
            return new AutoFishingTaskParam(config.WholeProcessTimeoutSeconds, config.AutoThrowRodTimeOut, config.FishingTimePolicy, saveScreenshotOnKeyTick, cultureInfo, null);
        }
    }
}
