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
                BaitType[] throwRodNoTargetFishfailuresIgnoredBaits = ThrowRodNoBaitFishFailures.Exists() ? ThrowRodNoBaitFishFailures.Get().GroupBy(f => f).Where(g => g.Count() >= LiftRod.MAX_NO_BAIT_FISH_TIMES).Select(g => g.Key).ToArray() : [];

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
    /// 举起鱼竿并瞄准鱼
    /// </summary>
    public partial class LiftRod : Behaviour, IScreenshotBehaviour
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

        private LiftRod(string name, ILogger logger, IInputSimulator input, BgiYoloPredictor predictor, TimeProvider? timeProvider = null, DrawContent? drawContent = null) : base(name)
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
            mouseMoveI *= -1;
            mouseMoveR = 0d;

            input.Mouse.LeftButtonDown();
            PitchReset.Set(true);
            logger.LogInformation("长按举起鱼竿");
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

        private int mouseMoveI = 1; // 上下移动视角的初始方向控制参数
        private double mouseMoveR; // 上下移动视角的切换频率控制参数

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();
            Action<int> sleep = Sleep.Get();

            // 找 鱼饵落点
            var result = _predictor.Predictor.Detect(imageRegion.CacheImage);
            Debug.WriteLine($"YOLOv8识别: {result.Speed}");
            var fishpond = new Fishpond(result, includeTarget: timeProvider.GetLocalNow() <= ignoreObtainedEndTime);
            Fishpond.Set(fishpond);
            Random _rd = new();
            if (fishpond.TargetRect == null || fishpond.TargetRect == default)
            {
                if (!foundTarget)
                {
                    if (timeProvider.GetLocalNow() <= findTargetEndTime)
                    {
                        // 上下移动视角方便看落点
                        mouseMoveR += Math.PI / 16d;
                        input.Mouse.MoveMouseBy(0, mouseMoveI * 80 * Math.Sign(Math.Cos(mouseMoveR)));
                        sleep(100);
                        return Status.Running;
                    }
                    else
                    {
                        logger.LogInformation("举起鱼竿失败，始终没有找到落点");

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

                    return Status.Failure;
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
    /// 抛竿
    /// <para>抛出后瞬间无法收杆，且有可能被回弹，因此视滞空时为运行中</para>
    /// </summary>
    public partial class Cast : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly TimeProvider timeProvider;

        private DateTimeOffset? castDelay;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        private Cast(string name, ILogger logger, IInputSimulator input, TimeProvider? timeProvider = null) : base(name)
        {
            this.logger = logger;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void Initialize()
        {
            castDelay = null;
        }

        protected async override Task<Status> Update()
        {
            if (castDelay == null)
            {
                logger.LogInformation("抛竿");
                input.Mouse.LeftButtonUp();
                castDelay = timeProvider.GetLocalNow();
                return Status.Running;
            }
            else if ((timeProvider.GetLocalNow() - castDelay.Value).TotalSeconds < 2)
            {
                return Status.Running;
            }

            return Status.Success;
        }
    }

    /// <summary>
    /// 检查抛竿结果
    /// </summary>
    public partial class CheckThrowRod : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly TimeProvider timeProvider;
        private DateTimeOffset? timeDelay;
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

            using Region btnRectArea = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "BaitButton", imageRegion));
            if (btnRectArea.IsEmpty())
            {
                hasChecked = true;
                return Status.Running;
            }
            else
            {
                logger.LogInformation("抛竿失败");
                return Status.Failure;
            }
        }
    }

    /// <summary>
    /// 咬钩超时检查
    /// <para>内置先收杆，延迟一定时间后才失败的机制。配合CheckFishBite并行，可以避免超时和咬钩撞上</para>
    /// </summary>
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
    /// 检查是否咬钩
    /// </summary>
    public partial class CheckFishBite : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly DrawContent drawContent;
        private readonly IOcrService ocrService;
        private readonly string getABiteLocalizedString;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        private CheckFishBite(string name, ILogger logger, IOcrService ocrService, DrawContent? drawContent = null, CultureInfo? cultureInfo = null, IStringLocalizer? stringLocalizer = null) : base(name)
        {
            this.logger = logger;
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

            return Status.Running;
        }

        private Status RaiseRod(string method)
        {
            logger.LogInformation(@"┌------------------------┐");
            logger.LogInformation("  提竿识别=>{m}", method);
            drawContent.RemoveRect("FishBiteTips");
            return Status.Success;
        }
    }

    /// <summary>
    /// 提竿，无论是否咬钩。
    /// <para>如果超出一定时间没有找到下钩或咬钩图标，就不操作直接成功</para>
    /// </summary>
    public partial class RaiseHook : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger logger;
        private readonly IInputSimulator input;
        private readonly TimeProvider timeProvider;

        /// <summary>
        /// 提竿后延迟这个时间才返回成功
        /// </summary>
        private DateTimeOffset? raiseHookDelay;

        /// <summary>
        /// 在这个时间内尝试查找图标并提竿
        /// </summary>
        private DateTimeOffset? raiseHookTimeout;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        private RaiseHook(string name, ILogger logger, IInputSimulator input, TimeProvider? timeProvider = null) : base(name)
        {
            this.logger = logger;
            this.input = input;
            this.timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void Initialize()
        {
            raiseHookDelay = null;
            raiseHookTimeout = timeProvider.GetLocalNow().AddSeconds(2);
        }

        protected async override Task<Status> Update()
        {
            if (timeProvider.GetLocalNow() < raiseHookTimeout && raiseHookDelay == null)
            {
                var imageRegion = Screenshot.Get();

                using Region waitBiteButton = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "WaitBiteButton", imageRegion));
                using Region liftRodButton = imageRegion.Find(RecognitionAssets.Get("AutoFishing", "LiftRodButton", imageRegion));
                if (!(waitBiteButton.IsEmpty() && liftRodButton.IsEmpty()))
                {
                    logger.LogInformation("提竿");
                    input.Mouse.LeftButtonClick();
                    raiseHookDelay = timeProvider.GetLocalNow().AddSeconds(0.8);
                }

                return Status.Running;
            }
            else if (raiseHookDelay != null && timeProvider.GetLocalNow() < raiseHookDelay.Value)
            {
                return Status.Running;
            }

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
            if (rects != null && rects.Count == 2)
            {
                Rect _cur, _right;
                if (Math.Abs(rects[0].Height - rects[1].Height) > 10)
                {
                    if (saveScreenshotOnError)
                    {
                        ScreenshotVisitor.SaveScreenshot(imageRegion, $"{DateTime.Now:yyyyMMddHHmmssfff}_{this.GetType().Name}_Error.png");
                    }
                    logger.LogError("两个矩形高度差距过大，未识别到钓鱼框");
                    return Status.Running;
                }

                if (rects[0].Width < rects[1].Width)
                {
                    _cur = rects[0];
                    _right = rects[1];
                }
                else
                {
                    _cur = rects[1];
                    _right = rects[0];
                }

                if (_right.X < _cur.X // cur 是游标位置, 在初始状态下，cur 一定在right左边
                    || _cur.Width > _right.Width // right一定比cur宽
                    || _cur.X + _cur.Width > topMat.Width / 2 // cur 一定在屏幕左侧
                    || _cur.X + _cur.Width > _right.X - _right.Width / 2 // cur 一定在right左侧+right的一半宽度
                    || _cur.X + _cur.Width > topMat.Width / 2 - _right.Width // cur 一定在屏幕中轴线减去整个right的宽度的位置左侧
                   )
                {
                    return Status.Running;
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
