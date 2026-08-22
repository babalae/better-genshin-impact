using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.View.Drawable;
using CsTrees.Blackboard;
using CsTrees.FluentBuilder;
using Fischless.WindowsInput;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTaskCatalog : IBehaviourCatalog
    {
        public SetSleep SetSleep(
            string name,
            Action<int> sleep,
            Blackboard blackboard) => new SetSleep(name, sleep, blackboard);

        public TakeScreenshot TakeScreenshot(
            string name,
            ILogger logger,
            Blackboard blackboard) => new TakeScreenshot(name, logger, blackboard);

        public MoveViewpointDown MoveViewpointDown(
            string name,
            ILogger logger,
            IInputSimulator input,
            Blackboard blackboard) => new MoveViewpointDown(name, logger, input, blackboard);

        public TurnAround TurnAround(
            string name,
            ILogger logger,
            IInputSimulator input,
            BgiYoloPredictor bgiYoloPredictor,
            Blackboard blackboard) => new TurnAround(name, logger, input, bgiYoloPredictor, blackboard);

        public GetFishpond GetFishpond(
            string name,
            ILogger logger,
            BgiYoloPredictor predictor,
            Blackboard blackboard,
            TimeProvider? timeProvider = null,
            DrawContent? drawContent = null) => new GetFishpond(name, logger, predictor, blackboard, timeProvider, drawContent);

        public FindFishTimeout FindFishTimeout(
            string name,
            int seconds,
            ILogger logger,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new FindFishTimeout(name, seconds, logger, blackboard, timeProvider);

        public EnterFishingMode EnterFishingMode(
            string name,
            ILogger logger,
            IInputSimulator input,
            InferenceSession session,
            Dictionary<string, float[]> prototypes,
            Blackboard blackboard,
            TimeProvider? timeProvider = null,
            CultureInfo? cultureInfo = null,
            IStringLocalizer? stringLocalizer = null) => new EnterFishingMode(name, logger, input, session, prototypes, blackboard, timeProvider, cultureInfo, stringLocalizer);

        public CheckInitalState CheckInitalState(
            string name,
            ILogger logger,
            IInputSimulator input,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new CheckInitalState(name, logger, input, blackboard, timeProvider);

        public ChooseBait ChooseBait(
            string name,
            ILogger logger,
            ISystemInfo systemInfo,
            IInputSimulator input,
            InferenceSession session,
            Dictionary<string, float[]> prototypes,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new ChooseBait(name, logger, systemInfo, input, session, prototypes, blackboard, timeProvider);

        public ThrowRod ThrowRod(
            string name,
            ILogger logger,
            IInputSimulator input,
            BgiYoloPredictor predictor,
            Blackboard blackboard,
            TimeProvider? timeProvider = null,
            DrawContent? drawContent = null) => new ThrowRod(name, logger, input, predictor, blackboard, timeProvider, drawContent);

        public CheckThrowRodResult CheckThrowRodResult(
            string name,
            Blackboard blackboard) => new CheckThrowRodResult(name, blackboard);

        public CheckThrowRod CheckThrowRod(
            string name,
            ILogger logger,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new CheckThrowRod(name, logger, blackboard, timeProvider);

        public FishBite FishBite(
            string name,
            ILogger logger,
            IInputSimulator input,
            IOcrService ocrService,
            Blackboard blackboard,
            DrawContent? drawContent = null,
            CultureInfo? cultureInfo = null,
            IStringLocalizer? stringLocalizer = null) => new FishBite(name, logger, input, ocrService, blackboard, drawContent, cultureInfo, stringLocalizer);

        public FishBiteTimeout FishBiteTimeout(
            string name,
            int seconds,
            ILogger logger,
            IInputSimulator input,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new FishBiteTimeout(name, seconds, logger, input, blackboard, timeProvider);

        public CheckRaiseHook CheckRaiseHook(
            string name,
            ILogger logger,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new CheckRaiseHook(name, logger, blackboard, timeProvider);

        public GetFishBoxArea GetFishBoxArea(
            string name,
            ILogger logger,
            bool saveScreenshotOnError,
            Blackboard blackboard,
            TimeProvider? timeProvider = null) => new GetFishBoxArea(name, logger, saveScreenshotOnError, blackboard, timeProvider);

        public Fishing Fishing(
            string name,
            ILogger logger,
            bool saveScreenshotOnError,
            IInputSimulator input,
            Blackboard blackboard,
            TimeProvider? timeProvider = null,
            DrawContent? drawContent = null) => new Fishing(name, logger, saveScreenshotOnError, input, blackboard, timeProvider, drawContent);

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
            IInputSimulator input,
            Blackboard blackboard,
            CultureInfo? cultureInfo = null,
            IStringLocalizer? stringLocalizer = null) => new QuitFishingMode(name, logger, input, blackboard, cultureInfo, stringLocalizer);
    }
}
