using BehaviourTree.Composites;
using BehaviourTree.FluentBuilder;
using BehaviourTree;
using System;
using System.Collections.Generic;
using System.Text;
using BetterGenshinImpact.Core.Config;
using OpenCvSharp;
using System.IO;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.GameTask.Model.Area;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public static class BehaviourTreeExtensions
    {
        public static FluentBuilder<TContext> MySimpleParallel<TContext>(this FluentBuilder<TContext> builder, string name, SimpleParallelPolicy policy = SimpleParallelPolicy.BothMustSucceed)
        {
            return builder.PushComposite((IBehaviour<TContext>[] children) => new MySimpleParallel<TContext>(name, policy, children[0], children[1]));
        }
    }

    /// <summary>
    /// MySimpleParallel
    /// 和SimpleParallel的区别是，任一子行为返回失败则返回失败
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    public class MySimpleParallel<TContext> : CompositeBehaviour<TContext>
    {
        private readonly IBehaviour<TContext> _first;

        private readonly IBehaviour<TContext> _second;

        private BehaviourStatus _firstStatus;

        private BehaviourStatus _secondStatus;

        private readonly Func<TContext, BehaviourStatus> _behave;

        public readonly SimpleParallelPolicy Policy;

        public MySimpleParallel(SimpleParallelPolicy policy, IBehaviour<TContext> first, IBehaviour<TContext> second)
            : this("SimpleParallel", policy, first, second)
        {
        }

        public MySimpleParallel(string name, SimpleParallelPolicy policy, IBehaviour<TContext> first, IBehaviour<TContext> second)
            : base(name, new IBehaviour<TContext>[2] { first, second })
        {
            Policy = policy;
            _first = first;
            _second = second;
            _behave = ((policy == SimpleParallelPolicy.BothMustSucceed) ? new Func<TContext, BehaviourStatus>(BothMustSucceedBehaviour) : new Func<TContext, BehaviourStatus>(OnlyOneMustSucceedBehaviour));
        }

        private BehaviourStatus OnlyOneMustSucceedBehaviour(TContext context)
        {
            if (_firstStatus == BehaviourStatus.Succeeded || _secondStatus == BehaviourStatus.Succeeded)
            {
                return BehaviourStatus.Succeeded;
            }

            if (_firstStatus == BehaviourStatus.Failed && _secondStatus == BehaviourStatus.Failed)
            {
                return BehaviourStatus.Failed;
            }

            return BehaviourStatus.Running;
        }

        private BehaviourStatus BothMustSucceedBehaviour(TContext context)
        {
            if (_firstStatus == BehaviourStatus.Succeeded && _secondStatus == BehaviourStatus.Succeeded)
            {
                return BehaviourStatus.Succeeded;
            }

            if (_firstStatus == BehaviourStatus.Failed || _secondStatus == BehaviourStatus.Failed)
            {
                return BehaviourStatus.Failed;
            }

            return BehaviourStatus.Running;
        }

        protected override BehaviourStatus Update(TContext context)
        {
            if (base.Status != BehaviourStatus.Running)
            {
                _firstStatus = _first.Tick(context);
                _secondStatus = _second.Tick(context);
            }
            else
            {
                if (_firstStatus == BehaviourStatus.Ready || _firstStatus == BehaviourStatus.Running)
                {
                    _firstStatus = _first.Tick(context);
                }

                if (_secondStatus == BehaviourStatus.Ready || _secondStatus == BehaviourStatus.Running)
                {
                    _secondStatus = _second.Tick(context);
                }
            }

            if (_firstStatus == BehaviourStatus.Failed || _secondStatus == BehaviourStatus.Failed)
            {
                return BehaviourStatus.Failed;
            }
            else
            {
                return _behave(context);
            }
        }

        protected override void DoReset(BehaviourStatus status)
        {
            _firstStatus = BehaviourStatus.Ready;
            _secondStatus = BehaviourStatus.Ready;
            base.DoReset(status);
        }
    }

    /// <summary>
    /// 原库的BaseBehaviour继承后也无法改写Tick方法，只得另起此实现类
    /// 暂时直接覆盖行为树原本的BaseBehaviour
    /// </summary>
    public abstract class BaseBehaviour<TImageRegion> : IBehaviour<TImageRegion>, IDisposable where TImageRegion : ImageRegion
    {
        public static bool SaveScreenshotOnTerminate;

        private readonly ILogger logger;
        public string Name { get; }

        public BehaviourStatus Status { get; private set; }

        protected BaseBehaviour(string name, ILogger logger)
        {
            Name = name;
            this.logger = logger;
        }

        public BehaviourStatus Tick(TImageRegion context)
        {
            if (Status == BehaviourStatus.Ready)
            {
                OnInitialize();
            }

            Status = Update(context);
            if (Status == BehaviourStatus.Ready)
            {
                throw new InvalidOperationException("Ready status should not be returned by Behaviour Update Method");
            }

            if (Status != BehaviourStatus.Running)
            {
                if (SaveScreenshotOnTerminate)
                {
                    string name = $"{DateTime.Now:yyyyMMddHHmmssfff}_{this.GetType().Name}_{Status}.png";
                    logger.LogInformation("保存截图: {Name}", name);
                    SaveScreenshot(context, name);
                }
                OnTerminate(Status);
            }

            return Status;
        }

        public virtual void SaveScreenshot(ImageRegion imageRegion, string? name)
        {
            var path = Global.Absolute($@"log\screenshot\");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var bitmap = imageRegion.SrcBitmap;
            if (String.IsNullOrWhiteSpace(name))
            {
                name = $@"{DateTime.Now:yyyyMMddHHmmssffff}.png";
            }
            var savePath = Global.Absolute($@"log\screenshot\{name}");

            var mat = imageRegion.SrcMat;
            if (TaskContext.Instance().Config.CommonConfig.ScreenshotUidCoverEnabled)
            {
                var rect = TaskContext.Instance().Config.MaskWindowConfig.UidCoverRect;
                mat.Rectangle(rect, Scalar.White, -1);
            }
            new Task(() =>
            {
                Cv2.ImWrite(savePath, mat);
            }).Start();
        }

        public void Reset()
        {
            if (Status != 0)
            {
                DoReset(Status);
                Status = BehaviourStatus.Ready;
            }
        }

        protected abstract BehaviourStatus Update(TImageRegion context);

        protected virtual void OnTerminate(BehaviourStatus status)
        {
        }

        protected virtual void OnInitialize()
        {
        }

        protected virtual void DoReset(BehaviourStatus status)
        {
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
