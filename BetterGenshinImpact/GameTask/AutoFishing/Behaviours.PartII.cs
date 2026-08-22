using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.Area;
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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    /// <summary>
    /// 如果超时就发布Abort
    /// </summary>
    public partial class WholeProcessTimeout : Behaviour
    {
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private DateTimeOffset? _timeout;
        private readonly int _seconds;

        /// <summary>
        /// 不钓啦
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> Abort { get; private set; } = null!;

        private WholeProcessTimeout(string name, ILogger logger, int seconds, TimeProvider? timeProvider = null) : base(name)
        {
            _logger = logger;
            _seconds = seconds;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void Initialize()
        {
            _logger.LogInformation($"钓鱼任务将在{_seconds}秒后超时");
            _timeout = _timeProvider.GetLocalNow().AddSeconds(_seconds);
        }

        protected async override Task<Status> Update()
        {
            if ((!Abort.Exists() || !Abort.Get()) &&  _timeProvider.GetLocalNow() >= _timeout)
            {
                Abort.Set(true);
                _logger.LogInformation($"{_seconds}秒超时已到，结束任务");
            }
            return Status.Running;
        }
    }

    /// <summary>
    /// 如果未超时返回运行中，超时返回失败
    /// </summary>
    public partial class FindFishTimeout : Behaviour
    {
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private DateTimeOffset? _timeout;
        private readonly int _seconds;

        /// <summary>
        /// 不钓啦
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> Abort { get; private set; } = null!;

        private FindFishTimeout(string name, int seconds, ILogger logger, TimeProvider? timeProvider = null) : base(name)
        {
            _logger = logger;
            _seconds = seconds;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override void Initialize()
        {
            _timeout = _timeProvider.GetLocalNow().AddSeconds(_seconds);
        }

        protected async override Task<Status> Update()
        {
            if (_timeProvider.GetLocalNow() >= _timeout)
            {
                Abort.Set(true);
                _logger.LogInformation($"{_seconds}秒没有{Name}，结束任务");
                return Status.Failure;
            }
            else
            {
                return Status.Running;
            }
        }
    }

    /// <summary>
    /// 转圈圈调整视角
    /// </summary>
    public partial class TurnAround : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger _logger;
        private readonly IInputSimulator _input;
        private readonly BgiYoloPredictor _bgiYoloPredictor;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<Action<int>> Sleep { get; private set; } = null!;

        private TurnAround(string name, ILogger logger, IInputSimulator input, BgiYoloPredictor bgiYoloPredictor) : base(name)
        {
            _logger = logger;
            _input = input;
            _bgiYoloPredictor = bgiYoloPredictor;
        }

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();
            Action<int> sleep = Sleep.Get();
            var result = _bgiYoloPredictor.Predictor.Detect(imageRegion.CacheImage);
            if (result.Any())
            {
                Fishpond fishpond = new Fishpond(result);
                _logger.LogInformation("定位到鱼塘：" + string.Join('、',
                    fishpond.Fishes.GroupBy(f => f.FishType).Select(g => $"{g.Key.ChineseName}{g.Count()}条")));
                int i = 0;
                foreach (var fish in fishpond.Fishes)
                {
                    imageRegion.Derive(fish.Rect).DrawSelf($"{fish.FishType.ChineseName}.{i++}");
                }

                sleep(1000);
                VisionContext.Instance().DrawContent.ClearAll();

                var oneFourthX = imageRegion.CacheImage.Width / 4;
                var threeFourthX = imageRegion.CacheImage.Width * 3 / 4;
                if (fishpond.FishpondRect.Left > threeFourthX)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(100, 0);
                    sleep(100);
                    return Status.Running;
                }
                else if (fishpond.FishpondRect.Right < oneFourthX)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(-100, 0);
                    sleep(100);
                    return Status.Running;
                }

                #region 1、使人物朝向和镜头方向一致；2、打断角色待机动作，避免钓鱼F交互键被吞

                // 加入昼夜切换后，使用KeyPress按S键被莫名吞掉了
                // 并且发现如果原地空格跳跃后紧跟按一下S键，角色会向侧后方走去
                // 于是使用"按一段时间"来代替KeyPress的"按一瞬间"，以求稳定的表现
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_S);
                sleep(100);
                Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_S);
                sleep(400);
                Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
                sleep(100);
                Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
                sleep(700);

                #endregion

                _logger.LogInformation("视角调整完毕");
                return Status.Success;
            }

            _input.Mouse.MoveMouseBy(100, 0);
            sleep(100);

            return Status.Running;
        }
    }

    /// <summary>
    /// 进入钓鱼模式
    /// </summary>
    public partial class EnterFishingMode : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger _logger;
        private readonly IInputSimulator _input;
        private readonly InferenceSession _session;
        private readonly Dictionary<string, float[]> _prototypes;
        private readonly TimeProvider _timeProvider;
        private DateTimeOffset? _pressFWaitEndTime;
        private DateTimeOffset? _clickWhiteConfirmButtonWaitEndTime;
        private DateTimeOffset? _overallWaitEndTime;
        private readonly string _fishingLocalizedString;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        /// <summary>
        /// 已选择的鱼饵类型
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<BaitType?> SelectedBait { get; private set; } = null!;

        /// <summary>
        /// 镜头俯仰是否被行为重置
        /// 进入钓鱼模式后、以及提竿后，镜头的俯仰会被重置。进行相关动作前须优化俯仰角，避免鱼塘被脚下的悬崖遮挡。
        /// </summary>
        [BlackboardKey(Access = Access.Write)]
        public BehaviourKeyAccess<bool> PitchReset { get; private set; } = null!;

        private EnterFishingMode(string name, ILogger logger, IInputSimulator input, InferenceSession session, Dictionary<string, float[]> prototypes, TimeProvider? timeProvider = null, CultureInfo? cultureInfo = null, IStringLocalizer? stringLocalizer = null) : base(name)
        {
            _logger = logger;
            _input = input;
            _session = session;
            _prototypes = prototypes;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _fishingLocalizedString = stringLocalizer == null ? "钓鱼" : stringLocalizer.WithCultureGet(cultureInfo, "钓鱼");
        }

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();

            if (this.Status == Status.Invalid)
            {
                _overallWaitEndTime = _timeProvider.GetLocalNow().AddSeconds(10);
                return Status.Running;
            }

            if ((_pressFWaitEndTime == null || _pressFWaitEndTime < _timeProvider.GetLocalNow()) &&
                Bv.FindFAndPress(imageRegion, _input.Keyboard, _fishingLocalizedString))
            {
                _logger.LogInformation("按下钓鱼键");
                _pressFWaitEndTime = _timeProvider.GetLocalNow().AddSeconds(3);
                return Status.Running;
            }
            else if ((_clickWhiteConfirmButtonWaitEndTime == null ||
                      _clickWhiteConfirmButtonWaitEndTime < _timeProvider.GetLocalNow()) &&
                     Bv.ClickWhiteConfirmButton(imageRegion))
            {
                // 截取鱼饵图标区域（正方形，宽高均取 6.5% 的屏幕宽度）
                // 最后一个参数有意用 Width 而非 Height，目的是保持正方形裁剪
                // 经验算在 16:9 常见分辨率（720p/1080p/1440p）下 Y+H 不会超出图像高度，暂不加钳位
                using Mat subMat = imageRegion.SrcMat.SubMat(new Rect((int)(0.824 * imageRegion.Width), (int)(0.669 * imageRegion.Height), (int)(0.065 * imageRegion.Width), (int)(0.065 * imageRegion.Width)));
                using Mat resized = subMat.Resize(new Size(125, 125));
                (string? predName, _) = GridIconsAccuracyTestTask.Infer(resized, _session, _prototypes);
                if (predName!.TryGetEnumValueFromDescription(out BaitType? parsedBait))
                {
                    SelectedBait.Set(parsedBait);
                    _logger.LogInformation("点击开始钓鱼，当前鱼饵为{bait}", parsedBait.Value.GetDescription());
                }
                else
                {
                    SelectedBait.Set(null);
                    _logger.LogInformation("点击开始钓鱼，当前鱼饵未识别");
                }

                PitchReset.Set(true);

                _clickWhiteConfirmButtonWaitEndTime = _timeProvider.GetLocalNow().AddSeconds(3);

                return Status.Running;
            }

            if (imageRegion.Find(RecognitionAssets.Get("AutoFishing", "ExitFishingButton", imageRegion)).IsEmpty())
            {
                if (_overallWaitEndTime < _timeProvider.GetLocalNow())
                {
                    _logger.LogInformation("进入钓鱼模式失败");
                    return Status.Failure;
                }
                else
                {
                    return Status.Running;
                }
            }
            else
            {
                _logger.LogInformation("进入钓鱼模式");
                return Status.Success;
            }
        }
    }

    /// <summary>
    /// 退出钓鱼模式
    /// </summary>
    public partial class QuitFishingMode : Behaviour, IScreenshotBehaviour
    {
        private readonly ILogger _logger;
        private readonly IInputSimulator _input;
        private readonly string _fishingLocalizedString;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<Action<int>> Sleep { get; private set; } = null!;

        private QuitFishingMode(string name, ILogger logger, IInputSimulator input, CultureInfo? cultureInfo = null, IStringLocalizer? stringLocalizer = null) : base(name)
        {
            _logger = logger;
            _input = input;
            _fishingLocalizedString = stringLocalizer == null ? "钓鱼" : stringLocalizer.WithCultureGet(cultureInfo, "钓鱼");
        }

        protected async override Task<Status> Update()
        {
            var imageRegion = Screenshot.Get();

            if (this.Status == Status.Invalid)
            {
                return Status.Running;
            }

            if (Bv.FindF(imageRegion, _fishingLocalizedString))
            {
                _logger.LogInformation("退出完成");
                return Status.Success;
            }

            Action<int> sleep = Sleep.Get();
            if (Bv.ClickBlackConfirmButton(imageRegion))
            {
                _logger.LogInformation("在“是否退出钓鱼？”界面点击确认");
                sleep(1000);
            }
            else
            {
                _input.Keyboard.KeyPress(VK.VK_ESCAPE);
                sleep(2000);
            }

            return Status.Running;
        }
    }

    /// <summary>
    /// 冒泡-终止检查
    /// 检查 abort 标志，为真则失败，否则成功
    /// </summary>
    public partial class BubbleAbortCheck : Behaviour
    {
        /// <summary>
        /// 不钓啦
        /// </summary>
        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<bool> Abort { get; private set; } = null!;

        private BubbleAbortCheck(string name) : base(name)
        {
        }

        protected async override Task<Status> Update()
        {
            return (Abort.Exists() && Abort.Get()) ? Status.Failure : Status.Success;
        }
    }

    /// <summary>
    /// 抛竿检查
    /// 检查抛竿阶段是否应该终止（abort/无落点/无适用鱼），任一条件为真则失败
    /// </summary>
    public partial class CheckThrowRodResult : Behaviour
    {
        /// <summary>
        /// 不钓啦
        /// </summary>
        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<bool> Abort { get; private set; } = null!;

        /// <summary>
        /// 是否没有抛竿落点
        /// </summary>
        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<bool> ThrowRodNoTarget { get; private set; } = null!;

        /// <summary>
        /// 是否没有鱼饵适用的鱼
        /// </summary>
        [BlackboardKey(Access = Access.Read)]
        public BehaviourKeyAccess<bool> ThrowRodNoBaitFish { get; private set; } = null!;

        private CheckThrowRodResult(string name) : base(name)
        {
        }

        protected async override Task<Status> Update()
        {
            if ((Abort.Exists() && Abort.Get())
                || (ThrowRodNoTarget.Exists() && ThrowRodNoTarget.Get())
                || (ThrowRodNoBaitFish.Exists() && ThrowRodNoBaitFish.Get()))
            {
                return Status.Failure;
            }

            return Status.Running;
        }
    }

    /// <summary>
    /// 截图
    /// </summary>
    public partial class TakeScreenshot : Behaviour
    {
        private readonly ILogger _logger;

        [BlackboardKey(Access = Access.ExclusiveWrite)]
        public BehaviourKeyAccess<ImageRegion> Screenshot { get; private set; } = null!;

        private TakeScreenshot(string name, ILogger logger) : base(name)
        {
            _logger = logger;
        }

        private Mat? _bitmap;
        private CaptureContent? _content;

        protected async override Task<Status> Update()
        {
            _bitmap?.Dispose();
            _bitmap = TaskControl.CaptureGameImageNoRetry(TaskTriggerDispatcher.Instance().GameCapture);
            if (_bitmap == null)
            {
                _logger.LogWarning("截图失败");
                return Status.Failure;
            }

            _content?.Dispose();
            _content = new CaptureContent(_bitmap, 0, 0);

            Screenshot.Set(_content.CaptureRectArea);

            return Status.Success;
        }
    }

    /// <summary>
    /// 设置Sleep
    /// TaskControl.Sleep没有和配置解耦，所以先这样糊
    /// </summary>
    public partial class SetSleep : Behaviour
    {
        [BlackboardKey(Access = Access.ExclusiveWrite)]
        public BehaviourKeyAccess<Action<int>> Sleep { get; private set; } = null!;

        private readonly Action<int> _sleep;

        private SetSleep(string name, Action<int> sleep) : base(name)
        {
            this._sleep = sleep;
        }

        protected async override Task<Status> Update()
        {
            Sleep.Set(this._sleep);

            return Status.Success;
        }
    }
}
