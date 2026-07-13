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
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public static class BehaviourTreeExtensions
    {
        public static FluentBuilder<TContext> MySimpleParallel<TContext>(this FluentBuilder<TContext> builder, string name, SimpleParallelPolicy policy = SimpleParallelPolicy.BothMustSucceed)
        {
            return builder.PushComposite((IBehaviour<TContext>[] children) => new MySimpleParallel<TContext>(name, policy, children));
        }
    }

    /// <summary>
    /// MySimpleParallel
    /// 和SimpleParallel的区别是，任一子行为返回失败则返回失败；并且支持两个以上子行为
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    public class MySimpleParallel<TContext> : CompositeBehaviour<TContext>
    {
        private readonly Func<TContext, BehaviourStatus> _behave;

        public readonly SimpleParallelPolicy Policy;

        public MySimpleParallel(SimpleParallelPolicy policy, IBehaviour<TContext>[] children)
            : this("SimpleParallel", policy, children)
        {
        }

        public MySimpleParallel(string name, SimpleParallelPolicy policy, IBehaviour<TContext>[] children)
            : base(name, children)
        {
            Policy = policy;
            _behave = ((policy == SimpleParallelPolicy.BothMustSucceed) ? new Func<TContext, BehaviourStatus>(BothMustSucceedBehaviour) : new Func<TContext, BehaviourStatus>(OnlyOneMustSucceedBehaviour));
        }

        private BehaviourStatus OnlyOneMustSucceedBehaviour(TContext context)
        {
            if (Children.Any(c => c.Status == BehaviourStatus.Succeeded))
            {
                return BehaviourStatus.Succeeded;
            }

            if (Children.All(c => c.Status == BehaviourStatus.Failed))
            {
                return BehaviourStatus.Failed;
            }

            return BehaviourStatus.Running;
        }

        private BehaviourStatus BothMustSucceedBehaviour(TContext context)
        {
            if (Children.All(c => c.Status == BehaviourStatus.Succeeded))
            {
                return BehaviourStatus.Succeeded;
            }

            if (Children.Any(c => c.Status == BehaviourStatus.Failed))
            {
                return BehaviourStatus.Failed;
            }

            return BehaviourStatus.Running;
        }

        protected override BehaviourStatus Update(TContext context)
        {
            if (base.Status != BehaviourStatus.Running)
            {
                foreach (var child in Children)
                {
                    child.Tick(context);
                }
            }
            else
            {
                foreach (var child in Children)
                {
                    if (child.Status == BehaviourStatus.Ready || child.Status == BehaviourStatus.Running)
                    {
                        child.Tick(context);
                    }
                }
            }

            if (Children.Any(c => c.Status == BehaviourStatus.Failed))
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
            base.DoReset(status);
        }
    }

    /// <summary>
    /// 原库的BaseBehaviour继承后也无法改写Tick方法，只得另起此实现类
    /// 暂时直接覆盖行为树原本的BaseBehaviour
    /// </summary>
    public abstract class BaseBehaviour<TImageRegion> : IBehaviour<TImageRegion>, IDisposable where TImageRegion : ImageRegion
    {
        protected readonly bool saveScreenshotOnTerminate;

        protected readonly ILogger logger;
        public string Name { get; }

        public BehaviourStatus Status { get; private set; }

        protected BaseBehaviour(string name, ILogger logger, bool saveScreenshotOnTerminate)
        {
            Name = name;
            this.logger = logger;
            this.saveScreenshotOnTerminate = saveScreenshotOnTerminate;
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
                if (saveScreenshotOnTerminate)
                {
                    string fileName = $"{DateTime.Now:yyyyMMddHHmmssfff}_{this.GetType().Name}_{Status}.png";
                    logger.LogInformation("保存截图: {Name}", fileName);
                    SaveScreenshot(context, fileName);
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

            if (String.IsNullOrWhiteSpace(name))
            {
                name = $@"{DateTime.Now:yyyyMMddHHmmssffff}.png";
            }
            var savePath = Global.Absolute($@"log\screenshot\{name}");

            var mat = imageRegion.SrcMat;
            if (TaskContext.Instance().Config.CommonConfig.ScreenshotUidCoverEnabled)
            {
                new Task(() =>
                {
                    using var mat2 = mat.Clone();
                    var assetScale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
                    var rect = new Rect((int)(mat.Width - MaskWindowConfig.UidCoverRightBottomRect.X * assetScale),
                        (int)(mat.Height - MaskWindowConfig.UidCoverRightBottomRect.Y * assetScale),
                        (int)(MaskWindowConfig.UidCoverRightBottomRect.Width * assetScale),
                        (int)(MaskWindowConfig.UidCoverRightBottomRect.Height * assetScale));
                    mat2.Rectangle(rect, Scalar.White, -1);
                    Cv2.ImWrite(savePath, mat2);
                }).Start();
            }
            else
            {
                new Task(() =>
                {
                    Cv2.ImWrite(savePath, mat);
                }).Start();
            }
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
