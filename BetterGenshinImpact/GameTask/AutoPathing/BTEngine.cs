using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// ==========================================
/// 行为树基础引擎 / Behavior Tree Foundation Engine
/// ==========================================

public enum BTStatus { Success, Failure, Running }

public interface IBTNode
{
    Task<BTStatus> TickAsync();
}

public class BTAction : IBTNode
{
    private readonly Func<Task<BTStatus>> _action;
    public BTAction(Func<Task<BTStatus>> action) => _action = action;
    public BTAction(Func<BTStatus> action) => _action = () => Task.FromResult(action());
    public BTAction(Action action) => _action = () => { action(); return Task.FromResult(BTStatus.Success); };
    public Task<BTStatus> TickAsync() => _action();
}

public class BTCondition : IBTNode
{
    private readonly Func<bool> _condition;
    public BTCondition(Func<bool> condition) => _condition = condition;
    public Task<BTStatus> TickAsync() => Task.FromResult(_condition() ? BTStatus.Success : BTStatus.Failure);
}

// 异步条件节点
public class BTConditionAsync : IBTNode
{
    private readonly Func<Task<bool>> _condition;
    public BTConditionAsync(Func<Task<bool>> condition) => _condition = condition;
    public async Task<BTStatus> TickAsync() => await _condition() ? BTStatus.Success : BTStatus.Failure;
}

// 顺序节点：遇到第一个非 Success 的节点即返回该状态（中断后续执行）
public class BTSequence : IBTNode
{
    private readonly IBTNode[] _nodes;
    public BTSequence(params IBTNode[] nodes) => _nodes = nodes;
    public async Task<BTStatus> TickAsync()
    {
        foreach (var node in _nodes)
        {
            var status = await node.TickAsync();
            if (status != BTStatus.Success) return status;
        }
        return BTStatus.Success;
    }
}

// 选择节点：遇到第一个非 Failure 的节点即返回该状态（具有 Fallback 特性）
public class BTSelector : IBTNode
{
    private readonly IBTNode[] _nodes;
    public BTSelector(params IBTNode[] nodes) => _nodes = nodes;
    public async Task<BTStatus> TickAsync()
    {
        foreach (var node in _nodes)
        {
            var status = await node.TickAsync();
            if (status != BTStatus.Failure) return status;
        }
        return BTStatus.Failure;
    }
}
