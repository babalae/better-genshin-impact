using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Tasks;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// 单个任务节点在一次执行中的上下文。
/// 它负责把节点元数据与事件发布能力打包传给具体任务实例。
/// </summary>
public sealed class GearTaskNodeExecutionContext
{
    private readonly IGearTaskEventBus _eventBus;

    public GearTaskNodeExecutionContext(
        IGearTaskEventBus eventBus,
        string recordId,
        string taskDefinitionName,
        string taskDefinitionFileKey,
        string nodeId,
        string? parentNodeId,
        string taskName,
        string taskType,
        string taskPath,
        int nodeDepth,
        int nodeOrder,
        string? resumeTokenJson)
    {
        _eventBus = eventBus;
        RecordId = recordId;
        TaskDefinitionName = taskDefinitionName;
        TaskDefinitionFileKey = taskDefinitionFileKey;
        NodeId = nodeId;
        ParentNodeId = parentNodeId;
        TaskName = taskName;
        TaskType = taskType;
        TaskPath = taskPath;
        NodeDepth = nodeDepth;
        NodeOrder = nodeOrder;
        ResumeTokenJson = resumeTokenJson;
    }

    /// <summary>
    /// 使用任务节点和运行上下文创建节点执行上下文。
    /// </summary>
    public static GearTaskNodeExecutionContext Create(
        IGearTaskEventBus eventBus,
        BaseGearTask task,
        GearTaskExecutionRunContext runContext,
        string nodeId,
        string? parentNodeId,
        int nodeDepth,
        int nodeOrder,
        string? resumeTokenJson)
    {
        return new GearTaskNodeExecutionContext(
            eventBus,
            runContext.RecordId,
            runContext.TaskDefinitionName,
            runContext.TaskDefinitionFileKey,
            nodeId,
            parentNodeId,
            task.Name,
            task.Type,
            GearTaskExecutionPathFormatter.FormatTaskPath(task.FilePath),
            nodeDepth,
            nodeOrder,
            resumeTokenJson);
    }

    public string RecordId { get; }

    public string TaskDefinitionName { get; }

    public string TaskDefinitionFileKey { get; }

    public string NodeId { get; }

    public string? ParentNodeId { get; }

    public string TaskName { get; }

    public string TaskType { get; }

    public string TaskPath { get; }

    public int NodeDepth { get; }

    public int NodeOrder { get; }

    /// <summary>
    /// 从历史记录恢复时传入的节点级恢复令牌。
    /// 由实现了 IGearTaskResumable 的任务自行解释。
    /// </summary>
    public string? ResumeTokenJson { get; }

    /// <summary>
    /// 基于当前节点信息构造一条事件，避免各任务重复填公共字段。
    /// </summary>
    public T CreateEvent<T>() where T : GearTaskExecutionEvent, new()
    {
        return new T
        {
            RecordId = RecordId,
            TaskDefinitionName = TaskDefinitionName,
            TaskDefinitionFileKey = TaskDefinitionFileKey,
            NodeId = NodeId,
            ParentNodeId = ParentNodeId,
            TaskName = TaskName,
            TaskType = TaskType,
            TaskPath = TaskPath,
            NodeDepth = NodeDepth,
            NodeOrder = NodeOrder,
        };
    }

    /// <summary>
    /// 发布当前节点相关事件。
    /// </summary>
    public ValueTask PublishAsync(GearTaskExecutionEvent evt, CancellationToken ct = default)
    {
        return _eventBus.PublishAsync(evt, ct);
    }
}
