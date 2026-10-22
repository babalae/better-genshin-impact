using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using Microsoft.ClearScript;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    /// <summary>
    /// 自动钓鱼任务参数
    /// </summary>
    public class AutoFishingTaskParam : BaseTaskParam<AutoFishingTask>
    {
        /// <summary>
        /// 配置根。参数类由脚本和界面直接构建，没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
        /// </summary>
        private static AllConfig Config => ConfigService.Config!;

        /// <summary>
        /// 使用默认值创建参数（与 <see cref="AutoFishingConfig"/> 的默认值一致），供 JSON 反序列化和默认参数使用
        /// </summary>
        public AutoFishingTaskParam() : base(null, null)
        {
        }

        /// <summary>
        /// 使用指定值创建参数
        /// </summary>
        /// <param name="wholeProcessTimeoutSeconds">整个任务超时时间（秒）</param>
        /// <param name="throwRodTimeOutTimeoutSeconds">抛竿后未上钩的超时时间（秒）</param>
        /// <param name="fishingTimePolicy">昼夜策略</param>
        /// <param name="saveScreenshotOnKeyTick">是否保存关键帧截图</param>
        /// <param name="cultureInfo">游戏语言，为 null 时读取配置</param>
        /// <param name="stringLocalizer">多语言资源，为 null 时从服务容器获取</param>
        public AutoFishingTaskParam(int wholeProcessTimeoutSeconds, int throwRodTimeOutTimeoutSeconds, FishingTimePolicy fishingTimePolicy, bool saveScreenshotOnKeyTick, CultureInfo? cultureInfo, IStringLocalizer<AutoFishingTask>? stringLocalizer) : base(cultureInfo, stringLocalizer)
        {
            WholeProcessTimeoutSeconds = wholeProcessTimeoutSeconds;
            ThrowRodTimeOutTimeoutSeconds = throwRodTimeOutTimeoutSeconds;
            FishingTimePolicy = fishingTimePolicy;
            SaveScreenshotOnKeyTick = saveScreenshotOnKeyTick;
        }

        /// <summary>
        /// 整个任务超时时间（秒），默认 300
        /// </summary>
        public int WholeProcessTimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// 抛竿后未上钩的超时时间（秒），默认 15
        /// </summary>
        public int ThrowRodTimeOutTimeoutSeconds { get; set; } = 15;

        /// <summary>
        /// 昼夜策略：钓全天的鱼、只钓白天或夜晚的鱼、不调整时间，默认全天
        /// </summary>
        public FishingTimePolicy FishingTimePolicy { get; set; } = FishingTimePolicy.All;

        /// <summary>
        /// 是否保存关键帧与出错截图，默认不保存
        /// </summary>
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
