using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 一个已注册独立任务的描述：身份、参数与结果类型、设置界面，以及按任务标识动态创建和运行任务的入口。
/// 调用委托在注册时生成，执行期不再使用反射。
/// </summary>
public sealed class SoloTaskDescriptor
{
    /// <summary>
    /// 创建任务实例的工厂，运行环境（以及可选的配置根）作为显式参数，其余依赖从 DI 解析。
    /// </summary>
    private readonly ObjectFactory _factory;

    /// <summary>
    /// 任务构造函数是否接收配置根。
    /// </summary>
    private readonly bool _acceptsConfig;

    /// <summary>
    /// 调用强类型 RunAsync 的委托。
    /// </summary>
    private readonly Func<object, object, CancellationToken, Task<ISoloTaskResult>> _run;

    /// <summary>
    /// 稳定任务标识。
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// 显示名。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 用途说明。
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 菜单分组。
    /// </summary>
    public string Group { get; }

    /// <summary>
    /// 分组内排序。
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// 可出现的运行入口。
    /// </summary>
    public SoloTaskScopes Scopes { get; }

    /// <summary>
    /// 新启动截图器时是否先等待进入主界面。
    /// </summary>
    public bool WaitForMainUi { get; }

    /// <summary>
    /// 一条龙默认清单中的顺序；0 表示不默认加入。
    /// </summary>
    public int OneDragonOrder { get; }

    /// <summary>
    /// 任务实现类型。
    /// </summary>
    public Type TaskType { get; }

    /// <summary>
    /// 参数类型。
    /// </summary>
    public Type ParamType { get; }

    /// <summary>
    /// 附加结果数据类型。
    /// </summary>
    public Type DataType { get; }

    /// <summary>
    /// 专属设置视图类型；没有时为 null。
    /// </summary>
    public Type? SettingsViewType { get; }

    /// <summary>
    /// 专属设置 VM 类型；没有时为 null。
    /// </summary>
    public Type? SettingsViewModelType { get; }

    /// <summary>
    /// 参数是否为空参数。
    /// </summary>
    public bool HasParameters => ParamType != typeof(NoParam);

    /// <summary>
    /// 按任务类型和特性建立描述符。
    /// </summary>
    /// <param name="taskType">任务实现类型。</param>
    /// <param name="info">任务声明特性。</param>
    /// <param name="paramType">参数类型。</param>
    /// <param name="dataType">附加数据类型。</param>
    /// <param name="settingsView">专属设置界面声明，可为空。</param>
    /// <param name="constructor">任务唯一的公共构造函数。</param>
    internal SoloTaskDescriptor(Type taskType, SoloTaskInfoAttribute info, Type paramType, Type dataType,
        SoloTaskSettingsViewAttribute? settingsView, ConstructorInfo constructor)
    {
        TaskType = taskType;
        TaskId = info.Id;
        DisplayName = info.DisplayName;
        Description = info.Description;
        Group = info.Group;
        Order = info.Order;
        Scopes = info.Scopes;
        WaitForMainUi = info.WaitForMainUi;
        OneDragonOrder = info.OneDragonOrder;
        ParamType = paramType;
        DataType = dataType;
        SettingsViewType = settingsView?.ViewType;
        SettingsViewModelType = settingsView?.ViewModelType;

        // 运行环境必须显式传入；构造函数接收配置根时也显式传入，便于运行服务使用磁盘上的最新配置
        _acceptsConfig = constructor.GetParameters().Any(p => p.ParameterType == typeof(AllConfig));
        Type[] argumentTypes = _acceptsConfig ? [typeof(GameRuntime), typeof(AllConfig)] : [typeof(GameRuntime)];
        _factory = ActivatorUtilities.CreateFactory(taskType, argumentTypes);

        _run = typeof(SoloTaskDescriptor)
            .GetMethod(nameof(RunTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(paramType, dataType)
            .CreateDelegate<Func<object, object, CancellationToken, Task<ISoloTaskResult>>>();
    }

    /// <summary>
    /// 创建默认参数。
    /// </summary>
    /// <returns>参数类的无参构造结果。</returns>
    public object CreateDefaultParam() => Activator.CreateInstance(ParamType)!;

    /// <summary>
    /// 按统一 JSON 约定序列化参数。
    /// </summary>
    /// <param name="param">参数对象。</param>
    /// <returns>JSON 文本。</returns>
    public string SerializeParam(object param) => SoloTaskJson.Serialize(param, ParamType);

    /// <summary>
    /// 按统一 JSON 约定反序列化参数。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <returns>参数对象。</returns>
    public object DeserializeParam(string json) => SoloTaskJson.Deserialize(json, ParamType);

    /// <summary>
    /// 在拿到运行环境之后创建任务实例。
    /// </summary>
    /// <param name="services">用于解析其余依赖的服务容器。</param>
    /// <param name="runtime">本次运行的运行环境。</param>
    /// <param name="config">配置根；为空时从服务容器解析。</param>
    /// <returns>任务实例。</returns>
    public object CreateTask(IServiceProvider services, GameRuntime runtime, AllConfig? config = null)
    {
        object?[] arguments = _acceptsConfig
            ? [runtime, config ?? services.GetRequiredService<AllConfig>()]
            : [runtime];
        return _factory(services, arguments);
    }

    /// <summary>
    /// 运行已创建的任务实例，任务内部的提前结束异常会被转换为结果。
    /// </summary>
    /// <param name="task">由 <see cref="CreateTask"/> 创建的任务实例。</param>
    /// <param name="param">参数对象，类型必须是 <see cref="ParamType"/>。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>无类型结果视图。</returns>
    public Task<ISoloTaskResult> RunAsync(object task, object param, CancellationToken ct) => _run(task, param, ct);

    /// <summary>
    /// 创建并运行任务。供已经持有会话的入口（如任务计划）使用，不获取任务锁。
    /// </summary>
    /// <param name="services">用于解析其余依赖的服务容器。</param>
    /// <param name="runtime">本次运行的运行环境。</param>
    /// <param name="param">参数对象。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="config">配置根；为空时从服务容器解析。</param>
    /// <returns>无类型结果视图。</returns>
    public Task<ISoloTaskResult> InvokeAsync(IServiceProvider services, GameRuntime runtime, object param,
        CancellationToken ct, AllConfig? config = null)
        => RunAsync(CreateTask(services, runtime, config), param, ct);

    /// <summary>
    /// 调用强类型任务的 ExecuteAsync，并把结果转换为无类型视图。
    /// </summary>
    private static async Task<ISoloTaskResult> RunTypedAsync<TParam, TData>(object task, object param, CancellationToken ct)
        where TParam : class, new()
        where TData : class
        => await ((ISoloTask<TParam, TData>)task).ExecuteAsync((TParam)param, ct);
}
