using BehaviourTree.Composites;
using BehaviourTree.FluentBuilder;
using BehaviourTree;
using System;
using System.Collections.Generic;
using System.Text;

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
}
