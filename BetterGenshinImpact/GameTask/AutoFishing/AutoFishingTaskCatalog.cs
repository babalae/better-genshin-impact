using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using CsTrees.Blackboard;
using CsTrees.FluentBuilder;
using BetterGenshinImpact.Core.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTaskCatalog : IBehaviourCatalog
    {
        public SetSleep SetSleep(
            string name,
            Action<int> sleep,
            Blackboard blackboard) => new SetSleep(name, sleep, blackboard);

        /// <summary>
        /// 从指定运行环境截图并写入黑板。运行环境随节点传入，目录与构建器本身不持有运行环境
        /// </summary>
        public TakeScreenshot TakeScreenshot(
            string name,
            GameRuntime runtime,
            ILogger logger,
            Blackboard blackboard) => new TakeScreenshot(name, runtime, logger, blackboard);

        public MoveViewpointDown MoveViewpointDown(
            string name,
            ILogger logger,
            IInputChannel input,
            Blackboard blackboard) => new MoveViewpointDown(name, logger, input, blackboard);

        public TurnAround TurnAround(
            string name,
            ILogger logger,
            IInputChannel input,
            BgiYoloPredictor bgiYoloPredictor,
            Blackboard blackboard) => new TurnAround(name, logger, input, bgiYoloPredictor, blackboard);

        public GetFishpond GetFishpond(
            string name,
            ILogger logger,
            BgiYoloPredictor predictor,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new GetFishpond(name, logger, predictor, blackboard, timeProvider);

        public FindFishTimeout FindFishTimeout(
            string name,
            int seconds,
            ILogger logger,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new FindFishTimeout(name, seconds, logger, blackboard, timeProvider);

        public EnterFishingMode EnterFishingMode(
            string name,
            ILogger logger,
            IInputChannel input,
            IItemIconRecognizer itemRecognizer,
            Blackboard blackboard,
            TimeProvider? timeProvider = null,
            CultureInfo? cultureInfo = null,
            IStringLocalizer? stringLocalizer = null) => new EnterFishingMode(name, logger, input, itemRecognizer, blackboard, timeProvider, cultureInfo, stringLocalizer);

        public CheckInitalState CheckInitalState(
            string name,
            ILogger logger,
            IInputChannel input,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new CheckInitalState(name, logger, input, blackboard, timeProvider);

        public ChooseBait ChooseBait(
            string name,
            ILogger logger,
            ISystemInfo systemInfo,
            IInputChannel input,
            IItemIconRecognizer itemRecognizer,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new ChooseBait(name, logger, systemInfo, input, itemRecognizer, blackboard, timeProvider);

        public LiftRod LiftRod(
            string name,
            ILogger logger,
            IInputChannel input,
            BgiYoloPredictor predictor,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new LiftRod(name, logger, input, predictor, blackboard, timeProvider);

        public Cast Cast(
            string name,
            ILogger logger,
            IInputChannel input,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new Cast(name, logger, input, blackboard, timeProvider);

        public CheckThrowRodResult CheckThrowRodResult(
            string name,
            Blackboard blackboard) => new CheckThrowRodResult(name, blackboard);

        public CheckThrowRod CheckThrowRod(
            string name,
            ILogger logger,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new CheckThrowRod(name, logger, blackboard, timeProvider);

        public CheckFishBite CheckFishBite(
            string name,
            ILogger logger,
            IOcrService ocrService,
            Blackboard blackboard,
            CultureInfo? cultureInfo = null,
            IStringLocalizer? stringLocalizer = null) => new CheckFishBite(name, logger, ocrService, blackboard, cultureInfo, stringLocalizer);

        public RaiseHook RaiseHook(
            string name,
            ILogger logger,
            IInputChannel input,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new RaiseHook(name, logger, input, blackboard, timeProvider);

        public FishBiteTimeout FishBiteTimeout(
            string name,
            int seconds,
            ILogger logger,
            IInputChannel input,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new FishBiteTimeout(name, seconds, logger, input, blackboard, timeProvider);

        public CheckRaiseHook CheckRaiseHook(
            string name,
            ILogger logger,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new CheckRaiseHook(name, logger, blackboard, timeProvider);

        /// <summary>
        /// 等待拉条出现。开启出错截图时由 <paramref name="screenshots"/> 保存，未提供服务则不保存
        /// </summary>
        public GetFishBoxArea GetFishBoxArea(
            string name,
            ILogger logger,
            bool saveScreenshotOnError,
            Blackboard blackboard,
            TimeProvider? timeProvider = null,
            GameScreenshotService? screenshots = null) => new GetFishBoxArea(name, logger, saveScreenshotOnError, blackboard, timeProvider, screenshots);

        /// <summary>
        /// 拉条。开启出错截图时由 <paramref name="screenshots"/> 保存，未提供服务则不保存
        /// </summary>
        public Fishing Fishing(
            string name,
            ILogger logger,
            bool saveScreenshotOnError,
            IInputChannel input,
            Blackboard blackboard,
            TimeProvider? timeProvider = null,
            GameScreenshotService? screenshots = null) => new Fishing(name, logger, saveScreenshotOnError, input, blackboard, timeProvider, screenshots);

        public BubbleAbortCheck BubbleAbortCheck(
            string name,
            Blackboard blackboard) => new BubbleAbortCheck(name, blackboard);

        public WholeProcessTimeout WholeProcessTimeout(
            string name,
            ILogger logger,
            int seconds,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new WholeProcessTimeout(name, logger, seconds, blackboard, timeProvider);

        public QuitFishingMode QuitFishingMode(
            string name,
            ILogger logger,
            IInputChannel input,
            Blackboard blackboard) => new QuitFishingMode(name, logger, input, blackboard);
    }
}
