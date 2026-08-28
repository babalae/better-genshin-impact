using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Drawable;
using Compunet.YoloSharp;
using CsTrees;
using CsTrees.Blackboard;
using Fischless.WindowsInput;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Vanara.PInvoke.User32;
using Color = System.Drawing.Color;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    /// <summary>
    /// 检测鱼群
    /// </summary>
    public partial class GetFishpond : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? detectInterval;
        private readonly DrawContent drawContent;
        private readonly BgiYoloPredictor _predictor;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        /// <summary>
        /// 鱼塘
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<Fishpond> Fishpond { get; private set; } = null!;

        /// <summary>
        /// 选鱼饵失败列表
        /// 失败一次就加入一次鱼饵类型，列表中同名鱼饵的数量代表该种失败了几次
        /// </summary>
        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<List<BaitType>> ChooseBaitFailures { get; private set; } = null!;

        /// <summary>
        /// 抛竿无目标鱼失败列表
        /// 失败一次就加入一次鱼饵类型，列表中同名鱼饵的数量代表该种失败了几次
        /// </summary>
        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<List<BaitType>> ThrowRodNoBaitFishFailures { get; private set; } = null!;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<Action<int>> Sleep { get; private set; } = null!;

        private GetFishpond(string name, ILogger logger, BgiYoloPredictor predictor, TimeProvider? timeProvider = null, DrawContent? drawContent = null) : base(name)
        {
            this.logger = logger;
            this._predictor = predictor;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
        }

        protected override void Initialize()
        {
            logger.LogInformation("开始寻找鱼塘");
        }

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();
            if (detectInterval != null && timeProvider.GetLocalNow() < detectInterval)
            {
                return Status.Running;
            }
            else
            {
                detectInterval = timeProvider.GetLocalNow().AddSeconds(0.5);
            }
            var result = _predictor.Predictor.Detect(imageRegion.CacheImage);
            Debug.WriteLine($"YOLO识别: {result.Speed}");
            var fishpond = new Fishpond(result, ignoreObtained: true);
            if (fishpond.FishpondRect == default)
            {
                return Status.Running;
            }
            else
            {
                Fishpond.Set(fishpond);

                BaitType[] chooseBaitfailuresIgnoredBaits = ChooseBaitFailures.Exists() ? ChooseBaitFailures.Get().GroupBy(f => f).Where(g => g.Count() >= ChooseBait.MAX_FAILED_TIMES).Select(g => g.Key).ToArray() : [];
                BaitType[] throwRodNoTargetFishfailuresIgnoredBaits = ThrowRodNoBaitFishFailures.Exists() ? ThrowRodNoBaitFishFailures.Get().GroupBy(f => f).Where(g => g.Count() >= ThrowRod.MAX_NO_BAIT_FISH_TIMES).Select(g => g.Key).ToArray() : [];

                logger.LogInformation("定位到鱼塘：" + string.Join('、', fishpond.Fishes.GroupBy(f => f.FishType)
                    .Select(g => $"{g.Key.ChineseName}{g.Count()}条" + ((chooseBaitfailuresIgnoredBaits.Contains(g.Key.BaitType) || throwRodNoTargetFishfailuresIgnoredBaits.Contains(g.Key.BaitType)) ? "（忽略）" : ""))
                    ));
                int i = 0;
                foreach (var fish in fishpond.Fishes)
                {
                    imageRegion.Derive(fish.Rect).DrawSelf($"{fish.FishType.ChineseName}.{i++}");
                }
                Sleep.Get()(1000);
                drawContent.ClearAll();
                if (Fishpond.Get().Fishes.Any(f =>
                    !chooseBaitfailuresIgnoredBaits.Contains(f.FishType.BaitType)
                    && !throwRodNoTargetFishfailuresIgnoredBaits.Contains(f.FishType.BaitType)))
                {
                    return Status.Success;
                }
                else
                {
                    return Status.Running;
                }
            }
        }
    }

    /// <summary>
    /// 选择鱼饵
    /// </summary>
    public partial class ChooseBait : Behaviour, IScreenshotBehaviour
    {
        private readonly ISystemInfo systemInfo;
        private readonly IInputSimulator input;
        private readonly InferenceSession session;
        private readonly Dictionary<string, float[]> prototypes;
        private readonly ILogger logger;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? chooseBaitUIOpenWaitEndTime; // 等待选鱼饵界面出现并尝试找鱼饵的结束时间
        public const int MAX_FAILED_TIMES = 2;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        /// <summary>
        /// 鱼塘
        /// </summary>
        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<Fishpond> Fishpond { get; private set; } = null!;

        /// <summary>
        /// 已选择的鱼饵类型
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<BaitType?> SelectedBait { get; private set; } = null!;

        /// <summary>
        /// 选鱼饵失败列表
        /// 失败一次就加入一次鱼饵类型，列表中同名鱼饵的数量代表该种失败了几次
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<List<BaitType>> ChooseBaitFailures { get; private set; } = null!;

        /// <summary>
        /// 是否正在选鱼饵界面
        /// 此时有阴影遮罩，OpenCv的图像匹配会受干扰
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> ChooseBaitUIOpening { get; private set; } = null!;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<Action<int>> Sleep { get; private set; } = null!;

        private ChooseBait(string name, ILogger logger, ISystemInfo systemInfo, IInputSimulator input, InferenceSession session, Dictionary<string, float[]> prototypes, TimeProvider? timeProvider = null) : base(name)
        {
            this.logger = logger;
            this.systemInfo = systemInfo;
            this.input = input;
            this.session = session;
            this.prototypes = prototypes;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();
            Action<int> sleep = Sleep.Get();

            if (this.Status == Status.Invalid)
            {
                if (SelectedBait.TryGet(out var existingSelectedBait) && Fishpond.Get().Fishes.Any(f => f.FishType.BaitType == existingSelectedBait))    // 如果该种鱼没钓完就不用换饵
                {
                    return Status.Success;
                }
                chooseBaitUIOpenWaitEndTime = timeProvider.GetLocalNow().AddSeconds(3);
                logger.LogInformation("打开换饵界面");
                ChooseBaitUIOpening.Set(true);
                input.Mouse.RightButtonClick();
                sleep(100);
                input.Mouse.MoveMouseBy(0, 200); // 鼠标移走，防止干扰
                sleep(500);
                return Status.Running;
            }

            var fishpond = Fishpond.Get();
            var chooseBaitFailures = ChooseBaitFailures.Exists() ? ChooseBaitFailures.Get() : [];
            var selectedBait = fishpond.Fishes.GroupBy(f => f.FishType.BaitType)
                .Where(b => !chooseBaitFailures.GroupBy(f => f).Where(g => g.Count() >= MAX_FAILED_TIMES).Any(g => g.Key == b.Key))  // 不能是已经失败两次的饵
                .OrderByDescending(g => g.Count()).First().Key; // 选择最多鱼吃的饵料
            SelectedBait.Set(selectedBait);
            logger.LogInformation("选择鱼饵 {Text}", selectedBait.GetDescription());

            // 寻找鱼饵
            var boxAndBaits = FindBait(imageRegion);

            foreach ((Rect box, string? predName) in boxAndBaits)
            {
                if (predName == selectedBait.GetDescription())
                {
                    using ImageRegion resRa = imageRegion.DeriveCrop(box);
                    resRa.Click();
                    sleep(700);
                    // 可能重复点击，所以固定界面点击下
                    imageRegion.ClickTo((int)(imageRegion.Width * 0.675), (int)(imageRegion.Height / 3d));
                    sleep(200);
                    // 点击确定
                    using var ra = imageRegion.Find(new RecognitionObject
                    {
                        Name = "BtnWhiteConfirm",
                        RecognitionType = RecognitionTypes.TemplateMatch,
                        TemplateImageMat = GameTaskManager.LoadAssetImage(@"Common\Element", "btn_white_confirm.png", systemInfo),
                        Use3Channels = true
                    }.InitTemplate());
                    if (ra.IsExist())
                    {
                        ra.Click();
                    }
                    ChooseBaitUIOpening.Set(false);
                    logger.LogInformation("退出换饵界面");
                    sleep(500); // 等待界面切换

                    return Status.Success;
                }
            }

            if (timeProvider.GetLocalNow() >= chooseBaitUIOpenWaitEndTime)
            {
                logger.LogWarning("没有找到目标鱼饵");
                input.Keyboard.KeyPress(VK.VK_ESCAPE);
                ChooseBaitUIOpening.Set(false);
                logger.LogInformation("退出换饵界面");

                if (ChooseBaitFailures.Exists())
                {
                    ChooseBaitFailures.Get().Add(selectedBait);
                }
                else
                {
                    ChooseBaitFailures.Set([selectedBait]);
                }
                if (ChooseBaitFailures.Get().Count(f => f == selectedBait) >= MAX_FAILED_TIMES)
                {
                    logger.LogWarning($"本次将忽略{selectedBait.GetDescription()}");
                }

                SelectedBait.Set(null);

                return Status.Failure;
            }
            else
            {
                sleep(200);
                return Status.Running;
            }
        }

        public IEnumerable<(Rect, string?)> FindBait(ImageRegion imageRegion1080p)
        {
            using ImageRegion singleRowGrid = imageRegion1080p.DeriveCrop(0.28 * imageRegion1080p.Width, 0.37 * imageRegion1080p.Height, 0.45 * imageRegion1080p.Width, 0.22 * imageRegion1080p.Height);
            using Mat grey = singleRowGrid.SrcMat.CvtColor(ColorConversionCodes.BGR2GRAY);
            using Mat canny = grey.Canny(20, 40);

            Cv2.FindContours(canny, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple, null);
            contours = contours
                .Where(c =>
                {
                    Rect r = Cv2.BoundingRect(c);
                    if (r.Width < 0.065 * imageRegion1080p.Width * 0.80)   // 剔除太小的
                    {
                        return false;
                    }
                    if (r.Height == 0)
                    {
                        return false;
                    }
                    return Math.Abs((float)r.Width / r.Height - 0.81) < 0.05; // 按形状筛选
                }).ToArray();
            IEnumerable<Rect> boxes = contours.Select(Cv2.BoundingRect);
            foreach (Rect box in boxes)
            {
                using ImageRegion resRa = singleRowGrid.DeriveCrop(box);
                using Mat img125 = resRa.SrcMat.GetGridIcon();
                (string? predName, _) = GridIconsAccuracyTestTask.Infer(img125, this.session, this.prototypes);
                if (predName != null && !availableBaitNames.Contains(predName))
                {
                    predName = null;
                }
                yield return (new Rect(singleRowGrid.X + box.X, singleRowGrid.Y + box.Y, box.Width, box.Height), predName);
            }
        }

        private static readonly FrozenSet<string> availableBaitNames = Enum.GetValues(typeof(BaitType)).Cast<BaitType>().Select(bt => bt.GetDescription()).ToFrozenSet();
    }

    [Obsolete]
    /// <summary>
    /// 《How to Cast a Fly Rod: Step-by-Step Guide for Beginners》：https://hookedonfly.fishing/2024/10/how-to-cast-a-fly-rod/
    /// 《How to Catch Fish》：https://game8.co/games/Genshin-Impact/archives/340798
    /// 《Tutorial/Fishing》：https://genshin-impact.fandom.com/wiki/Tutorial/Fishing
    /// </summary>
    public partial class LiftAndHold : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        /// <summary>
        /// 镜头俯仰是否被行为重置
        /// 进入钓鱼模式后、以及提竿后，镜头的俯仰会被重置。进行相关动作前须优化俯仰角，避免鱼塘被脚下的悬崖遮挡。
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> PitchReset { get; private set; } = null!;

        /// <summary>
        /// 不钓啦
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> Abort { get; private set; } = null!;

        private LiftAndHold(string name, ILogger logger, IInputSimulator input) : base(name)
        {
            this.logger = logger;
            this.input = input;
        }

        protected override void Initialize()
        {
            input.Mouse.LeftButtonDown();
            PitchReset.Set(true);
            logger.LogInformation("长按举起鱼竿");
        }

        protected async override Task<Status> Update()
        {
            // todo 这个方案不能令人满意，应该是底层做一个事件监听来记录被点击，底层向上暴露一个和Timer用起来差不多的东西，它应该有个开始记录方法、有个获取从开始到目前是否被点击的方法
            // 但说到底，检查是否鼠标被干扰，不是一个必选的方法。做一个精确度高的图形检测方案，来检测当前位于哪个步骤，会更好。
            if (!Simulation.IsKeyDown(VK.VK_LBUTTON))
            {
                logger.LogWarning("检测到当前鼠标左键状态不符合要求，可能受到干扰，退出任务");
                Abort.Set(true);
                return Status.Failure;
            }
            return Status.Running;
        }
    }

    /// <summary>
    /// 抛竿
    /// </summary>
    public partial class ThrowRod : Behaviour, IScreenshotBehaviour
    {
        private readonly IInputSimulator input;
        private readonly ILogger logger;
        private readonly DrawContent drawContent;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? ignoreObtainedEndTime;
        public const int MAX_NO_BAIT_FISH_TIMES = 2;
        private DateTimeOffset? findTargetEndTime;
        private bool foundTarget;
        private readonly BgiYoloPredictor _predictor;

        private int noPlacementTimes; // 没有落点的次数
        private int noTargetFishTimes; // 没有目标鱼的次数

        // 举起鱼竿确认相关
        private const int _maxLeftButtonRetry = 3; // 左键按下重试次数上限
        private const int _raiseHookWaitMs = 400; // 按下左键后等待举竿画面渲染的时间
        private const int _viewpointSearchStep = 30; // 寻找落点时每次上下移动视角的像素步长
        private const int _raiseHookConfirmRetry = 3; // 钓鱼界面前置校验失败时的重试次数上限（换饵遮罩旧帧干扰场景）
        private const int _outOfBaitPopupDismissRetry = 3; // 关闭鱼饵不足提示条的重试次数上限（防止提示条持续可见时无限按 ESC）

        private DateTimeOffset? _raiseHookWaitEndTime; // 举起鱼竿画面等待的结束时间
        private bool _raiseHookConfirmed; // 是否已按下左键（举起鱼竿）
        private bool _raiseHookConfirmedByTarget; // 是否已通过识别到落点确认鱼竿举起
        private int _leftButtonDownRetryTimes; // 左键按下重试次数
        private int _raiseHookConfirmRetryTimes; // 钓鱼界面前置校验重试次数
        private int _outOfBaitPopupStage; // 鱼饵不足弹窗处理阶段：0=无；1=已关提示条待退出；2=已按 ESC 待置 Abort
        private int _outOfBaitPopupDismissRetryTimes; // 关闭鱼饵不足提示条的重试次数

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        /// <summary>
        /// 鱼塘
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<Fishpond> Fishpond { get; private set; } = null!;

        /// <summary>
        /// 已选择的鱼饵类型
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<BaitType?> SelectedBait { get; private set; } = null!;

        /// <summary>
        /// 是否没有抛竿落点
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> ThrowRodNoTarget { get; private set; } = null!;

        /// <summary>
        /// 没有抛竿落点的次数
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<int> ThrowRodNoTargetTimes { get; private set; } = null!;

        /// <summary>
        /// 是否没有鱼饵适用的鱼
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> ThrowRodNoBaitFish { get; private set; } = null!;

        /// <summary>
        /// 抛竿无目标鱼失败列表
        /// 失败一次就加入一次鱼饵类型，列表中同名鱼饵的数量代表该种失败了几次
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<List<BaitType>> ThrowRodNoBaitFishFailures { get; private set; } = null!;

        /// <summary>
        /// 镜头俯仰是否被行为重置
        /// 进入钓鱼模式后、以及提竿后，镜头的俯仰会被重置。进行相关动作前须优化俯仰角，避免鱼塘被脚下的悬崖遮挡。
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> PitchReset { get; private set; } = null!;

        /// <summary>
        /// 不钓啦
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> Abort { get; private set; } = null!;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<Action<int>> Sleep { get; private set; } = null!;

        private ThrowRod(string name, ILogger logger, IInputSimulator input, BgiYoloPredictor predictor, TimeProvider? timeProvider = null, DrawContent? drawContent = null) : base(name)
        {
            this.logger = logger;
            this.input = input;
            this._predictor = predictor;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
        }

        protected override void Initialize()
        {
            noPlacementTimes = 0;
            noTargetFishTimes = 0;
            ThrowRodNoBaitFish.Set(false);
            ignoreObtainedEndTime = timeProvider.GetLocalNow().AddSeconds(6);
            ThrowRodNoTarget.Set(false);
            findTargetEndTime = timeProvider.GetLocalNow().AddSeconds(5);
            foundTarget = false;
            // 不再每次抛竿翻转视角移动方向，避免连续失败时视角来回甩动
            mouseMoveR = 0d;
            _raiseHookConfirmed = false;
            _raiseHookConfirmedByTarget = false;
            _leftButtonDownRetryTimes = 0;
            _raiseHookConfirmRetryTimes = 0;
            _outOfBaitPopupStage = 0;
            _outOfBaitPopupDismissRetryTimes = 0;
            _raiseHookWaitEndTime = timeProvider.GetLocalNow().AddMilliseconds(_raiseHookWaitMs);
            PitchReset.Set(true);
        }

        protected override void Terminate(Status newStatus)
        {
            drawContent.RemoveRect("Target");
            drawContent.RemoveRect("Fish");
        }

        /// <summary>
        /// 当前鱼
        /// </summary>
        public OneFish? currentFish { get; private set; }

        private double mouseMoveR; // 上下移动视角的切换频率控制参数

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();
            Action<int> sleep = Sleep.Get();

            // 检测"鱼饵不足"弹窗：抛竿时当前选用的饵料已用光会弹出提示条（out_of_bait）。
            // 弹窗会遮挡落点/鱼塘识别，导致抛竿失败并误触发后续退出流程。
            // 处理策略：跨 tick 状态机——先 ESC 关提示条，下一 tick 用新帧确认已关闭后再 ESC 退出钓鱼模式，
            // 再等一个 tick 让"是否退出钓鱼？"确认弹窗渲染完成后才置 Abort，确保冒泡到 QuitFishingMode 时
            // 其 Screenshot 是包含确认弹窗的新帧（避免用 ESC 按下前的旧帧误匹配/误点）。
            using Region outOfBaitPopupRa = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "OutOfBaitPopup", imageRegion));
            bool outOfBaitPopupVisible = !outOfBaitPopupRa.IsEmpty();

            if (_outOfBaitPopupStage == 2)
            {
                // 阶段 2：确认弹窗已渲染（本 tick 截图为新帧），置 Abort 触发冒泡
                _raiseHookConfirmed = false;
                _raiseHookConfirmedByTarget = false;
                _leftButtonDownRetryTimes = 0;
                _raiseHookConfirmRetryTimes = 0;
                _outOfBaitPopupStage = 0;
                _outOfBaitPopupDismissRetryTimes = 0;
                Abort.Set(true);
                return Status.Failure;
            }

            if (_outOfBaitPopupStage == 1 && !outOfBaitPopupVisible)
            {
                // 阶段 1 → 2：提示条已关，按 ESC 退出钓鱼模式（会弹出"是否退出钓鱼？"确认弹窗），
                // 等待确认弹窗渲染后下一 tick 再置 Abort
                input.Keyboard.KeyPress(VK.VK_ESCAPE);
                _outOfBaitPopupStage = 2;
                sleep(500);
                return Status.Running;
            }

            if (outOfBaitPopupVisible)
            {
                // 阶段 0 → 1：首次检测到弹窗（或上次 ESC 未关掉提示条），关闭"鱼饵不足"提示条，
                // 等下一 tick 用新帧确认已关闭后再退出。重试有上限，避免提示条持续可见时无限按 ESC 活锁。
                _outOfBaitPopupDismissRetryTimes++;
                if (_outOfBaitPopupDismissRetryTimes > _outOfBaitPopupDismissRetry)
                {
                    logger.LogWarning("多次按下 ESC 后仍未关闭鱼饵不足弹窗，放弃本轮抛竿并退出钓鱼模式");
                    _raiseHookConfirmed = false;
                    _raiseHookConfirmedByTarget = false;
                    _leftButtonDownRetryTimes = 0;
                    _raiseHookConfirmRetryTimes = 0;
                    _outOfBaitPopupStage = 0;
                    _outOfBaitPopupDismissRetryTimes = 0;
                    Abort.Set(true);
                    return Status.Failure;
                }

                logger.LogWarning("检测到鱼饵不足弹窗，关闭弹窗并退出钓鱼模式，重新开始钓鱼");
                input.Keyboard.KeyPress(VK.VK_ESCAPE);
                _outOfBaitPopupStage = 1;
                sleep(300);
                return Status.Running;
            }

            // 举起鱼竿：按下左键进入抛竿瞄准状态
            // 注：左键按下从 Initialize 挪到 Update（行为树 Initialize 仅做状态初始化，不应有输入副作用）
            if (!_raiseHookConfirmed)
            {
                // 前置校验：确认处于钓鱼界面。
                // 钓鱼界面存在两个标志性按钮：换饵按钮（switch_bait）和退出钓鱼按钮（exit_fishing），
                // 二者任一存在即表明处于钓鱼界面；若都不存在说明未进入钓鱼状态，不应继续抛竿。
                // 注意：换饵刚完成时（ChooseBait 返回 Success 的同一 tick），Screenshot.Get() 可能仍是
                // 换饵界面遮罩的旧帧，遮罩会干扰模板匹配导致两个按钮均漏配。因此校验失败时不立即 Abort，
                // 而是等待后续 tick 的新截图重试（最多 _raiseHookConfirmRetry 次），避免误判退出钓鱼。
                using Region baitButtonRa = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "BaitButton", imageRegion));
                using Region exitFishingButtonRa = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "ExitFishingButton", imageRegion));
                if (baitButtonRa.IsEmpty() && exitFishingButtonRa.IsEmpty())
                {
                    _raiseHookConfirmRetryTimes++;
                    if (_raiseHookConfirmRetryTimes > _raiseHookConfirmRetry)
                    {
                        logger.LogWarning("多次截图仍未检测到换饵按钮或退出钓鱼按钮，可能未处于钓鱼状态，退出抛竿");
                        input.Mouse.LeftButtonUp();
                        Abort.Set(true);
                        return Status.Failure;
                    }

                    logger.LogWarning("未检测到换饵按钮或退出钓鱼按钮，等待新截图重试（第{Retry}次）", _raiseHookConfirmRetryTimes);
                    sleep(100);
                    return Status.Running;
                }

                _raiseHookConfirmed = true;
                logger.LogInformation("长按举起鱼竿");
                input.Mouse.LeftButtonDown();
                PitchReset.Set(true);

                // 按下后等待举竿画面渲染完成，避免用"未举竿"的旧帧做落点检测
                if (_raiseHookWaitEndTime != null && timeProvider.GetLocalNow() < _raiseHookWaitEndTime)
                {
                    sleep(50);
                    return Status.Running;
                }
            }

            // 找 鱼饵落点
            var result = _predictor.Predictor.Detect(imageRegion.CacheImage);
            Debug.WriteLine($"YOLOv8识别: {result.Speed}");
            var fishpond = new Fishpond(result, includeTarget: timeProvider.GetLocalNow() <= ignoreObtainedEndTime);
            Fishpond.Set(fishpond);

            // 以能否识别到落点确认左键是否生效（鱼竿是否举起）：
            // 若识别不到落点，说明左键按下可能未生效（如游戏窗口失焦），重按左键重试（最多 _maxLeftButtonRetry 次）；
            // 一旦识别到落点即视为鱼竿已举起，后续正常进入抛竿流程。
            // 重按次数耗尽仍无落点时，不再判定"左键未按下"，转为按"视野内确实没有鱼塘/落点"
            // 处理（转入下方移视角找落点 → 超时 → ThrowRodNoTarget），保留卡视角岸边的场景语义。
            if (fishpond.TargetRect == null || fishpond.TargetRect == default)
            {
                if (!_raiseHookConfirmedByTarget)
                {
                    _leftButtonDownRetryTimes++;
                    if (_leftButtonDownRetryTimes > _maxLeftButtonRetry)
                    {
                        logger.LogWarning("多次按下左键后仍无法识别到落点，尝试移动视角寻找落点");
                        _raiseHookConfirmedByTarget = true; // 不再重按左键，转入移视角找落点流程
                        // 不 return，继续向下执行移视角找落点逻辑
                    }
                    else
                    {
                        logger.LogWarning("未识别到落点，可能鱼竿未举起，重新按下左键（第{Retry}次）", _leftButtonDownRetryTimes);
                        input.Mouse.LeftButtonDown();
                        _raiseHookWaitEndTime = timeProvider.GetLocalNow().AddMilliseconds(_raiseHookWaitMs);
                        sleep(100);
                        return Status.Running;
                    }
                }
            }
            else
            {
                _raiseHookConfirmedByTarget = true;
            }

            Random _rd = new();
            if (fishpond.TargetRect == null || fishpond.TargetRect == default)
            {
                if (!foundTarget)
                {
                    if (timeProvider.GetLocalNow() <= findTargetEndTime)
                    {
                        // 上下小幅移动视角方便看落点，避免大幅摆动把落点环甩出视野
                        mouseMoveR += Math.PI / 16d;
                        input.Mouse.MoveMouseBy(0, (int)(_viewpointSearchStep * Math.Sign(Math.Cos(mouseMoveR))));
                        sleep(100);
                        return Status.Running;
                    }
                    else
                    {
                        // 到达此处说明已通过落点确认鱼竿举起（_raiseHookConfirmedByTarget=true），
                        // 但 5 秒内仍未找到落点，说明视野内确实没有鱼塘/落点（如卡视角岸边），
                        // 按原逻辑超时失败并记录 ThrowRodNoTarget。
                        logger.LogInformation("举起鱼竿失败，始终没有找到落点");
                        input.Mouse.LeftButtonUp();
                        sleep(2000);
                        input.Mouse.LeftButtonClick();
                        sleep(800);

                        ThrowRodNoTarget.Set(true);
                        ThrowRodNoTargetTimes.TryGet(out int throwRodNoTargetTimes);
                        ThrowRodNoTargetTimes.Set(throwRodNoTargetTimes + 1);
                        if (ThrowRodNoTargetTimes.Get() > 2)
                        {
                            logger.LogWarning("没有找到落点次数过多，目前位置可能视野不佳，退出");
                            Abort.Set(true);
                        }

                        return Status.Failure;
                    }
                }

                noPlacementTimes++;
                sleep(50);
                Debug.WriteLine($"{noPlacementTimes}次未找到鱼饵落点");

                var cX = imageRegion.CacheImage.Width / 2;
                var cY = imageRegion.CacheImage.Height / 2;
                var rdX = _rd.Next(0, imageRegion.CacheImage.Width);
                var rdY = _rd.Next(0, imageRegion.CacheImage.Height);

                var moveX = 100 * (cX - rdX) / imageRegion.CacheImage.Width;
                var moveY = 100 * (cY - rdY) / imageRegion.CacheImage.Height;

                input.Mouse.MoveMouseBy(moveX, moveY);

                if (noPlacementTimes > 25)
                {
                    logger.LogInformation("中途丢失鱼饵落点，重试");
                    input.Mouse.LeftButtonUp();
                    sleep(2000);
                    input.Mouse.LeftButtonClick();
                    sleep(2000);    //此处需要久一点
                    return Status.Failure;
                }

                return Status.Running;
            }
            else
            {
                foundTarget = true;
            }

            Rect fishpondTargetRect = (Rect)fishpond.TargetRect;

            // 找到落点最近的鱼
            currentFish = null;
            var throwRodNoBaitFishFailures = ThrowRodNoBaitFishFailures.Exists() ? ThrowRodNoBaitFishFailures.Get() : [];
            BaitType[] ignoredBaits = throwRodNoBaitFishFailures.GroupBy(f => f).Where(g => g.Count() >= MAX_NO_BAIT_FISH_TIMES).Select(g => g.Key).ToArray();
            var selectedBait = SelectedBait.Get();
            var list = fishpond.Fishes
                .Where(f => !ignoredBaits.Contains(f.FishType.BaitType))   // 不能是已经失败两次的饵;
                .Where(f => f.FishType.BaitType == selectedBait).OrderByDescending(f => f.Confidence)
                .ToList();
            if (list.Count > 0)
            {
                currentFish = list.OrderBy(f => f.Rect.GetCenterPoint().DistanceTo(fishpond.TargetRect.Value.GetCenterPoint())).ThenByDescending(fish => fish.Confidence).First();
            }

            if (currentFish == null)
            {
                Debug.WriteLine("无鱼饵适用鱼");
                noTargetFishTimes++;

                if (noTargetFishTimes > 10)
                {
                    // 没有找到鱼饵适用鱼，重新选择鱼饵
                    ThrowRodNoBaitFish.Set(true);
                    if (selectedBait == null)
                    {
                        throw new NullReferenceException();
                    }
                    if (ThrowRodNoBaitFishFailures.Exists())
                    {
                        ThrowRodNoBaitFishFailures.Get().Add(selectedBait.Value);
                    }
                    else
                    {
                        ThrowRodNoBaitFishFailures.Set([selectedBait.Value]);
                    }
                    if (ThrowRodNoBaitFishFailures.Get().Count(f => f == selectedBait) >= MAX_NO_BAIT_FISH_TIMES)
                    {
                        logger.LogWarning("本次将忽略{bait}", selectedBait.GetDescription());
                    }

                    SelectedBait.Set(null);
                    logger.LogInformation("没有找到鱼饵适用鱼");
                    input.Mouse.LeftButtonUp();
                    sleep(2000);
                    input.Mouse.LeftButtonClick();
                    sleep(800);

                    return Status.Success;
                }

                return Status.Running;
            }
            else
            {
                noTargetFishTimes = 0;
                imageRegion.DrawRect(fishpondTargetRect, "Target", System.Drawing.Pens.White);
                imageRegion.Derive(currentFish.Rect).DrawSelf("Fish");

                // drawContent.PutRect("Target", fishpond.TargetRect.ToRectDrawable());
                // drawContent.PutRect("Fish", currentFish.Rect.ToRectDrawable());

                // 来自 HutaoFisher 的抛竿技术
                var rod = fishpondTargetRect;
                var fish = currentFish.Rect;
                if (ScaleMax1080PCaptureRect == default)  // todo 等配置能注入后和SystemInfo.ScaleMax1080PCaptureRect放到一起
                {
                    if (imageRegion.Width > 1920)
                    {
                        var scale = imageRegion.Width / 1920d;
                        ScaleMax1080PCaptureRect = new Rect(imageRegion.X, imageRegion.Y, 1920, (int)(imageRegion.Height / scale));
                    }
                    else
                    {
                        ScaleMax1080PCaptureRect = new Rect(imageRegion.X, imageRegion.Y, imageRegion.Width, imageRegion.Height);
                    }
                }
                var dx = NormalizeXTo1024(fish.Left + fish.Right - rod.Left - rod.Right) / 2.0;
                var dy = NormalizeYTo576(fish.Top + fish.Bottom - rod.Top - rod.Bottom) / 2.0;
                var dl = Math.Sqrt(dx * dx + dy * dy);
                //logger.LogInformation("dl = {dl}", dl);

                RodInput rodInput = new RodInput
                {
                    rod_x1 = NormalizeXTo1024(rod.Left),
                    rod_x2 = NormalizeXTo1024(rod.Right),
                    rod_y1 = NormalizeYTo576(rod.Top),
                    rod_y2 = NormalizeYTo576(rod.Bottom),
                    fish_x1 = NormalizeXTo1024(fish.Left),
                    fish_x2 = NormalizeXTo1024(fish.Right),
                    fish_y1 = NormalizeYTo576(fish.Top),
                    fish_y2 = NormalizeYTo576(fish.Bottom),
                    fish_label = BigFishType.GetIndex(currentFish.FishType)
                };
                int state = new RodNet().GetRodState(rodInput);

                // 如果hutao钓鱼暂时没有更新导致报错，可以先用这段凑合
                //int state;
                //System.Drawing.Rectangle rod3XRectangle = new System.Drawing.Rectangle(rod.Left - rod.Width, rod.Top - rod.Height, rod.Width * 3, rod.Height * 3);
                //System.Drawing.Rectangle rod5XRectangle = new System.Drawing.Rectangle(rod.Left - rod.Width * 2, rod.Top - rod.Height * 2, rod.Width * 5, rod.Height * 5);
                //System.Drawing.Rectangle fishRectangle = new System.Drawing.Rectangle(fish.Left, fish.Top, fish.Width, fish.Height);
                //if (rod3XRectangle.IntersectsWith(fishRectangle))
                //{
                //    state = 1;
                //}
                //else if (rod5XRectangle.IntersectsWith(fishRectangle))
                //{
                //    state = 0;
                //}
                //else
                //{
                //    state = 2;
                //}

                if (state == -1)
                {
                    // 失败 随机移动鼠标
                    var cX = imageRegion.CacheImage.Width / 2;
                    var cY = imageRegion.CacheImage.Height / 2;
                    var rdX = _rd.Next(0, imageRegion.CacheImage.Width);
                    var rdY = _rd.Next(0, imageRegion.CacheImage.Height);

                    var moveX = 100 * (cX - rdX) / imageRegion.CacheImage.Width;
                    var moveY = 100 * (cY - rdY) / imageRegion.CacheImage.Height;

                    logger.LogInformation("失败 随机移动 {DX}, {DY}", moveX, moveY);
                    input.Mouse.MoveMouseBy(moveX, moveY);
                }
                else if (state == 0)
                {
                    // 成功 抛竿
                    input.Mouse.LeftButtonUp();
                    logger.LogInformation("尝试钓取 {Text}", currentFish.FishType.ChineseName);
                    return Status.Success;
                }
                else if (state == 1)
                {
                    // 太近
                    // set a minimum step
                    dx = dx / dl * 30;
                    dy = dy / dl * 30;
                    // _logger.LogInformation("太近 移动 {DX}, {DY}", dx, dy);
                    input.Mouse.MoveMouseBy((int)(-dx / 1.5), (int)(-dy * 1.5));
                }
                else if (state == 2)
                {
                    // 太远
                    // _logger.LogInformation("太远 移动 {DX}, {DY}", dx, dy);
                    input.Mouse.MoveMouseBy((int)(dx / 1.5), (int)(dy * 1.5));
                }
                sleep((int)dl);
                return Status.Running;
            }
        }

        private Rect ScaleMax1080PCaptureRect { get; set; }

        private double NormalizeXTo1024(int x)
        {
            return x * 1.0 / ScaleMax1080PCaptureRect.Width * 1024;
        }

        private double NormalizeYTo576(int y)
        {
            return y * 1.0 / ScaleMax1080PCaptureRect.Height * 576;
        }
    }

    /// <summary>
    /// 检查抛竿结果
    /// </summary>
    public partial class CheckThrowRod : Behaviour, IScreenshotBehaviour
    {
        private const int _initialDelaySeconds = 3; // 抛竿后先等待下杆画面出现的基础延迟
        private const int _renderWaitSeconds = 2; // 基础延迟后等待"等待咬钩"按钮渲染的额外窗口，避免用未渲染完成的旧帧误判抛竿失败

        private readonly ILogger logger;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? timeDelay;
        private DateTimeOffset? _checkDeadline; // 双重校验的最终判定截止时间
        private bool hasChecked;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        private CheckThrowRod(string name, ILogger logger, TimeProvider? timeProvider = null) : base(name)
        {
            this.logger = logger;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void Initialize()
        {
            timeDelay = timeProvider.GetLocalNow().AddSeconds(_initialDelaySeconds);
            _checkDeadline = timeProvider.GetLocalNow().AddSeconds(_initialDelaySeconds + _renderWaitSeconds);
            hasChecked = false;
        }

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();

            if (timeProvider.GetLocalNow() < timeDelay || hasChecked)
            {
                return Status.Running;
            }

            // 抛竿成功判定（双重校验）：
            // 1. 换饵按钮（BaitButton/switch_bait）应消失 —— 说明已退出换饵界面
            // 2. 等待咬钩按钮（WaitBiteButton/wait_bite）应出现 —— 说明已进入下杆等待状态
            // 二者同时满足才算抛竿成功，避免仅凭单个按钮误判。
            using Region baitButtonRa = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "BaitButton", imageRegion));
            using Region waitBiteButtonRa = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "WaitBiteButton", imageRegion));
            if (baitButtonRa.IsEmpty() && !waitBiteButtonRa.IsEmpty())
            {
                hasChecked = true;
                return Status.Running;
            }
            else if (timeProvider.GetLocalNow() < _checkDeadline)
            {
                // 换饵按钮已消失但"等待咬钩"按钮尚未渲染出来（抛竿动画/UI 切换未完成），
                // 继续用后续新截图重试，而不是立即返回 Failure——否则每次正常下杆都会被误判为
                // 抛竿失败而触发整轮重抛（几乎每次下杆都重复一次）。
                return Status.Running;
            }
            else
            {
                logger.LogInformation("抛竿失败");
                return Status.Failure;
            }
        }
    }

    public partial class FishBiteTimeout : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? waitFishBiteTimeout;
        private readonly int seconds;
        public bool leftButtonClicked;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        private FishBiteTimeout(string name, int seconds, ILogger logger, IInputSimulator input, TimeProvider? timeProvider = null) : base(name)
        {
            this.logger = logger;
            this.seconds = seconds;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void Initialize()
        {
            waitFishBiteTimeout = timeProvider.GetLocalNow().AddSeconds(seconds);
            leftButtonClicked = false;
        }

        protected async override Task<Status> Update()
        {
            if (timeProvider.GetLocalNow() >= waitFishBiteTimeout)
            {
                if (leftButtonClicked)
                {
                    logger.LogInformation($"收杆成功");

                    return Status.Failure;
                }
                else
                {
                    logger.LogInformation($"{seconds}秒没有咬杆，本次收杆");
                    leftButtonClicked = true;
                    input.Mouse.LeftButtonClick();
                    waitFishBiteTimeout = timeProvider.GetLocalNow().AddSeconds(2);
                    return Status.Running;
                }
            }
            else
            {
                return Status.Running;
            }
        }
    }

    /// <summary>
    /// 检查提竿结果
    /// </summary>
    public partial class CheckRaiseHook : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? timeDelay;
        private bool hasChecked;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        /// <summary>
        /// 检查提竿结果
        /// 如果仍发现提竿按钮则失败
        /// </summary>
        private CheckRaiseHook(string name, ILogger logger, TimeProvider? timeProvider = null) : base(name)
        {
            this.logger = logger;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void Initialize()
        {
            timeDelay = timeProvider.GetLocalNow().AddSeconds(3);
            hasChecked = false;
        }

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();

            if (timeProvider.GetLocalNow() < timeDelay || hasChecked)
            {
                return Status.Running;
            }

            using Region btnRectArea = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "WaitBiteButton", imageRegion));
            if (btnRectArea.IsEmpty())
            {
                hasChecked = true;
                return Status.Running;
            }
            else
            {
                logger.LogInformation("提竿失败");
                return Status.Failure;
            }
        }
    }

    /// <summary>
    /// 自动提竿
    /// </summary>
    public partial class FishBite : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly DrawContent drawContent;
        private readonly IOcrService ocrService;
        private readonly string getABiteLocalizedString;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        private FishBite(string name, ILogger logger, IInputSimulator input, IOcrService ocrService, DrawContent? drawContent = null, CultureInfo? cultureInfo = null, IStringLocalizer? stringLocalizer = null) : base(name)
        {
            this.logger = logger;
            this.input = input;
            this.ocrService = ocrService;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
            this.getABiteLocalizedString = stringLocalizer == null ? "上钩" : stringLocalizer.WithCultureGet(cultureInfo, "上钩");
        }

        protected override void Initialize()
        {
            logger.LogInformation("提竿识别开始");
        }

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();

            // 自动识别的钓鱼框向下延伸到屏幕中间
            //var liftingWordsAreaRect = new Rect(fishBoxRect.X, fishBoxRect.Y + fishBoxRect.Height * 2,
            //    fishBoxRect.Width, imageRegion.CaptureRectArea.SrcMat.Height / 2 - fishBoxRect.Y - fishBoxRect.Height * 5);
            // 上半屏幕和中间1/2的区域
            var liftingWordsAreaRect = new Rect(imageRegion.SrcMat.Width / 3, 0, imageRegion.SrcMat.Width / 3,
                imageRegion.SrcMat.Height / 2);
            //VisionContext.Instance().DrawContent.PutRect("liftingWordsAreaRect", liftingWordsAreaRect.ToRectDrawable(new Pen(Color.Cyan, 2)));
            using var wordCaptureMat = new Mat(imageRegion.SrcMat, liftingWordsAreaRect);
            var currentBiteWordsTips = AutoFishingImageRecognition.MatchFishBiteWords(wordCaptureMat, liftingWordsAreaRect);
            if (currentBiteWordsTips != null)
            {
                // VisionContext.Instance().DrawContent.PutRect("FishBiteTips",
                //     currentBiteWordsTips
                //         .ToWindowsRectangleOffset(liftingWordsAreaRect.X, liftingWordsAreaRect.Y)
                //         .ToRectDrawable());
                using var tipsRa = imageRegion.Derive((Rect)currentBiteWordsTips + liftingWordsAreaRect.Location);
                tipsRa.DrawSelf("FishBiteTips");

                return RaiseRod("文字块");
            }

            // 图像提竿判断
            using var liftRodButtonRa = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "LiftRodButton", imageRegion));
            if (!liftRodButtonRa.IsEmpty())
            {
                return RaiseRod("图像识别");
            }

            // OCR 提竿判断
            var text = ocrService.Ocr(wordCaptureMat);

            if (!string.IsNullOrEmpty(text) && StringUtils.RemoveAllSpace(text).Contains(this.getABiteLocalizedString))
            {
                return RaiseRod("OCR");
            }

            // 拉条识别提竿判断：拉条（黄色进度条）只在鱼上钩后出现，为纯色检测，不受帧率/文字位置/模板匹配影响，
            // 中鱼后默认会出现进度条，作为兜底检测
            using var fishBarTopMat = new Mat(imageRegion.SrcMat, new Rect(0, 0, imageRegion.Width, imageRegion.Height / 2));
            var fishBarRects = AutoFishingImageRecognition.GetFishBarRect(fishBarTopMat);
            if (AutoFishingImageRecognition.IsValidFishBar(fishBarRects, fishBarTopMat.Width))
            {
                return RaiseRod("拉条识别");
            }

            return Status.Running;
        }

        private Status RaiseRod(string method)
        {
            input.Mouse.LeftButtonClick();
            logger.LogInformation(@"┌------------------------┐");
            logger.LogInformation("  自动提竿({m})", method);
            drawContent.RemoveRect("FishBiteTips");
            return Status.Success;
        }
    }

    /// <summary>
    /// 进入钓鱼界面先尝试获取钓鱼框的位置
    /// </summary>
    public partial class GetFishBoxArea : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? waitFishBoxAppearEndTime;
        private readonly bool saveScreenshotOnError;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        /// <summary>
        /// 拉条位置的识别框
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<Rect> FishBoxRect { get; private set; } = null!;

        private GetFishBoxArea(string name, ILogger logger, bool saveScreenshotOnError, TimeProvider? timeProvider = null) : base(name)
        {
            this.logger = logger;
            this.saveScreenshotOnError = saveScreenshotOnError;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void Initialize()
        {
            logger.LogInformation("钓鱼框识别开始");
            waitFishBoxAppearEndTime = timeProvider.GetLocalNow().AddSeconds(5);
        }

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();

            if (timeProvider.GetLocalNow() > waitFishBoxAppearEndTime)
            {
                logger.LogInformation("钓鱼框识别失败");
                return Status.Failure;
            }

            using var topMat = new Mat(imageRegion.SrcMat, new Rect(0, 0, imageRegion.Width, imageRegion.Height / 2));

            var rects = AutoFishingImageRecognition.GetFishBarRect(topMat);
            if (rects != null && rects.Count == 2 && Math.Abs(rects[0].Height - rects[1].Height) > 10)
            {
                if (saveScreenshotOnError)
                {
                    ScreenshotVisitor.SaveScreenshot(imageRegion, $"{DateTime.Now:yyyyMMddHHmmssfff}_{this.GetType().Name}_Error.png");
                }
                logger.LogError("两个矩形高度差距过大，未识别到钓鱼框");
                return Status.Running;
            }

            // 复用统一的拉条几何校验：恰好2个矩形、游标/进度条位置关系等
            if (AutoFishingImageRecognition.IsValidFishBar(rects, topMat.Width))
            {
                Rect _cur, _right;
                if (rects![0].Width < rects[1].Width)
                {
                    _cur = rects[0];
                    _right = rects[1];
                }
                else
                {
                    _cur = rects[1];
                    _right = rects[0];
                }

                int hExtra = _cur.Height, vExtra = _cur.Height / 4;
                {
                    int rx = _cur.X - hExtra;
                    int ry = _cur.Y - vExtra;
                    int rw = (topMat.Width / 2 - _cur.X) * 2 + hExtra * 2;
                    int rh = _cur.Height + vExtra * 2;
                    var rect = new Rect(rx, ry, rw, rh).ClampTo(imageRegion.SrcMat);
                    FishBoxRect.Set(rect);
                }
                using var boxRa = imageRegion.Derive(FishBoxRect.Get());
                boxRa.DrawSelf("FishBox", System.Drawing.Pens.LightPink);
                logger.LogInformation("  识别到钓鱼框");
                return Status.Success;
            }

            return Status.Running;
        }
    }

    /// <summary>
    /// 拉条
    /// </summary>
    public partial class Fishing : Behaviour, IScreenshotBehaviour
    {
        private readonly IInputSimulator input;
        private readonly ILogger logger;
        private readonly TimeProvider timeProvider;
        private readonly DrawContent drawContent;
        private DateTimeOffset? noDetectionDuringTime;
        private readonly bool saveScreenshotOnError;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        /// <summary>
        /// 拉条位置的识别框
        /// </summary>
        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<Rect> FishBoxRect { get; private set; } = null!;

        private Fishing(string name, ILogger logger, bool saveScreenshotOnError, IInputSimulator input, TimeProvider? timeProvider = null, DrawContent? drawContent = null) : base(name)
        {
            this.logger = logger;
            this.saveScreenshotOnError = saveScreenshotOnError;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.drawContent = drawContent ?? VisionContext.Instance().DrawContent;
        }

        protected override void Initialize()
        {
            logger.LogInformation("拉扯开始");
        }

        private MOUSEEVENTF _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();

            using var fishBarMat = new Mat(imageRegion.SrcMat, FishBoxRect.Get());
            var rects = AutoFishingImageRecognition.GetFishBarRect(fishBarMat);
            if (rects != null && rects.Count > 0)
            {
                // 超过3个矩形是异常情况，取高度最高的三个矩形进行识别
                if (rects.Count > 3)
                {
                    if (saveScreenshotOnError)
                    {
                        ScreenshotVisitor.SaveScreenshot(imageRegion, $"{DateTime.Now:yyyyMMddHHmmssfff}_{this.GetType().Name}_Error.png");
                    }
                    logger.LogError("识别到超过3个矩形，取前三");
                    rects.Sort((a, b) => b.Height.CompareTo(a.Height));
                    rects.RemoveRange(3, rects.Count - 3);
                }

                //Debug.WriteLine($"识别到{rects.Count} 个矩形");
                if (rects.Count == 2)
                {
                    // 游标矩形不在区间内或恰在区间两端时只会检测到两个矩形
                    Rect _cursor, _target;
                    if (rects[0].Width < rects[1].Width)
                    {
                        _cursor = rects[0];
                        _target = rects[1];
                    }
                    else
                    {
                        _cursor = rects[1];
                        _target = rects[0];
                    }
                    if (_target.Width < _cursor.Width * 10) // 异常：当目标矩形明显不够长时视为无效检测，不作为
                    {
                        return Status.Running;
                    }

                    PutRects(imageRegion, _target, _cursor, new Rect());

                    if (_cursor.X < _target.X)
                    {
                        if (_prevMouseEvent != MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            input.Mouse.LeftButtonDown();
                            //input.PostMessage(TaskContext.Instance().GameHandle).LeftButtonDown();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN;
                            //Debug.WriteLine("进度不到 左键按下");
                        }
                    }
                    else
                    {
                        if (_prevMouseEvent == MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            input.Mouse.LeftButtonUp();
                            //input.PostMessage(TaskContext.Instance().GameHandle).LeftButtonUp();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                            //Debug.WriteLine("进度超出 左键松开");
                        }
                    }
                }
                else if (rects.Count == 3)
                {
                    // 游标矩形在区间内会检测到三个矩形，即目标区间被游标分割成左半和右半
                    Rect _cursor, _left, _right;
                    rects.Sort((a, b) => a.X.CompareTo(b.X));
                    _left = rects[0];
                    _cursor = rects[1];
                    _right = rects[2];
                    PutRects(imageRegion, _left, _cursor, _right);

                    if (_right.X + _right.Width - (_cursor.X + _cursor.Width) <= _cursor.X - _left.X)
                    {
                        if (_prevMouseEvent == MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            input.Mouse.LeftButtonUp();
                            //input.PostMessage(TaskContext.Instance().GameHandle).LeftButtonUp();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                            //Debug.WriteLine("进入框内中间 左键松开");
                        }
                    }
                    else
                    {
                        if (_prevMouseEvent != MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN)
                        {
                            input.Mouse.LeftButtonDown();
                            //input.PostMessage(TaskContext.Instance().GameHandle).LeftButtonDown();
                            _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN;
                            //Debug.WriteLine("未到框内中间 左键按下");
                        }
                    }
                }
                else
                {
                    PutRects(imageRegion, new Rect(), new Rect(), new Rect());
                }
            }
            else
            {
                PutRects(imageRegion, new Rect(), new Rect(), new Rect());

                if (noDetectionDuringTime == null)
                {
                    noDetectionDuringTime = timeProvider.GetLocalNow().AddSeconds(1);
                    return Status.Running;
                }
                else if (timeProvider.GetLocalNow() < noDetectionDuringTime)
                {
                    return Status.Running;
                }

                // 没有矩形视为已经完成钓鱼
                drawContent.RemoveRect("FishBox");
                _prevMouseEvent = MOUSEEVENTF.MOUSEEVENTF_LEFTUP;
                logger.LogInformation("  拉扯结束");
                logger.LogInformation(@"└------------------------┘");

                // 保证鼠标松开
                input.Mouse.LeftButtonUp();


                return Status.Success;
            }


            noDetectionDuringTime = null;
            return Status.Running;
        }

        private void PutRects(ImageRegion imageRegion, Rect left, Rect cur, Rect right)
        {
            //var list = new List<RectDrawable>
            //{
            //    left.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(System.Drawing.Pens.Red),
            //    cur.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(System.Drawing.Pens.Red),
            //    right.ToWindowsRectangleOffset(_fishBoxRect.X, _fishBoxRect.Y).ToRectDrawable(System.Drawing.Pens.Red)
            //};
            using var fishBoxRa = imageRegion.Derive(FishBoxRect.Get());
            var list = new List<RectDrawable>
                {
                    fishBoxRa.ToRectDrawable(left, "left", System.Drawing.Pens.Red),
                    fishBoxRa.ToRectDrawable(cur, "cur", System.Drawing.Pens.Red),
                    fishBoxRa.ToRectDrawable(right, "right", System.Drawing.Pens.Red),
                }.Where(r => r.Rect.Height != 0).ToList();
            drawContent.PutOrRemoveRectList("FishingBarAll", list);
        }
    }

    /// <summary>
    /// 如果视角被其他行为重置过，则调整视角至俯视
    /// </summary>
    public partial class MoveViewpointDown : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        /// <summary>
        /// 镜头俯仰是否被行为重置
        /// 进入钓鱼模式后、以及提竿后，镜头的俯仰会被重置。进行相关动作前须优化俯仰角，避免鱼塘被脚下的悬崖遮挡。
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> PitchReset { get; private set; } = null!;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<Action<int>> Sleep { get; private set; } = null!;

        private MoveViewpointDown(string name, ILogger logger, IInputSimulator input) : base(name)
        {
            this.logger = logger;
            this.input = input;
        }

        protected async override Task<Status> Update()
        {
            if (!PitchReset.Exists() || PitchReset.Get())
            {
                logger.LogInformation("调整视角至俯视");
                PitchReset.Set(false);
                // 下移视角方便看鱼
                input.Mouse.MoveMouseBy(0, 500);
                Sleep.Get()(100);
                return Status.Running;
            }
            return Status.Success;
        }
    }

    /// <summary>
    /// 检查开始钓一条鱼的初始状态
    /// </summary>
    public partial class CheckInitalState : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? moveMouseInterval;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        private CheckInitalState(string name, ILogger logger, IInputSimulator input, TimeProvider? timeProvider = null) : base(name)
        {
            this.logger = logger;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void Initialize()
        {
            logger.LogInformation("开始寻找换饵图标");
            theta = 0d;
        }

        private double theta;

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();

            using Region btnRectArea = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "BaitButton", imageRegion));
            if (btnRectArea.IsEmpty())
            {
                if (moveMouseInterval == null || timeProvider.GetLocalNow() > moveMouseInterval)
                {
                    theta += Math.PI / 10;
                    double rho = 10 + 2 * theta;
                    double x = rho * Math.Cos(theta);
                    double y = rho * Math.Sin(theta);

                    input.Mouse.MoveMouseBy((int)x, (int)y);
                    moveMouseInterval = timeProvider.GetLocalNow().AddSeconds(0.1);
                }
                return Status.Running;
            }
            else
            {
                logger.LogInformation("找到换饵图标");
                return Status.Success;
            }
        }
    }
}
